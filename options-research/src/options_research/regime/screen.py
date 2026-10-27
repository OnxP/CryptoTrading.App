"""Low-priced, high-volume screen with a per-symbol regime assessment.

Universe from the Yahoo screener (price band, US exchanges, sorted by day
volume), daily candles from the Yahoo chart endpoint, a Gaussian HMM per
symbol, and the benchmark's regime first. Output is a table and an
append-only ledger record. No quantities, no ranking by attractiveness, no
recommendation: the sort is the screener's own volume sort.
"""
from __future__ import annotations

import json
import uuid
from datetime import datetime
from pathlib import Path
from typing import Callable

from pydantic import BaseModel, ConfigDict, Field, field_validator

from options_research.config import Config
from options_research.connectors.cassette import NoCassette
from options_research.connectors.yahoo import YahooClient, YahooError
from options_research.market.bars import Bar
from options_research.market.sources import MarketDataUnavailable, YahooBarSource
from options_research.regime.assessment import RegimeAssessment, RegimeConfig, assess
from options_research.timeutil import require_utc, utc_now

DISCLOSURES = [
    "Source: Yahoo Finance public endpoints (unofficial; quotes may be delayed; history is split-adjusted).",
    "Regime labels are clusters of each symbol's own return/volume/range history, found by a Gaussian HMM. "
    "They describe the past up to the last complete bar; they are not forecasts.",
    "Turning-point flags are model diagnostics (posterior weakening, a fresh switch, an overdue run). "
    "They are not buy or sell signals and no position size is computed.",
    "Low-priced, high-volume names carry listing, dilution and halt risk that this screen does not assess.",
]


class ScreenParams(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    max_price: float
    min_price: float
    top: int
    sort_field: str
    chart_range: str
    exchanges: list[str]
    region: str
    benchmark: str | None
    regime: RegimeConfig
    symbols: list[str] = Field(default_factory=list)     # explicit watchlist; empty = Yahoo screener universe


class ScreenRow(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    rank: int
    symbol: str
    name: str
    exchange: str
    price: float | None
    day_volume: float | None
    avg_volume_3m: float | None
    market_cap: float | None
    sector: str | None = None
    industry: str | None = None
    country: str | None = None
    quote_time: datetime | None
    assessment: RegimeAssessment | None
    error: str | None = None

    _utc = field_validator("quote_time")(lambda v: require_utc(v) if v is not None else v)


class ScreenRecord(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    screen_id: str
    created_at: datetime
    params: ScreenParams
    universe_total: int
    benchmark: RegimeAssessment | None
    benchmark_error: str | None = None
    rows: list[ScreenRow]
    disclosures: list[str] = Field(default_factory=lambda: list(DISCLOSURES))

    _utc = field_validator("created_at")(require_utc)


class ScreenLedger:
    def __init__(self, path: Path):
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)

    def append(self, rec: ScreenRecord) -> None:
        with open(self.path, "a", encoding="utf-8") as fh:
            fh.write(rec.model_dump_json() + "\n")

    def all(self) -> list[ScreenRecord]:
        if not self.path.exists():
            return []
        with open(self.path, encoding="utf-8") as fh:
            return [ScreenRecord.model_validate_json(line) for line in fh if line.strip()]


def _yahoo_time(q: dict) -> datetime | None:
    t = q.get("regularMarketTime")
    if t is None:
        return None
    from datetime import timezone
    return datetime.fromtimestamp(int(t), tz=timezone.utc)


def run_screen(yahoo: YahooClient, config: Config, max_price: float | None = None, min_price: float | None = None,
               top: int | None = None, benchmark: str | None = None, chart_range: str | None = None,
               progress: Callable[[str], None] | None = None, symbols: list[str] | None = None) -> ScreenRecord:
    s = config.raw.get("screen", {})
    rcfg = RegimeConfig.from_raw(config.raw)
    params = ScreenParams(
        max_price=float(max_price if max_price is not None else s.get("max_price", 10.0)),
        min_price=float(min_price if min_price is not None else s.get("min_price", 1.0)),
        top=int(top if top is not None else s.get("top", 50)),
        sort_field=str(s.get("sort_field", "dayvolume")),
        chart_range=chart_range or yahoo.chart_range,
        exchanges=list(yahoo.exchanges), region=yahoo.region,
        benchmark=(benchmark if benchmark is not None else s.get("benchmark", "SPY")) or None,
        regime=rcfg, symbols=[x.strip().upper() for x in (symbols or []) if x.strip()],
    )
    say = progress or (lambda _m: None)
    source = YahooBarSource(yahoo, chart_range=params.chart_range)

    def fit(symbol: str) -> tuple[RegimeAssessment | None, str | None]:
        try:
            bars = source.daily(symbol)
        except (MarketDataUnavailable, YahooError, NoCassette) as exc:
            return None, str(exc)
        return assess(symbol, bars, rcfg), None

    bench, bench_err = (None, None)
    if params.benchmark:
        say(f"benchmark {params.benchmark}")
        bench, bench_err = fit(params.benchmark)

    if params.symbols:
        say(f"watchlist: {', '.join(params.symbols)} (no price filter)")
        quotes, total = [{"symbol": x} for x in params.symbols], len(params.symbols)
    else:
        say(f"screen: {params.min_price} < price < {params.max_price}, top {params.top} by {params.sort_field}")
        quotes, total = yahoo.screen_low_priced_by_volume(params.max_price, params.min_price, params.top,
                                                          params.sort_field)
    rows: list[ScreenRow] = []
    for i, q in enumerate(quotes, start=1):
        sym = str(q.get("symbol", "")).strip()
        say(f"{i}/{len(quotes)} {sym}")
        a, err = fit(sym) if sym else (None, "quote without symbol")
        prof: dict = {}
        if sym:
            try:
                prof = yahoo.profile(sym)
            except (YahooError, NoCassette) as exc:
                prof = {"unavailable_reason": str(exc)}
        if params.symbols:   # watchlist: quote fields come from the profile's price module
            q = {**(prof.get("quote") or {}), "symbol": sym}
        rows.append(ScreenRow(
            rank=i, symbol=sym, name=str(q.get("shortName") or q.get("longName") or ""),
            exchange=str(q.get("exchange") or ""), price=_f(q.get("regularMarketPrice")),
            day_volume=_f(q.get("regularMarketVolume")), avg_volume_3m=_f(q.get("averageDailyVolume3Month")),
            market_cap=_f(q.get("marketCap")), sector=prof.get("sector"), industry=prof.get("industry"),
            country=prof.get("country"), quote_time=_yahoo_time(q), assessment=a, error=err))
    return ScreenRecord(screen_id=f"scr:{uuid.uuid4().hex[:12]}", created_at=utc_now(), params=params,
                        universe_total=total, benchmark=bench, benchmark_error=bench_err, rows=rows)


def _f(v) -> float | None:
    try:
        return None if v is None else float(v)
    except (TypeError, ValueError):
        return None


# ---- rendering (ASCII only: the console is cp1252) ---------------------------------
def _vol(v: float | None) -> str:
    if v is None:
        return "-"
    if v >= 1e9:
        return f"{v / 1e9:.1f}B"
    if v >= 1e6:
        return f"{v / 1e6:.1f}M"
    if v >= 1e3:
        return f"{v / 1e3:.0f}K"
    return f"{v:.0f}"


_SECTOR_SHORT = {"Communication Services": "Comm Services", "Consumer Cyclical": "Cons Cyclical",
                 "Consumer Defensive": "Cons Defensive", "Financial Services": "Financials",
                 "Basic Materials": "Materials", "Real Estate": "Real Estate", "Industrials": "Industrials"}


def _sector(v: str | None) -> str:
    if not v:
        return "-"
    return _SECTOR_SHORT.get(v, v)[:14]


def _pct(v: float | None, signed: bool = False) -> str:
    if v is None:
        return "-"
    return f"{v:+.0%}" if signed else f"{v:.0%}"


def render_assessment_line(a: RegimeAssessment) -> str:
    if a.unavailable_reason:
        return f"UNAVAILABLE ({a.unavailable_reason})"
    return (f"{a.current:<7} {a.days_in_regime:>4}d  P={_pct(a.p_current):>4}  switch1d={_pct(a.p_switch_next_day):>4}  "
            f"drift{a.drift_days}d={_pct(a.drift, signed=True):>5}  lean={a.runner_up}({_pct(a.p_runner_up)})  {a.flag}")


def render_states(a: RegimeAssessment, indent: str = "    ") -> list[str]:
    out = []
    for st in a.states:
        out.append(f"{indent}{st.label:<7} mean ret {st.mean_daily_return_pct:+.2f}%/d, range {st.mean_range_pct:.1f}%, "
                   f"vol x{st.mean_rel_volume:.2f} ({st.descriptor}); stays {st.stay_prob:.0%}, "
                   f"typ {st.expected_duration_days:.0f}d, {st.share_of_history:.0%} of history")
    return out


def render_screen(rec: ScreenRecord, detail: bool = False) -> str:
    p = rec.params
    universe = (f"watchlist {', '.join(p.symbols)} (no price or volume filter)" if p.symbols else
                f"{p.region.upper()} equities on {', '.join(p.exchanges)}, {p.min_price:g} < price < {p.max_price:g}, "
                f"sorted by {p.sort_field} desc, top {p.top} of {rec.universe_total} matching")
    lines = [
        f"REGIME SCREEN  {rec.created_at.strftime('%Y-%m-%d %H:%MZ')}  screen_id={rec.screen_id}",
        f"universe: {universe}; {p.chart_range} daily candles; {p.regime.states}-state Gaussian HMM on "
        f"{p.regime.feature_window}-day trailing features, sticky prior {p.regime.sticky_prior:g} (seed {p.regime.seed})",
        "",
    ]
    if rec.benchmark is not None:
        b = rec.benchmark
        lines.append(f"MARKET ({b.symbol}, last bar {b.as_of.date()}): {render_assessment_line(b)}")
        lines.extend(render_states(b))
        for n in b.notes:
            lines.append(f"    note: {n}")
    elif rec.benchmark_error:
        lines.append(f"MARKET ({p.benchmark}): UNAVAILABLE ({rec.benchmark_error})")
    lines.append("")
    hdr = (f"{'#':>2} {'SYM':<6} {'PRICE':>7} {'VOLUME':>7} {'AVG3M':>7} {'MKTCAP':>7}  {'SECTOR':<14}  {'REGIME':<7} "
           f"{'DAYS':>5}  {'P':>4}  {'SW1D':>4}  {'DRIFT':>5}  {'LEAN':<13} {'FLAG':<9} {'BARS':>4}  NAME / INDUSTRY")
    lines.append(hdr)
    lines.append("-" * len(hdr))
    for r in rec.rows:
        price = "-" if r.price is None else f"{r.price:.2f}"
        base = (f"{r.rank:>2} {r.symbol:<6} {price:>7} {_vol(r.day_volume):>7} {_vol(r.avg_volume_3m):>7} "
                f"{_vol(r.market_cap):>7}  {_sector(r.sector):<14}  ")
        tail = f"{r.name[:26]} / {r.industry or '-'}"
        a = r.assessment
        if a is None:
            lines.append(base + f"{'ERR':<7} {'':>5}  {'':>4}  {'':>4}  {'':>5}  {'':<13} {'':<9} {'':>4}  {tail}  "
                         f"[{(r.error or 'unknown error')[:60]}]")
            continue
        if a.unavailable_reason:
            lines.append(base + f"{'N/A':<7} {'':>5}  {'':>4}  {'':>4}  {'':>5}  {'':<13} {'UNAVAIL':<9} {a.rows_used:>4}  "
                         f"{tail}  [{a.unavailable_reason}]")
            continue
        lean = f"{a.runner_up}({_pct(a.p_runner_up)})"
        lines.append(base + f"{a.current:<7} {a.days_in_regime:>4}d  {_pct(a.p_current):>4}  "
                     f"{_pct(a.p_switch_next_day):>4}  {_pct(a.drift, signed=True):>5}  {lean:<13} {a.flag:<9} "
                     f"{a.rows_used:>4}  {tail}")
        if detail:
            lines.extend(render_states(a, indent="      "))
            for n in a.notes:
                lines.append(f"      note: {n}")
    tp = [r.symbol for r in rec.rows if r.assessment and r.assessment.turning_point]
    lines.append("")
    lines.append(f"turning-point candidates (FRESH or WEAKENING): {', '.join(tp) if tp else 'none'}")
    lines.append("regime names: BULL/BEAR = mean daily return beyond the trend threshold, FLAT = within it; "
                 "-V/-M/-Q = widest / middle / narrowest daily range among the symbol's own states.")
    lines.append("columns: MKTCAP = Yahoo market cap in USD; SECTOR and industry from Yahoo's company profile; "
                 "P = P(current regime) at last bar; SW1D = one-step switch hazard from the fitted "
                 "transition matrix; DRIFT = change in P(current) over the drift window; LEAN = runner-up regime "
                 "and its probability; BARS = usable daily observations.")
    lines.append("")
    for d in rec.disclosures:
        lines.append(f"* {d}")
    return "\n".join(lines)


def to_json(rec: ScreenRecord) -> str:
    return json.dumps(json.loads(rec.model_dump_json()), indent=1)
