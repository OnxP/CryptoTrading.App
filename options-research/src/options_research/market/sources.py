"""Bar sources behind one seam. MarketDataSpecialist does not know where bars come from."""
from __future__ import annotations

import csv
from dataclasses import dataclass
from datetime import date, datetime, time, timedelta, timezone
from pathlib import Path
from typing import Protocol
from zoneinfo import ZoneInfo

from options_research.intake.models import Instrument
from options_research.market.bars import INTERVAL_SECONDS, Bar

NY = ZoneInfo("America/New_York")


class MarketDataUnavailable(RuntimeError):
    pass


@dataclass(frozen=True)
class BarBatch:
    bars: list[Bar]
    source_label: str
    sessions: list[date] | None = None       # open sessions covering the range (for daily gap checks)
    delay_disclosure: str | None = None       # e.g. "sandbox data is delayed"


class BarSource(Protocol):
    def load(self, instrument: Instrument, interval: str, start: datetime, end: datetime) -> BarBatch: ...


class FixtureBarSource:
    """CSV fixtures: <VENUE>_<SYMBOL>_<interval>.csv with open_time (UTC), interval, o/h/l/c/v."""

    def __init__(self, directory: Path):
        self._dir = Path(directory)

    def load(self, instrument: Instrument, interval: str, start: datetime, end: datetime) -> BarBatch:
        path = self._dir / f"{instrument.venue}_{instrument.symbol}_{interval}.csv"
        if not path.exists():
            raise MarketDataUnavailable(f"no market data for {instrument.canonical} {interval} at {path.name}")
        secs = INTERVAL_SECONDS[interval]
        bars: list[Bar] = []
        with open(path, newline="", encoding="utf-8") as fh:
            for row in csv.DictReader(fh):
                ot = datetime.fromisoformat(row["open_time"].replace("Z", "+00:00"))
                bars.append(Bar(open_time=ot, close_time=ot + timedelta(seconds=secs),
                                open=float(row["open"]), high=float(row["high"]), low=float(row["low"]),
                                close=float(row["close"]), volume=float(row["volume"])))
        return BarBatch(bars=bars, source_label=f"FIXTURE:{path.name}")


def ny_session_bounds(d: date) -> tuple[datetime, datetime]:
    """Regular session 09:30-16:00 America/New_York as UTC instants (half days ignored in M2)."""
    o = datetime.combine(d, time(9, 30), tzinfo=NY).astimezone(timezone.utc)
    c = datetime.combine(d, time(16, 0), tzinfo=NY).astimezone(timezone.utc)
    return o, c


class TradierBarSource:
    def __init__(self, client):
        from options_research.connectors.tradier import TradierAuthUnavailable, TradierClient, TradierError  # local to keep import light

        self._c: TradierClient = client
        self._auth_err = TradierAuthUnavailable
        self._err = TradierError

    def load(self, instrument: Instrument, interval: str, start: datetime, end: datetime) -> BarBatch:
        sym = instrument.symbol
        try:
            if interval == "1d":
                rows = self._c.history(sym, start=start.date().isoformat(), end=end.date().isoformat())
                bars = []
                for r in rows:
                    d = date.fromisoformat(str(r["date"]))
                    o, c = ny_session_bounds(d)
                    bars.append(Bar(open_time=o, close_time=c, open=float(r["open"]), high=float(r["high"]),
                                    low=float(r["low"]), close=float(r["close"]), volume=float(r["volume"])))
                sessions = self._c.sessions(start.date(), end.date())
            elif interval in self._c.INTERVALS:
                rows = self._c.timesales(sym, self._c.INTERVALS[interval],
                                         start.astimezone(NY).strftime("%Y-%m-%d %H:%M"),
                                         end.astimezone(NY).strftime("%Y-%m-%d %H:%M"))
                secs = INTERVAL_SECONDS[interval]
                bars = []
                for r in rows:
                    ot = datetime.fromtimestamp(int(r["timestamp"]), tz=timezone.utc)
                    bars.append(Bar(open_time=ot, close_time=ot + timedelta(seconds=secs), open=float(r["open"]),
                                    high=float(r["high"]), low=float(r["low"]), close=float(r["close"]),
                                    volume=float(r["volume"])))
                sessions = None
            else:
                raise MarketDataUnavailable(f"interval {interval} not supported by Tradier source")
        except self._auth_err as exc:
            raise MarketDataUnavailable(str(exc)) from exc
        except self._err as exc:
            raise MarketDataUnavailable(f"Tradier error: {exc}") from exc
        if not bars:
            raise MarketDataUnavailable(f"Tradier returned no bars for {sym} {interval} {start.date()}..{end.date()}")
        label = f"TRADIER{'-SANDBOX' if self._c.is_sandbox else ''}:{sym}:{interval}"
        disc = "Tradier sandbox data is delayed; freshness reflects the vendor's last update, not the market" \
            if self._c.is_sandbox else None
        return BarBatch(bars=bars, source_label=label, sessions=sessions, delay_disclosure=disc)


class YahooBarSource:
    """Daily bars from the Yahoo chart endpoint (split-adjusted OHLC, exchange-local sessions)."""

    def __init__(self, client, chart_range: str | None = None, now=None):
        from options_research.connectors.yahoo import YahooClient, YahooError  # local to keep import light
        from options_research.timeutil import utc_now

        self._c: YahooClient = client
        self._err = YahooError
        self._range = chart_range or client.chart_range
        self._now = now or utc_now

    def daily(self, symbol: str) -> list[Bar]:
        """Complete daily bars only: a session still open at call time is dropped (Yahoo includes it)."""
        try:
            rows, meta = self._c.chart(symbol, range_=self._range, interval="1d")
        except self._err as exc:
            raise MarketDataUnavailable(f"Yahoo error: {exc}") from exc
        if not rows:
            raise MarketDataUnavailable(f"Yahoo returned no daily bars for {symbol} ({self._range})")
        tz = ZoneInfo(str(meta.get("exchangeTimezoneName") or "America/New_York"))
        bars: list[Bar] = []
        for r in rows:
            d = r["time"].astimezone(tz).date()
            if tz.key == "America/New_York":
                o, c = ny_session_bounds(d)
            else:
                o = datetime.combine(d, time(0, 0), tzinfo=tz).astimezone(timezone.utc)
                c = o + timedelta(hours=24)
            bars.append(Bar(open_time=o, close_time=c, open=r["open"], high=r["high"], low=r["low"], close=r["close"],
                            volume=r["volume"]))
        now = self._now()
        return [b for b in bars if b.close_time <= now]

    def load(self, instrument: Instrument, interval: str, start: datetime, end: datetime) -> BarBatch:
        if interval != "1d":
            raise MarketDataUnavailable(f"interval {interval} not supported by Yahoo source")
        bars = [b for b in self.daily(instrument.symbol) if start <= b.open_time and b.close_time <= end]
        if not bars:
            raise MarketDataUnavailable(f"Yahoo returned no bars for {instrument.symbol} in {start.date()}..{end.date()}")
        return BarBatch(bars=bars, source_label=f"YAHOO:{instrument.symbol}:1d",
                        delay_disclosure="Yahoo Finance public data is unofficial and may be delayed")
