"""Record live cassettes. Refuses unless --record is passed AND the provider's
token env var (named in config) is set. Never run from tests.

    options-research\\.venv\\Scripts\\python.exe options-research\\scripts\\record_cassettes.py --provider tradier --record

Secrets are read from the environment at call time and are never written to disk.
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))

from options_research.config import PROJECT_ROOT, load_config  # noqa: E402
from options_research.connectors.cassette import RecordingHttpClient  # noqa: E402
from options_research.connectors.http import HttpxClient  # noqa: E402


def main(argv=None) -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--provider", choices=["tradier", "edgar", "news", "yahoo"], required=True)
    p.add_argument("--record", action="store_true", help="actually perform live calls")
    p.add_argument("--symbol", default=None)
    args = p.parse_args(argv)
    if not args.record:
        print("Dry run: pass --record to perform live calls.")
        return 0
    cfg = load_config()
    out = PROJECT_ROOT / "fixtures" / "cassettes" / args.provider
    client = RecordingHttpClient(HttpxClient(), out, source="live")

    if args.provider == "tradier":
        token_env = cfg.raw.get("tradier", {}).get("token_env", "TRADIER_SANDBOX_TOKEN")
        if not os.environ.get(token_env):
            print(f"{token_env} is not set; refusing to record.")
            return 2
        from options_research.connectors.tradier import TradierClient
        tc = TradierClient(client, cfg)
        sym = args.symbol or "SPY"
        tc.calendar()
        tc.quotes([sym])
        tc.history(sym, start="2026-09-01", end="2026-10-15")
        exps = tc.expirations(sym)
        if exps:
            tc.chain(sym, exps[0])
    elif args.provider == "yahoo":  # no token: public endpoints; the crumb/cookie are never stored
        from options_research.connectors.yahoo import YahooClient
        yc = YahooClient(client, cfg)
        yc.chart(args.symbol or "SPY")
        yc.screen_low_priced_by_volume(10.0, 1.0, 5)
        yc.profile(args.symbol or "SPY")
    elif args.provider == "edgar":
        from options_research.connectors.edgar import EdgarClient
        ec = EdgarClient(client, cfg)
        ec.company_tickers()
        if args.symbol:
            ins = ec.lookup().by_symbol(args.symbol)
            if ins:
                ec.submissions(ins[0].symbol)
    else:
        from datetime import date, timedelta

        from options_research.connectors.news_api import NewsApiClient
        nc = NewsApiClient(client, cfg)
        key_env = cfg.raw.get("news", {}).get("key_env", "FINNHUB_API_KEY")
        if not os.environ.get(key_env):
            print(f"{key_env} is not set; refusing to record.")
            return 2
        sym = args.symbol or "SPY"
        nc.company_news(sym, date.today() - timedelta(days=7), date.today())
    print(f"recorded into {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
