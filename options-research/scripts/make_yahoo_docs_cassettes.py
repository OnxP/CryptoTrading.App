"""Write docs-shaped Yahoo cassettes with FICTIONAL regime-switching bars for tests.

Shapes follow the public chart/screener responses; every value is synthetic
(seeded), symbols are fixture instruments, and `source` is "docs". Re-run to
regenerate; tests replay these and never touch the network.

    options-research\\.venv\\Scripts\\python.exe options-research\\scripts\\make_yahoo_docs_cassettes.py
"""
from __future__ import annotations

import math
import random
import sys
from datetime import date, datetime, timedelta, timezone
from pathlib import Path
from zoneinfo import ZoneInfo

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from options_research.config import PROJECT_ROOT, load_config  # noqa: E402
from options_research.connectors.cassette import Cassette, cassette_key, write_cassette  # noqa: E402
from options_research.connectors.yahoo import YahooClient  # noqa: E402

OUT = PROJECT_ROOT / "fixtures" / "cassettes" / "yahoo"
NY = ZoneInfo("America/New_York")
RECEIVED = datetime(2026, 10, 15, 20, 30, tzinfo=timezone.utc)
LAST_SESSION = date(2026, 10, 15)
NOTE = "FIXTURE: synthetic regime-switching bars, docs-shaped response; no real quotes"

# (name, mean daily log return, daily sigma, volume multiplier, range multiplier)
REGIMES = {
    "bull_quiet": (0.0012, 0.012, 0.85, 0.8),
    "bear_heavy": (-0.0030, 0.035, 1.9, 2.0),
    "flat": (0.0000, 0.018, 1.0, 1.0),
}

# symbol -> (start price, base volume, list of (regime, n_bars)); last segment is the current one
PLANS = {
    "BENCH": (400.0, 60_000_000, [("bull_quiet", 300), ("bear_heavy", 60), ("flat", 150), ("bull_quiet", 240)]),
    "FIXT": (6.0, 40_000_000, [("flat", 250), ("bull_quiet", 300), ("bear_heavy", 198), ("bull_quiet", 2)]),
    "ACME": (3.5, 25_000_000, [("bull_quiet", 350), ("flat", 300), ("bear_heavy", 100)]),
    "NEWB": (2.1, 15_000_000, [("flat", 60)]),   # too short a history -> regime unavailable
}


def sessions_back(n: int) -> list[date]:
    out: list[date] = []
    d = LAST_SESSION
    while len(out) < n:
        if d.weekday() < 5:
            out.append(d)
        d -= timedelta(days=1)
    return list(reversed(out))


def synth(symbol: str, seed: int) -> dict:
    start, base_vol, plan = PLANS[symbol]
    rng = random.Random(seed)
    n = sum(k for _, k in plan)
    days = sessions_back(n)
    ts, o, h, lo, c, v, adj = [], [], [], [], [], [], []
    price = start
    i = 0
    for regime, k in plan:
        mu, sig, vm, rm = REGIMES[regime]
        for _ in range(k):
            r = rng.gauss(mu, sig)
            op = price * math.exp(rng.gauss(0, sig * 0.3))
            cl = price * math.exp(r)
            spread = abs(rng.gauss(0, sig)) * rm + 0.004
            hi = max(op, cl) * (1 + spread / 2)
            low = min(op, cl) * (1 - spread / 2)
            vol = base_vol * vm * math.exp(rng.gauss(0, 0.35))
            t = datetime.combine(days[i], datetime.min.time().replace(hour=9, minute=30), tzinfo=NY)
            ts.append(int(t.timestamp()))
            o.append(round(op, 4)); h.append(round(hi, 4)); lo.append(round(low, 4)); c.append(round(cl, 4))
            v.append(int(vol)); adj.append(round(cl, 4))
            price = cl
            i += 1
    # one null row near the end, as Yahoo sometimes returns for the live session
    ts.insert(-1, ts[-1]); o.insert(-1, None); h.insert(-1, None); lo.insert(-1, None); c.insert(-1, None)
    v.insert(-1, None); adj.insert(-1, None)
    return {"chart": {"result": [{
        "meta": {"currency": "USD", "symbol": symbol, "exchangeName": "NMS", "fullExchangeName": "NasdaqGS",
                 "instrumentType": "EQUITY", "regularMarketPrice": c[-1], "regularMarketVolume": v[-1],
                 "exchangeTimezoneName": "America/New_York", "timezone": "EDT", "gmtoffset": -14400,
                 "dataGranularity": "1d", "range": "3y", "validRanges": ["1d", "5d", "1mo", "3mo", "6mo", "1y", "2y", "5y"]},
        "timestamp": ts,
        "events": {},
        "indicators": {"quote": [{"open": o, "high": h, "low": lo, "close": c, "volume": v}], "adjclose": [{"adjclose": adj}]},
    }], "error": None}}


def quote(symbol: str, price: float, vol: int, avg3m: int, name: str, exch: str) -> dict:
    return {"symbol": symbol, "shortName": name, "longName": name + " (fixture)", "exchange": exch, "quoteType": "EQUITY",
            "region": "US", "currency": "USD", "regularMarketPrice": price, "regularMarketVolume": vol,
            "averageDailyVolume3Month": avg3m, "averageDailyVolume10Day": avg3m, "marketCap": int(price * 4e8),
            "regularMarketTime": int(datetime(2026, 10, 15, 16, 0, tzinfo=NY).timestamp()),
            "regularMarketChangePercent": 1.25, "fiftyTwoWeekRange": f"{price * 0.5:.2f} - {price * 1.8:.2f}",
            "fullExchangeName": "NasdaqGS" if exch == "NMS" else "NYSE", "marketState": "CLOSED"}


def main() -> int:
    cfg = load_config()
    yc = YahooClient(http=None, config=cfg)  # only for URLs/params shapes
    base = yc._base
    out: list[Cassette] = []
    headers = ["Accept", "User-Agent"]

    def add(method: str, url: str, params: dict | None, status: int, body, body_json=None):
        out.append(Cassette(key=cassette_key(method, url, params, body_json), method=method, url=url,
                            params={k: ("(volatile, redacted)" if k == "crumb" else str(v)) for k, v in (params or {}).items()},
                            header_names=headers, status=status, json=body, received_at=RECEIVED, source="docs", note=NOTE))

    for n, sym in enumerate(PLANS):
        add("GET", base + YahooClient.CHART_PATH.format(symbol=sym),
            {"range": "3y", "interval": "1d", "events": "div,splits"}, 200, synth(sym, 100 + n))
    add("GET", base + YahooClient.CHART_PATH.format(symbol="NOPE"), {"range": "3y", "interval": "1d", "events": "div,splits"},
        404, {"chart": {"result": None, "error": {"code": "Not Found", "description": "No data found, symbol may be delisted"}}})
    profiles = {"FIXT": ("Technology", "Semiconductors", "United States"),
                "ACME": ("Industrials", "Specialty Industrial Machinery", "United States"),
                "NEWB": ("Healthcare", "Biotechnology", "United States")}
    quotes_by_sym = {"FIXT": quote("FIXT", 6.42, 48_000_000, 41_000_000, "Fixture Semiconductors Inc", "NMS"),
                     "ACME": quote("ACME", 3.17, 31_000_000, 22_000_000, "Acme Holdings", "NYQ"),
                     "NEWB": quote("NEWB", 2.05, 19_000_000, 17_000_000, "Newborn Listings Corp", "NMS")}
    for sym, (sec, ind, ctry) in profiles.items():
        qq = quotes_by_sym[sym]
        price_mod = {"symbol": sym, "shortName": qq["shortName"], "longName": qq["longName"], "exchange": qq["exchange"],
                     "quoteType": "EQUITY",
                     **{k: {"raw": qq[k], "fmt": str(qq[k])} for k in ("regularMarketPrice", "regularMarketVolume",
                                                                     "averageDailyVolume3Month", "marketCap",
                                                                     "regularMarketTime")}}
        add("GET", base + YahooClient.PROFILE_PATH.format(symbol=sym), {"modules": "assetProfile,price", "crumb": "FIXTURECRUMB"}, 200,
            {"quoteSummary": {"result": [{"assetProfile": {"sector": sec, "industry": ind, "country": ctry,
                                                            "longBusinessSummary": "FIXTURE company."},
                                           "price": price_mod}], "error": None}})
    add("GET", base + YahooClient.PROFILE_PATH.format(symbol="NOPE"), {"modules": "assetProfile,price", "crumb": "FIXTURECRUMB"}, 404,
        {"quoteSummary": {"result": None, "error": {"code": "Not Found", "description": "Quote not found for symbol: NOPE"}}})
    add("GET", yc._cookie_url, None, 404, {"_non_json_body": "<html>fixture cookie page</html>"})
    add("GET", base + YahooClient.CRUMB_PATH, None, 200, {"_non_json_body": "FIXTURECRUMB"})

    quotes = [quote("FIXT", 6.42, 48_000_000, 41_000_000, "Fixture Semiconductors Inc", "NMS"),
              quote("ACME", 3.17, 31_000_000, 22_000_000, "Acme Holdings", "NYQ"),
              quote("NEWB", 2.05, 19_000_000, 17_000_000, "Newborn Listings Corp", "NMS"),
              quote("NOPE", 1.40, 12_000_000, 9_000_000, "Delisted Example", "ASE")]
    for size in (3, 4, 5):
        query = {"operator": "AND", "operands": [
            {"operator": "LT", "operands": ["intradayprice", 10.0]},
            {"operator": "GT", "operands": ["intradayprice", 1.0]},
            {"operator": "EQ", "operands": ["region", yc.region]},
            {"operator": "OR", "operands": [{"operator": "EQ", "operands": ["exchange", e]} for e in yc.exchanges]},
        ]}
        body = {"size": size, "offset": 0, "sortField": "dayvolume", "sortType": "DESC", "quoteType": "EQUITY",
                "query": query, "userId": "", "userIdType": "guid"}
        add("POST", base + YahooClient.SCREENER_PATH, {"crumb": "FIXTURECRUMB", "formatted": "false", "lang": "en-US", "region": "US"},
            200, {"finance": {"result": [{"id": "fixture", "title": "fixture screen", "start": 0, "count": min(size, len(quotes)),
                                           "total": 937, "quotes": quotes[:size]}], "error": None}}, body_json=body)

    OUT.mkdir(parents=True, exist_ok=True)
    for old in OUT.glob("*.json"):
        old.unlink()
    for c in out:
        write_cassette(OUT, c)
    print(f"wrote {len(out)} docs cassettes to {OUT}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
