"""
Generate a TradingView Pine v5 indicator that visualises a backtest trade list.

TradingView has no native "import CSV" for trades, but Pine v5 can place labels
and lines at absolute timestamps (xloc.bar_time). So we just bake the CSV rows
into arrays inside a generated .pine file, and the indicator paints every
historical trade on the chart when you add it.

Usage:
    python Tools/generate_tv_trades.py trades_arm_F_bbguide.csv \\
        TradingView/trades_arm_F_bbguide.pine

The CSV must contain at least these columns (the backtest writes them):
    Direction, SignalTime, SignalPrice, EntryTime, EntryPrice,
    ExitTime,  ExitPrice,  ExitReason, PnlUsdt

Times are treated as UTC (the backtest emits UTC throughout).

Pine limit: max_labels_count / max_lines_count cap at 500 per indicator.
With one entry label + one exit label + one entry→exit line per trade, the
script supports ~250 trades before overflowing. If the CSV has more, set
--limit or split across multiple files.
"""

from __future__ import annotations

import argparse
import csv
import os
import sys
from datetime import datetime


def parse_utc(ts: str) -> datetime:
    """Backtest emits 'yyyy-MM-dd HH:mm' in UTC."""
    return datetime.strptime(ts.strip(), "%Y-%m-%d %H:%M")


def pine_timestamp_call(dt: datetime) -> str:
    """Pine v5: timestamp('UTC', year, month, day, hour, minute, second)"""
    return (
        f'timestamp("UTC", {dt.year}, {dt.month}, {dt.day}, '
        f"{dt.hour}, {dt.minute}, 0)"
    )


def generate(csv_path: str, out_path: str, limit: int | None, title: str | None) -> None:
    rows: list[dict[str, str]] = []
    with open(csv_path, newline="", encoding="utf-8") as f:
        for row in csv.DictReader(f):
            # Skip cancelled entries — nothing to plot: no fill happened.
            if row["ExitReason"].startswith("EntryCancelled"):
                continue
            rows.append(row)

    if limit is not None:
        rows = rows[:limit]

    n = len(rows)
    if n == 0:
        print(f"No filled trades in {csv_path}", file=sys.stderr)
        sys.exit(1)

    # Pine imposes 500 per draw type. 2 labels + 1 line per trade = cap 166.
    if n > 250:
        print(
            f"WARN: {n} trades exceeds Pine's 500-label limit when plotting "
            f"entry + exit markers. Use --limit 250, or split the CSV.",
            file=sys.stderr,
        )

    indicator_title = title or f"Backtest trades — {os.path.basename(csv_path)}"

    lines: list[str] = []
    add = lines.append

    add(
        "// "
        + "=" * 76
        + "\n"
        + "// Auto-generated from "
        + os.path.basename(csv_path)
        + f" ({n} filled trades)\n"
        + "// "
        + "=" * 76
        + "\n"
        + "// Paste into TradingView → Pine Editor → Save → Add to chart.\n"
        + "// Each trade renders as an entry arrow, an exit X, and a line joining\n"
        + "// them. Line colour = green (win) / red (loss). Exit symbol text =\n"
        + "// exit reason. Hover a label for full details.\n"
        + "// "
        + "=" * 76
    )
    add("")
    add("//@version=5")
    add(
        f'indicator("{indicator_title}", overlay=true, '
        "max_labels_count=500, max_lines_count=500)"
    )
    add("")
    add(
        "// Display controls\n"
        'showLongs      = input.bool(true,  "Show longs",            group="Display")\n'
        'showShorts     = input.bool(true,  "Show shorts",           group="Display")\n'
        'showWins       = input.bool(true,  "Show winners",          group="Display")\n'
        'showLosers     = input.bool(true,  "Show losers",           group="Display")\n'
        'showLines      = input.bool(true,  "Draw entry→exit lines", group="Display")\n'
        'showExitReason = input.bool(true,  "Label exit reasons",    group="Display")\n'
        'showPnl        = input.bool(false, "Label entry P&L",       group="Display")'
    )
    add("")
    add(
        "// Trade arrays — index-aligned. Built once on the very first bar the\n"
        "// indicator runs on; then painted via time-anchored labels/lines so\n"
        "// they sit at the correct historical timestamps on any timeframe."
    )
    add("var entryTimes  = array.new_int(0)")
    add("var exitTimes   = array.new_int(0)")
    add("var entryPrices = array.new_float(0)")
    add("var exitPrices  = array.new_float(0)")
    add("var dirs        = array.new_string(0)")
    add("var reasons     = array.new_string(0)")
    add("var pnls        = array.new_float(0)")
    add("")
    add("if barstate.isfirst")

    # Emit all pushes in one barstate.isfirst block.
    for r in rows:
        et = parse_utc(r["EntryTime"])
        xt = parse_utc(r["ExitTime"])
        ep = float(r["EntryPrice"])
        xp = float(r["ExitPrice"])
        direction = r["Direction"]
        reason = r["ExitReason"]
        pnl = float(r["PnlUsdt"])
        add(f"    array.push(entryTimes,  {pine_timestamp_call(et)})")
        add(f"    array.push(exitTimes,   {pine_timestamp_call(xt)})")
        add(f"    array.push(entryPrices, {ep:.2f})")
        add(f"    array.push(exitPrices,  {xp:.2f})")
        add(f'    array.push(dirs,        "{direction}")')
        add(f'    array.push(reasons,     "{reason}")')
        add(f"    array.push(pnls,        {pnl:.2f})")

    add("")
    add("// Render once, on the first bar. Labels/lines are time-anchored\n"
        "// (xloc.bar_time) so they land at absolute UTC times regardless of\n"
        "// the current chart's timeframe or scroll position.")
    add("if barstate.isfirst")
    add("    for i = 0 to array.size(entryTimes) - 1")
    add("        et     = array.get(entryTimes,  i)")
    add("        xt     = array.get(exitTimes,   i)")
    add("        ep     = array.get(entryPrices, i)")
    add("        xp     = array.get(exitPrices,  i)")
    add('        d      = array.get(dirs,        i)')
    add('        r      = array.get(reasons,     i)')
    add("        p      = array.get(pnls,        i)")
    add('        isLong = d == "Long"')
    add("        isWin  = p > 0")
    add("")
    add("        // Respect display filters")
    add("        showIt = (isLong ? showLongs : showShorts) and")
    add("                 (isWin  ? showWins  : showLosers)")
    add("")
    add("        if showIt")
    add("            entryColor = isLong ? color.new(color.lime, 0) : color.new(color.orange, 0)")
    add("            lineColor  = isWin  ? color.new(color.green, 30) : color.new(color.red, 30)")
    add("")
    add("            // Entry arrow")
    add("            entryText = showPnl ? d + \" \" + str.tostring(p, \"#\") : d")
    add("            label.new(x=et, y=ep, xloc=xloc.bar_time,")
    add("                      text=entryText,")
    add("                      style=isLong ? label.style_label_up : label.style_label_down,")
    add("                      color=entryColor, textcolor=color.black,")
    add("                      size=size.tiny,")
    add("                      tooltip=d + \" entry @ \" + str.tostring(ep, \"#.##\") +")
    add("                              \"\\nExit: \" + r + \" @ \" + str.tostring(xp, \"#.##\") +")
    add("                              \"\\nP&L: \" + str.tostring(p, \"#.##\"))")
    add("")
    add("            // Exit marker")
    add("            exitColor = isWin ? color.new(color.green, 0) : color.new(color.red, 0)")
    add("            exitText = showExitReason ? r : \"\"")
    add("            label.new(x=xt, y=xp, xloc=xloc.bar_time,")
    add("                      text=exitText,")
    add("                      style=label.style_xcross,")
    add("                      color=exitColor, textcolor=color.white,")
    add("                      size=size.tiny)")
    add("")
    add("            // Entry → exit line")
    add("            if showLines")
    add("                line.new(x1=et, y1=ep, x2=xt, y2=xp, xloc=xloc.bar_time,")
    add("                         color=lineColor, width=1,")
    add("                         style=line.style_solid)")
    add("")
    add("// Summary in top-right — reads from the arrays so it reflects the")
    add("// current display filters implicitly via the label/line plotting.")
    add("var table summary = table.new(position.top_right, 2, 4,")
    add("                              bgcolor=color.new(color.black, 75),")
    add("                              border_width=1)")
    add("if barstate.islast")

    n_long = sum(1 for r in rows if r["Direction"] == "Long")
    n_short = n - n_long
    wins = sum(1 for r in rows if float(r["PnlUsdt"]) > 0)
    total_pnl = sum(float(r["PnlUsdt"]) for r in rows)
    add(f'    table.cell(summary, 0, 0, "Source",   text_color=color.white, bgcolor=color.new(color.gray, 60))')
    add(f'    table.cell(summary, 1, 0, "{os.path.basename(csv_path)}", text_color=color.white)')
    add(f'    table.cell(summary, 0, 1, "Trades",   text_color=color.white, bgcolor=color.new(color.gray, 60))')
    add(f'    table.cell(summary, 1, 1, "{n} ({n_long}L/{n_short}S)", text_color=color.white)')
    wr_pct = 100.0 * wins / n if n else 0.0
    add(f'    table.cell(summary, 0, 2, "Win rate", text_color=color.white, bgcolor=color.new(color.gray, 60))')
    add(f'    table.cell(summary, 1, 2, "{wr_pct:.1f}% ({wins}/{n})",     text_color=color.white)')
    add(f'    table.cell(summary, 0, 3, "Net P&L",  text_color=color.white, bgcolor=color.new(color.gray, 60))')
    pnl_color = "color.lime" if total_pnl >= 0 else "color.red"
    add(f'    table.cell(summary, 1, 3, "{total_pnl:,.0f}", text_color={pnl_color})')

    output = "\n".join(lines) + "\n"
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        f.write(output)
    print(f"Wrote {out_path} ({n} trades, {len(lines)} lines)")


def main() -> None:
    ap = argparse.ArgumentParser(
        description="Convert a backtest trade CSV into a TradingView Pine v5 indicator."
    )
    ap.add_argument("csv_path", help="Input trade CSV from the backtester")
    ap.add_argument("out_path", help="Output .pine file path")
    ap.add_argument("--limit", type=int, default=None,
                    help="Cap at N trades (Pine labels max 500; default: no cap)")
    ap.add_argument("--title", default=None,
                    help="Indicator title (default: derived from filename)")
    args = ap.parse_args()
    generate(args.csv_path, args.out_path, args.limit, args.title)


if __name__ == "__main__":
    main()
