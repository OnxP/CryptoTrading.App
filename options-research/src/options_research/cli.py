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


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
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
