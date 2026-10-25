"""Tradier market-data connector (sandbox by default).

Only /v1/markets/* endpoints exist here. No /accounts, no orders, ever.

Documented shapes (verified against one live recording once a token exists):
  GET markets/quotes?symbols=A,B            {"quotes": {"quote": {...} | [{...}]}}
  GET markets/history?symbol&interval=daily&start&end
                                            {"history": {"day": {...} | [{"date","open","high","low","close","volume"}]}}
  GET markets/timesales?symbol&interval=1min|5min|15min&start&end&session_filter=open
                                            {"series": {"data": [{"time","timestamp","price","open","high","low","close","volume","vwap"}]}}
  GET markets/calendar?month&year           {"calendar": {"days": {"day": [{"date","status","open":{"start","end"}}]}}}
  GET markets/options/expirations?symbol&includeAllRoots=true&strikes=false
                                            {"expirations": {"date": [..]}}
  GET markets/options/chains?symbol&expiration&greeks=true
                                            {"options": {"option": [{...quote fields..., "greeks": {...}}]}}
Tradier returns a bare object instead of a one-element list; `_as_list` normalises.
Epoch fields (bid_date, ask_date, trade_date) are milliseconds UTC.
"""
from __future__ import annotations

import os
from datetime import date, datetime, timezone
from typing import Any

from options_research.config import Config
from options_research.connectors.http import HttpClient

MARKET_TZ = "America/New_York"


class TradierAuthUnavailable(RuntimeError):
    pass


class TradierError(RuntimeError):
    pass


def _as_list(x: Any) -> list:
    if x is None or x == "null":
        return []
    return x if isinstance(x, list) else [x]


def epoch_ms_to_utc(ms: int | float | None) -> datetime | None:
    if ms in (None, 0, "0"):
        return None
    return datetime.fromtimestamp(float(ms) / 1000.0, tz=timezone.utc)


class TradierClient:
    INTERVALS = {"1m": "1min", "5m": "5min", "15m": "15min"}

    def __init__(self, http: HttpClient, config: Config):
        t = config.raw.get("tradier", {})
        self._http = http
        self._base = t.get("base_url", "https://sandbox.tradier.com/v1").rstrip("/")
        self._token_env = t.get("token_env", "TRADIER_SANDBOX_TOKEN")
        self.is_sandbox = "sandbox" in self._base

    # ---- plumbing ----------------------------------------------------------
    def _headers(self) -> dict[str, str]:
        token = os.environ.get(self._token_env)
        if not token:
            raise TradierAuthUnavailable(f"token env var {self._token_env} not set")
        return {"Authorization": f"Bearer {token}", "Accept": "application/json"}

    def _get(self, path: str, params: dict[str, str]) -> Any:
        resp = self._http.get(f"{self._base}/{path}", params=params, headers=self._headers())
        if resp.status != 200:
            raise TradierError(f"{path} returned {resp.status}: {str(resp.json)[:200]}")
        return resp.json, resp.received_at

    # ---- market data -------------------------------------------------------
    def quotes(self, symbols: list[str]) -> list[dict]:
        body, _ = self._get("markets/quotes", {"symbols": ",".join(symbols), "greeks": "false"})
        return _as_list((body.get("quotes") or {}).get("quote"))

    def history(self, symbol: str, start: str, end: str, interval: str = "daily") -> list[dict]:
        body, _ = self._get("markets/history", {"symbol": symbol, "interval": interval, "start": start, "end": end,
                                                "session_filter": "open"})
        return _as_list((body.get("history") or {}).get("day"))

    def timesales(self, symbol: str, interval: str, start: str, end: str) -> list[dict]:
        body, _ = self._get("markets/timesales", {"symbol": symbol, "interval": interval, "start": start, "end": end,
                                                  "session_filter": "open"})
        return _as_list((body.get("series") or {}).get("data"))

    def calendar(self, month: int | None = None, year: int | None = None) -> list[dict]:
        params: dict[str, str] = {}
        if month:
            params["month"] = f"{month:02d}"
        if year:
            params["year"] = str(year)
        body, _ = self._get("markets/calendar", params)
        return _as_list(((body.get("calendar") or {}).get("days") or {}).get("day"))

    def sessions(self, start: date, end: date) -> list[date] | None:
        """Open session dates in [start, end] from the calendar, or None if unavailable."""
        out: list[date] = []
        y, m = start.year, start.month
        while (y, m) <= (end.year, end.month):
            try:
                days = self.calendar(m, y)
            except Exception:  # noqa: BLE001 - calendar is advisory for gap detection
                return None
            for d in days:
                dd = date.fromisoformat(d["date"])
                if start <= dd <= end and d.get("status") == "open":
                    out.append(dd)
            m += 1
            if m == 13:
                m, y = 1, y + 1
        return out

    # ---- options -----------------------------------------------------------
    def expirations(self, symbol: str) -> list[str]:
        body, _ = self._get("markets/options/expirations",
                            {"symbol": symbol, "includeAllRoots": "true", "strikes": "false"})
        return [str(d) for d in _as_list((body.get("expirations") or {}).get("date"))]

    def chain(self, symbol: str, expiration: str) -> tuple[list[dict], datetime]:
        body, received_at = self._get("markets/options/chains",
                                      {"symbol": symbol, "expiration": expiration, "greeks": "true"})
        return _as_list((body.get("options") or {}).get("option")), received_at
