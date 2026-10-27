"""Regime assessment for one symbol: fit, label, and look for a turning point.

Everything is descriptive and computed by code. A regime label is a name for
a cluster of (return, volume, range) behaviour the model found in this
symbol's own history; it is not a forecast and not a recommendation.
"""
from __future__ import annotations

import math
from datetime import date, datetime, timezone
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, field_validator

from options_research.market.bars import Bar
from options_research.market.snapshot import CalculationReceipt
from options_research.regime.features import FEATURE_NAMES, FeatureMatrix, build_features
from options_research.regime.hmm import GaussianHMM, HMMFit
from options_research.timeutil import require_utc

Flag = Literal["FRESH", "WEAKENING", "OVERDUE", "STABLE", "UNAVAILABLE"]


class RegimeConfig(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    states: int = 3
    restarts: int = 3
    seed: int = 7
    max_iter: int = 150
    tol: float = 1e-4
    min_bars: int = 120
    volume_median_days: int = 20
    winsor: float = 5.0
    feature_window: int = 5
    sticky_prior: float = 20.0
    trend_threshold_pct: float = 0.05            # mean daily return beyond +/- this -> BULL / BEAR, else FLAT
    weak_posterior: float = 0.70
    drift_days: int = 5
    drift_threshold: float = -0.25
    fresh_days: int = 3
    overdue_ratio: float = 1.5

    @classmethod
    def from_raw(cls, raw: dict) -> "RegimeConfig":
        r = raw.get("regime", {})
        return cls(**{k: r[k] for k in cls.model_fields if k in r})


class RegimeState(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    index: int
    label: str                                   # {BULL,BEAR,FLAT}-{V,M,Q}: trend, then range rank (+n if repeated)
    descriptor: str                              # e.g. "heavy-volume, wide-range"
    mean_daily_return_pct: float
    mean_rel_volume: float                       # volume / 20-day median, geometric mean
    mean_range_pct: float                        # high/low - 1
    stay_prob: float
    expected_duration_days: float
    stationary_prob: float
    share_of_history: float


class RegimeAssessment(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    symbol: str
    as_of: datetime                               # close time of the last bar used
    bars_in: int
    rows_used: int
    n_states: int
    unavailable_reason: str | None = None
    fit_log_likelihood: float | None = None
    converged: bool | None = None
    iterations: int | None = None
    states: list[RegimeState] = Field(default_factory=list)
    transition: list[list[float]] = Field(default_factory=list)
    current: str | None = None
    current_index: int | None = None
    days_in_regime: int | None = None
    p_current: float | None = None
    p_switch_next_day: float | None = None
    expected_duration_days: float | None = None
    drift: float | None = None                    # change in P(current) over drift_days (negative = weakening)
    drift_days: int | None = None
    runner_up: str | None = None
    p_runner_up: float | None = None
    last_transition: date | None = None
    transitions_60d: int | None = None
    flag: Flag = "UNAVAILABLE"
    turning_point: bool = False
    notes: list[str] = Field(default_factory=list)
    posterior_tail: list[list[float]] = Field(default_factory=list)   # last drift_days+1 rows of P(state)
    receipt_ids: list[str] = Field(default_factory=list)

    _utc = field_validator("as_of")(require_utc)


def label_states(fit: HMMFit, fm: FeatureMatrix, gamma: list[list[float]],
                 trend_threshold_pct: float = 0.05) -> list[RegimeState]:
    """Name states by trend and volatility.

    BULL / BEAR when the state's mean daily return is beyond +/- `trend_threshold_pct`
    (in percent per day), otherwise FLAT; then the state's rank by mean daily range
    among this symbol's states: -V widest, -Q narrowest, -M in between. A repeated
    name gets a numeric suffix.
    """
    k = fit.n_states
    weights = [[g[s] for g in gamma] for s in range(k)]
    stats = []
    for s in range(k):
        w = weights[s]
        sw = sum(w) or 1e-12
        ret = sum(wi * r[0] for wi, r in zip(w, fm.raw)) / sw
        relv = sum(wi * r[1] for wi, r in zip(w, fm.raw)) / sw
        rng = sum(wi * r[2] for wi, r in zip(w, fm.raw)) / sw
        stats.append((ret, relv, rng, sw / len(fm.raw)))
    med_relv = sorted(st[1] for st in stats)[k // 2]
    med_rng = sorted(st[2] for st in stats)[k // 2]
    thr = trend_threshold_pct / 100.0
    by_range = sorted(range(k), key=lambda s: stats[s][2])
    vol_class = {by_range[0]: "Q", by_range[-1]: "V"}
    for s in by_range[1:-1]:
        vol_class[s] = "M"
    labels: dict[int, str] = {}
    for s in range(k):
        ret, _relv, _rng, _share = stats[s]
        trend = "BULL" if ret > thr else ("BEAR" if ret < -thr else "FLAT")
        labels[s] = f"{trend}-{vol_class[s]}"
    counts: dict[str, int] = {}
    for s in range(k):
        counts[labels[s]] = counts.get(labels[s], 0) + 1
    seen: dict[str, int] = {}
    for s in range(k):
        if counts[labels[s]] > 1:
            seen[labels[s]] = seen.get(labels[s], 0) + 1
            labels[s] = f"{labels[s]}{seen[labels[s]]}"
    stationary = fit.stationary()
    out = []
    for s in range(k):
        ret, relv, rng, share = stats[s]
        parts = ["heavy-volume" if relv > med_relv else ("light-volume" if relv < med_relv else "median-volume"),
                 "wide-range" if rng > med_rng else ("narrow-range" if rng < med_rng else "median-range")]
        out.append(RegimeState(index=s, label=labels[s], descriptor=", ".join(parts), mean_daily_return_pct=100 * ret,
                               mean_rel_volume=math.exp(relv), mean_range_pct=100 * (math.exp(rng) - 1),
                               stay_prob=fit.transition[s][s], expected_duration_days=fit.expected_duration(s),
                               stationary_prob=stationary[s], share_of_history=share))
    return out


def assess(symbol: str, bars: list[Bar], cfg: RegimeConfig) -> RegimeAssessment:
    as_of = bars[-1].close_time if bars else datetime.fromtimestamp(0, tz=timezone.utc)
    fm = build_features(bars, volume_median_days=cfg.volume_median_days, winsor=cfg.winsor,
                        window=cfg.feature_window)
    base = dict(symbol=symbol, as_of=as_of, bars_in=len(bars), rows_used=fm.n, n_states=cfg.states)
    if fm.n < cfg.min_bars:
        return RegimeAssessment(**base, unavailable_reason=f"only {fm.n} usable daily bars; need {cfg.min_bars}")
    model = GaussianHMM(n_states=cfg.states, seed=cfg.seed, restarts=cfg.restarts, max_iter=cfg.max_iter, tol=cfg.tol,
                        sticky_prior=cfg.sticky_prior)
    fit = model.fit(fm.rows)
    gamma = model.posteriors(fit, fm.rows)
    states = label_states(fit, fm, gamma, cfg.trend_threshold_pct)
    path = [max(range(fit.n_states), key=lambda s: g[s]) for g in gamma]
    cur = path[-1]
    run = 1
    while run < len(path) and path[-1 - run] == cur:
        run += 1
    p_cur = gamma[-1][cur]
    p_switch = sum(gamma[-1][s] * (1.0 - fit.transition[s][s]) for s in range(fit.n_states))
    exp_dur = fit.expected_duration(cur)
    dd = min(cfg.drift_days, len(gamma) - 1)
    drift = gamma[-1][cur] - gamma[-1 - dd][cur]
    others = [s for s in range(fit.n_states) if s != cur]
    runner = max(others, key=lambda s: gamma[-1][s])
    transitions = [i for i in range(1, len(path)) if path[i] != path[i - 1]]
    last_tr = fm.times[transitions[-1]].date() if transitions else None
    tr_60 = sum(1 for i in transitions if i >= len(path) - 60)
    notes: list[str] = []
    flag: Flag = "STABLE"
    if run <= cfg.fresh_days:
        flag = "FRESH"
        notes.append(f"regime changed {run - 1} bar(s) ago from {states[path[-1 - run]].label if len(path) > run else '?'}")
    elif p_cur < cfg.weak_posterior or drift < cfg.drift_threshold:
        flag = "WEAKENING"
        if p_cur < cfg.weak_posterior:
            notes.append(f"P({states[cur].label}) = {p_cur:.2f} below {cfg.weak_posterior:.2f}")
        if drift < cfg.drift_threshold:
            notes.append(f"P({states[cur].label}) fell {abs(drift):.2f} over {dd} bars")
    elif exp_dur != float("inf") and run > cfg.overdue_ratio * exp_dur:
        flag = "OVERDUE"
        notes.append(f"{run} bars in regime vs expected {exp_dur:.0f}; posterior still firm")
    if tr_60 >= 6:
        notes.append(f"{tr_60} regime changes in the last 60 bars: choppy, labels less reliable")
    if not fit.converged:
        notes.append(f"EM did not converge in {fit.iterations} iterations")
    warmup = (min(5, cfg.volume_median_days) - 1) + (cfg.feature_window - 1)   # baseline bars + window bars
    if fm.rows_skipped > warmup:
        notes.append(f"{fm.rows_skipped - warmup} bar(s) skipped beyond the {warmup}-bar warm-up (non-finite values)")
    receipts = [
        CalculationReceipt.make("regime_hmm_fit", {"features": fm.inputs_hash, "states": cfg.states, "seed": cfg.seed,
                                                   "restarts": cfg.restarts, "winsor": cfg.winsor,
                                                   "window": cfg.feature_window, "sticky_prior": cfg.sticky_prior,
                                                   "feature_names": list(FEATURE_NAMES)},
                                "log-likelihood", ["diagonal Gaussian emissions on z-scored trailing-window features",
                                                   "Baum-Welch EM (MAP with sticky Dirichlet prior on self-transitions), "
                                                   "seeded k-means++ init, best of restarts",
                                                   "labels by trend threshold and cross-state range median; descriptive"],
                                fit.log_likelihood),
        CalculationReceipt.make("regime_p_switch_next_day", {"features": fm.inputs_hash, "states": cfg.states,
                                                             "seed": cfg.seed},
                                "probability", ["sum_k P(state_k at last bar) * (1 - A[k][k])",
                                                "one-step model hazard, not a forecast of price"], p_switch),
    ]
    return RegimeAssessment(
        **base, fit_log_likelihood=fit.log_likelihood, converged=fit.converged, iterations=fit.iterations,
        states=states, transition=fit.transition, current=states[cur].label, current_index=cur, days_in_regime=run,
        p_current=p_cur, p_switch_next_day=p_switch, expected_duration_days=exp_dur, drift=drift, drift_days=dd,
        runner_up=states[runner].label, p_runner_up=gamma[-1][runner], last_transition=last_tr, transitions_60d=tr_60,
        flag=flag, turning_point=flag in ("FRESH", "WEAKENING"), notes=notes,
        posterior_tail=[[round(v, 4) for v in g] for g in gamma[-(dd + 1):]],
        receipt_ids=[r.receipt_id for r in receipts],
    )
