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
from options_research.coordinator import Coordinator
from options_research.intake import Analysis, Clarification, IntakeError


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
    return p


def main(argv: list[str] | None = None) -> int:
    args = build_parser().parse_args(argv)
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
        print(render(out.sections))
        print(f"report_id: {out.report_id}  status: {out.status}  as_of: {out.as_of.isoformat()}  "
              f"content_hash: {out.content_hash[:16]}")
    return 0


if __name__ == "__main__":  # pragma: no cover
    sys.exit(main())
