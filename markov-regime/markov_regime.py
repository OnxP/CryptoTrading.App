"""
Observable Markov regime model -- the "hedge fund method" from the video,
steps 1-9 (rule-based). Step 10 (the HMM that *learns* the states) comes later.

Pipeline
--------
  1. States .............. bull / sideways / bear
  2. Label each bar ...... by trailing N-period cumulative return vs +/- threshold
  3. Markov property ..... next state depends only on the current state
  4. Transition matrix ... 3x3, row-normalised (rows sum to 1)
  5. Stickiness .......... the diagonal (P[stay in same state])
  6. n-step forecast ..... matrix ** n  (squaring/cubing the matrix)
  7. Stationary dist. .... the long-run mix (matrix ** inf)
  8. Signal .............. P(bull next) - P(bear next); sign = direction, |.| = size
  9. Walk-forward ........ matrix rebuilt from past-only data at every step (no lookahead)

The descriptive sections (4-8) use the full-sample matrix -- this mirrors the
video's TradingView indicator, which shows the matrix over all visible history.
The walk-forward backtest (9) is the honest test: at each bar it only knows the
transitions that had already happened.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass

import numpy as np
import pandas as pd

# State indices -- ordered bull/sideways/bear so the printed matrix reads like
# the video's grid (bull row first). Signal = row[BULL] - row[BEAR].
BULL, SIDE, BEAR = 0, 1, 2
STATE_NAMES = ["bull", "sideways", "bear"]

# Raw CSV column layout (no header), matching the DualRegime loader.
CSV_COLS = [
    "close_time", "open_time", "open", "high", "low", "close",
    "volume_btc", "volume_quote", "trades", "symbol", "tf_id",
]


@dataclass
class Config:
    csv_path: str = r"C:\Temp\BTCUSDT 4Hour.csv"
    bars_per_day: int = 6          # 4H bars -> 6 per day
    lookback_days: float = 20.0    # the video's "last 20 days"
    bull_thr: float = 5.0          # cumulative % return >= this -> bull
    bear_thr: float = -5.0         # cumulative % return <= this -> bear
    fee_bps: float = 5.0           # cost per unit of turnover, one side, in bps
    warmup_days: float = 60.0      # min history before the walk-forward starts trading

    @property
    def lookback_bars(self) -> int:
        return max(1, round(self.lookback_days * self.bars_per_day))

    @property
    def warmup_bars(self) -> int:
        return max(self.lookback_bars + 5, round(self.warmup_days * self.bars_per_day))

    @property
    def bars_per_year(self) -> float:
        return self.bars_per_day * 365.0


# ----------------------------------------------------------------------------
# 1-2. Load + label states
# ----------------------------------------------------------------------------
def load_bars(csv_path: str) -> pd.DataFrame:
    df = pd.read_csv(csv_path, header=None, names=CSV_COLS)
    df["ts"] = pd.to_datetime(df["close_time"])
    df = df.sort_values("ts").reset_index(drop=True)
    for c in ("open", "high", "low", "close", "volume_btc"):
        df[c] = pd.to_numeric(df[c], errors="coerce")
    return df


def label_states(df: pd.DataFrame, cfg: Config) -> pd.DataFrame:
    """Cumulative % return over the trailing lookback window -> {bull, sideways, bear}.

    cum_ret[t] = close[t] / close[t - lookback] - 1, in percent. This is the
    "20% gain over 20 days" the video describes (compounding vs summing simple
    returns differs only trivially at these thresholds).
    """
    df = df.copy()
    df["cum_ret"] = df["close"].pct_change(cfg.lookback_bars) * 100.0

    state = np.full(len(df), -1, dtype=int)  # -1 = warmup / undefined
    defined = df["cum_ret"].notna().to_numpy()
    cr = df["cum_ret"].to_numpy()
    state[defined & (cr >= cfg.bull_thr)] = BULL
    state[defined & (cr <= cfg.bear_thr)] = BEAR
    state[defined & (cr < cfg.bull_thr) & (cr > cfg.bear_thr)] = SIDE
    df["state"] = state
    return df


# ----------------------------------------------------------------------------
# 4. Transition matrix
# ----------------------------------------------------------------------------
def transition_counts(states: np.ndarray) -> np.ndarray:
    """3x3 counts[i, j] = # of times state i was followed by state j.

    Only consecutive bars where BOTH states are defined (>= 0) are counted.
    """
    counts = np.zeros((3, 3), dtype=float)
    for a, b in zip(states[:-1], states[1:]):
        if a >= 0 and b >= 0:
            counts[a, b] += 1.0
    return counts


def to_probabilities(counts: np.ndarray) -> np.ndarray:
    probs = np.zeros_like(counts)
    row_sums = counts.sum(axis=1)
    for i in range(3):
        if row_sums[i] > 0:
            probs[i] = counts[i] / row_sums[i]
    return probs


# ----------------------------------------------------------------------------
# 7. Stationary distribution (long-run mix)
# ----------------------------------------------------------------------------
def stationary_distribution(probs: np.ndarray) -> np.ndarray:
    """Left eigenvector of P for eigenvalue 1, normalised to sum to 1."""
    if probs.sum() == 0:
        return np.full(3, np.nan)
    vals, vecs = np.linalg.eig(probs.T)
    idx = int(np.argmin(np.abs(vals - 1.0)))
    pi = np.real(vecs[:, idx])
    pi = np.abs(pi)  # eigenvector sign is arbitrary
    total = pi.sum()
    return pi / total if total > 0 else np.full(3, np.nan)


# ----------------------------------------------------------------------------
# 8. Signal
# ----------------------------------------------------------------------------
def signal_for_state(probs: np.ndarray, state: int) -> float:
    """P(bull next) - P(bear next) for the given current state. In [-1, 1]."""
    if state < 0:
        return 0.0
    return float(probs[state, BULL] - probs[state, BEAR])


# ----------------------------------------------------------------------------
# 9. Walk-forward backtest
# ----------------------------------------------------------------------------
@dataclass
class BacktestResult:
    equity: np.ndarray            # strategy equity curve (starts at 1.0)
    equity_bh: np.ndarray         # buy & hold equity curve
    positions: np.ndarray         # position held into each next bar, in [-1, 1]
    n_bars: int
    total_return: float
    bh_return: float
    sharpe: float
    bh_sharpe: float
    frac_long: float
    frac_short: float
    frac_flat: float
    avg_abs_pos: float
    total_turnover: float
    win_rate: float


def walk_forward_backtest(df: pd.DataFrame, cfg: Config) -> BacktestResult:
    """At each bar t (>= warmup): build the matrix from transitions known up to t,
    read the signal for the current state, hold that position into bar t+1, and
    book the realised return minus turnover cost. Counts are updated incrementally,
    which is mathematically identical to rebuilding the whole matrix every step.
    """
    states = df["state"].to_numpy()
    close = df["close"].to_numpy()
    n = len(df)
    warmup = cfg.warmup_bars
    fee = cfg.fee_bps / 1e4

    # Seed counts with every transition strictly inside the warmup region.
    counts = transition_counts(states[: warmup + 1])

    eq, eq_bh = 1.0, 1.0
    prev_pos = 0.0
    equity, equity_bh, positions, rets = [], [], [], []

    for t in range(warmup, n - 1):
        # Incrementally fold in the transition (t-1 -> t): it is known at t and
        # was not yet in `counts` (which covered up to warmup-1 -> warmup).
        if t > warmup and states[t - 1] >= 0 and states[t] >= 0:
            counts[states[t - 1], states[t]] += 1.0

        probs = to_probabilities(counts)
        sig = signal_for_state(probs, states[t])
        pos = float(np.clip(sig, -1.0, 1.0))

        nxt_ret = close[t + 1] / close[t] - 1.0
        turnover = abs(pos - prev_pos)
        pnl = pos * nxt_ret - turnover * fee

        eq *= (1.0 + pnl)
        eq_bh *= (1.0 + nxt_ret)
        equity.append(eq)
        equity_bh.append(eq_bh)
        positions.append(pos)
        rets.append(pnl)
        prev_pos = pos

    equity = np.array(equity)
    equity_bh = np.array(equity_bh)
    positions = np.array(positions)
    rets = np.array(rets)
    bh_rets = np.diff(np.concatenate([[1.0], equity_bh])) / np.concatenate([[1.0], equity_bh])[:-1]

    ann = np.sqrt(cfg.bars_per_year)
    sharpe = float(rets.mean() / rets.std() * ann) if rets.std() > 0 else 0.0
    bh_sharpe = float(bh_rets.mean() / bh_rets.std() * ann) if bh_rets.std() > 0 else 0.0

    return BacktestResult(
        equity=equity,
        equity_bh=equity_bh,
        positions=positions,
        n_bars=len(positions),
        total_return=float(equity[-1] - 1.0) if len(equity) else 0.0,
        bh_return=float(equity_bh[-1] - 1.0) if len(equity_bh) else 0.0,
        sharpe=sharpe,
        bh_sharpe=bh_sharpe,
        frac_long=float(np.mean(positions > 0.01)),
        frac_short=float(np.mean(positions < -0.01)),
        frac_flat=float(np.mean(np.abs(positions) <= 0.01)),
        avg_abs_pos=float(np.mean(np.abs(positions))),
        total_turnover=float(np.sum(np.abs(np.diff(np.concatenate([[0.0], positions]))))),
        win_rate=float(np.mean(rets > 0)) if len(rets) else 0.0,
    )


# ----------------------------------------------------------------------------
# Reporting
# ----------------------------------------------------------------------------
def _fmt_matrix(m: np.ndarray, as_pct: bool = True) -> str:
    hdr = "today \\ next   " + "".join(f"{name:>11}" for name in STATE_NAMES)
    lines = [hdr]
    for i, name in enumerate(STATE_NAMES):
        cells = "".join(
            f"{(m[i, j] * 100):>10.1f}%" if as_pct else f"{m[i, j]:>11.0f}"
            for j in range(3)
        )
        lines.append(f"{name:>12}   {cells}")
    return "\n".join(lines)


def main(cfg: Config) -> None:
    df = load_bars(cfg.csv_path)
    df = label_states(df, cfg)

    print("=" * 64)
    print("  OBSERVABLE MARKOV REGIME MODEL  (video steps 1-9)")
    print("=" * 64)
    print(f"  CSV:           {cfg.csv_path}")
    print(f"  Bars:          {len(df)}  ({df['ts'].iloc[0]:%Y-%m-%d} -> {df['ts'].iloc[-1]:%Y-%m-%d})")
    print(f"  Lookback:      {cfg.lookback_days:g} days = {cfg.lookback_bars} bars")
    print(f"  Thresholds:    bull >= +{cfg.bull_thr:g}% , bear <= {cfg.bear_thr:g}%  (cumulative)")
    print("-" * 64)

    # State distribution
    defined = df[df["state"] >= 0]
    dist = defined["state"].value_counts().reindex([BULL, SIDE, BEAR]).fillna(0).astype(int)
    tot = int(dist.sum())
    print("  State distribution (labelled bars):")
    for s in (BULL, SIDE, BEAR):
        c = int(dist[s])
        print(f"    {STATE_NAMES[s]:>9}: {c:6d}  ({100 * c / tot:5.1f}%)")
    print("-" * 64)

    # 4. Transition matrix (full sample, descriptive)
    counts = transition_counts(df["state"].to_numpy())
    probs = to_probabilities(counts)
    print("  Transition COUNTS (full sample):")
    print(_fmt_matrix(counts, as_pct=False))
    print()
    print("  Transition PROBABILITIES (each row sums to 100%):")
    print(_fmt_matrix(probs, as_pct=True))
    print("-" * 64)

    # 5. Stickiness
    print("  Stickiness (diagonal = P[stay in same state]):")
    for s in (BULL, SIDE, BEAR):
        print(f"    {STATE_NAMES[s]:>9}: {probs[s, s] * 100:5.1f}%")
    print("-" * 64)

    # 7. Stationary distribution
    pi = stationary_distribution(probs)
    print("  Stationary distribution (long-run mix):")
    print("    " + "  ".join(f"{STATE_NAMES[s]}={pi[s] * 100:4.1f}%" for s in (BULL, SIDE, BEAR)))
    print("-" * 64)

    # 6 + 8. Current state, multi-step forecast, signal
    cur = int(df["state"].iloc[-1])
    print(f"  Current state: {STATE_NAMES[cur].upper()}")
    print("  Multi-step forecast from current state (squaring the matrix):")
    print(f"    {'horizon':>8}{'bull':>10}{'sideways':>10}{'bear':>10}")
    for n_step in (1, 2, 7, 30):
        pn = np.linalg.matrix_power(probs, n_step)[cur]
        print(f"    {n_step:>6}d {pn[BULL] * 100:>9.1f}%{pn[SIDE] * 100:>9.1f}%{pn[BEAR] * 100:>9.1f}%")
    sig = signal_for_state(probs, cur)
    direction = "LONG" if sig > 0 else "SHORT" if sig < 0 else "FLAT"
    print(f"  Signal = P(bull) - P(bear) = {sig:+.3f}  ->  {direction}  (size {abs(sig) * 100:.0f}%)")
    print("=" * 64)

    # 9. Walk-forward backtest
    bt = walk_forward_backtest(df, cfg)
    print("  WALK-FORWARD BACKTEST  (matrix rebuilt from past-only data each bar)")
    print("-" * 64)
    print(f"  Traded bars:     {bt.n_bars}")
    print(f"  Strategy return: {bt.total_return * 100:+8.1f}%   Sharpe {bt.sharpe:5.2f}")
    print(f"  Buy & hold:      {bt.bh_return * 100:+8.1f}%   Sharpe {bt.bh_sharpe:5.2f}")
    print(f"  Exposure:        long {bt.frac_long * 100:.0f}% / short {bt.frac_short * 100:.0f}% / flat {bt.frac_flat * 100:.0f}%")
    print(f"  Avg |position|:  {bt.avg_abs_pos:.2f}   total turnover {bt.total_turnover:.1f}   win rate {bt.win_rate * 100:.1f}%")
    print("=" * 64)


def parse_args() -> Config:
    cfg = Config()
    p = argparse.ArgumentParser(description="Observable Markov regime model (video steps 1-9)")
    p.add_argument("--csv", default=cfg.csv_path)
    p.add_argument("--bars-per-day", type=int, default=cfg.bars_per_day)
    p.add_argument("--lookback-days", type=float, default=cfg.lookback_days)
    p.add_argument("--bull", type=float, default=cfg.bull_thr)
    p.add_argument("--bear", type=float, default=cfg.bear_thr)
    p.add_argument("--fee-bps", type=float, default=cfg.fee_bps)
    p.add_argument("--warmup-days", type=float, default=cfg.warmup_days)
    a = p.parse_args()
    return Config(
        csv_path=a.csv, bars_per_day=a.bars_per_day, lookback_days=a.lookback_days,
        bull_thr=a.bull, bear_thr=a.bear, fee_bps=a.fee_bps, warmup_days=a.warmup_days,
    )


if __name__ == "__main__":
    main(parse_args())
