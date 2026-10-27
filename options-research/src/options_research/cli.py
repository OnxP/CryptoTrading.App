"""CLI usable from Claude or a shell.

    options-research research "Analyse FIXT news from the last seven days" --as-of 2026-10-10T20:00Z

Prints the briefing and the report id. Returns findings only; no delivery option exists.
"""
from __future__ import annotations

import argparse
import sys
from datetime import datetime, timezone
from pathlib import Path

from options_research.briefing import render
from options_research.config import load_config
from options_research.coordinator import Coordinator
from options_research.intake import Analysis, Clarification, IntakeError
from options_research.ledger import ReportLedger
from options_research.timeutil import utc_now

STALE_BANNER = "STALE: this report's advisory content passed its valid_until ({valid_until}); it is not actionable."


def _parse_dt(s: str) -> datetime:
    dt = datetime.fromisoformat(s.replace("Z", "+00:00"))
    if dt.tzinfo is None:
        raise argparse.ArgumentTypeError("datetime must carry a timezone, e.g. 2026-10-10T20:00Z")
    return dt.astimezone(timezone.utc)


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(prog="options-research", description="Advisory research. Returns briefings, never orders.")
    sub = p.add_subparsers(dest="cmd", required=True)
    r = sub.add_parser("research", help="run one ad hoc research request against fixtures")
    r.add_argument("question")
    r.add_argument("--as-of", type=_parse_dt, default=None, help="decision cutoff (UTC ISO 8601)")
    r.add_argument("--window-days", type=int, default=None)
    r.add_argument("--parent", default=None, help="parent report id for a follow-up")
    r.add_argument("--requester", default="local")
    r.add_argument("--analyses", nargs="*", choices=[a.value for a in Analysis], default=None)
    r.add_argument("--horizon-days", type=int, default=None)
    r.add_argument("--data-dir", type=Path, default=None, help="ledger directory (default from config)")
    r.add_argument("--fixtures-dir", type=Path, default=None)
    r.add_argument("--json", action="store_true", help="print the full report record as JSON")
    s = sub.add_parser("show", help="reprint a stored report; prints STALE when its validity has passed")
    s.add_argument("report_id")
    s.add_argument("--data-dir", type=Path, default=None)
    s.add_argument("--now", type=_parse_dt, default=None, help="evaluation time for staleness (default: now, UTC)")
    s.add_argument("--json", action="store_true")
    sc = sub.add_parser("screen", help="low-priced, high-volume US equities with a per-symbol HMM regime (Yahoo, live)")
    sc.add_argument("--max-price", type=float, default=None)
    sc.add_argument("--min-price", type=float, default=None)
    sc.add_argument("--top", type=int, default=None, help="number of symbols (1..250)")
    sc.add_argument("--symbols", default=None, help="comma-separated watchlist instead of the screener (no price filter)")
    sc.add_argument("--benchmark", default=None, help="market symbol shown first (default SPY; '' to skip)")
    sc.add_argument("--range", dest="chart_range", default=None, help="Yahoo chart range, e.g. 3y, 5y")
    sc.add_argument("--replay-dir", type=Path, default=None, help="serve Yahoo responses from cassettes; no network")
    sc.add_argument("--record-dir", type=Path, default=None, help="record live Yahoo responses as cassettes")
    sc.add_argument("--data-dir", type=Path, default=None)
    sc.add_argument("--detail", action="store_true", help="print each symbol's regime descriptions")
    sc.add_argument("--json", action="store_true")
    sc.add_argument("--quiet", action="store_true", help="no progress on stderr")
    return p


def print_report(out, now: datetime) -> None:
    stale = out.is_stale(now)
    if stale:
        print(STALE_BANNER.format(valid_until=out.valid_until.isoformat()))
    print(render(out.sections))
    tail = (f"report_id: {out.report_id}  status: {out.status}  as_of: {out.as_of.isoformat()}  "
            f"content_hash: {out.content_hash[:16]}")
    if out.valid_until is not None:
        tail += f"  valid_until: {out.valid_until.isoformat()}" + ("  [STALE, not actionable]" if stale else "")
    print(tail)


def run_screen_cmd(args) -> int:
    from options_research.connectors import NoCassette, RecordingHttpClient, ReplayHttpClient
    from options_research.connectors.yahoo import YahooClient, YahooError
    from options_research.regime import ScreenLedger, render_screen, run_screen
    from options_research.regime.screen import to_json

    cfg = load_config()
    if args.replay_dir is not None:
        http = ReplayHttpClient(args.replay_dir)
    else:
        from options_research.connectors.http import HttpxClient

        http = HttpxClient()
        if args.record_dir is not None:
            http = RecordingHttpClient(http, args.record_dir, source="live")
    progress = (lambda m: None) if args.quiet else (lambda m: print(m, file=sys.stderr, flush=True))
    try:
        rec = run_screen(YahooClient(http, cfg), cfg, max_price=args.max_price, min_price=args.min_price, top=args.top,
                         benchmark=args.benchmark, chart_range=args.chart_range, progress=progress,
                         symbols=args.symbols.split(",") if args.symbols else None)
    except (YahooError, NoCassette, ValueError) as exc:
        print(f"UNAVAILABLE: {exc}", file=sys.stderr)
        return 5
    ScreenLedger((args.data_dir or cfg.ledger_dir) / "screens.jsonl").append(rec)
    print(to_json(rec) if args.json else render_screen(rec, detail=args.detail))
    return 0


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
    if args.cmd == "screen":
        return run_screen_cmd(args)
    if args.cmd == "show":
        data_dir = args.data_dir or load_config().ledger_dir
        try:
            rec = ReportLedger(data_dir / "reports.jsonl").get(args.report_id)
        except KeyError:
            print(f"NOT FOUND: {args.report_id}", file=sys.stderr)
            return 4
        if args.json:
            print(rec.model_dump_json(indent=1))
        else:
            print_report(rec, args.now or utc_now())
        return 0
    coord = Coordinator.offline(fixtures_dir=args.fixtures_dir, data_dir=args.data_dir)
    try:
        out = coord.handle(
            args.question, requester=args.requester, as_of=args.as_of, window_days=args.window_days,
            parent_report_id=args.parent,
            analyses=[Analysis(a) for a in args.analyses] if args.analyses else None,
            horizon_days=args.horizon_days,
        )
    except IntakeError as exc:
        print(f"REJECTED: {exc}", file=sys.stderr)
        return 2
    if isinstance(out, Clarification):
        print(f"CLARIFICATION NEEDED ({out.reason}): {out.question}")
        for c in out.candidates:
            print(f"  - {c.name} ({c.canonical})")
        return 3
    if args.json:
        print(out.model_dump_json(indent=1))
    else:
        print_report(out, utc_now())
    return 0


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())
