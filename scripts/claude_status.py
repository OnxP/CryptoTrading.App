import json
import os
import sys
import time
from pathlib import Path

data = json.load(sys.stdin)

workspace = data.get("workspace", {})
project_dir = Path(
    workspace.get("project_dir")
    or workspace.get("current_dir")
    or os.getcwd()
)

runtime_dir = project_dir / ".workflow" / "runtime"
runtime_dir.mkdir(parents=True, exist_ok=True)

rate_limits = data.get("rate_limits") or {}
five = rate_limits.get("five_hour") or {}
seven = rate_limits.get("seven_day") or {}

context = data.get("context_window") or {}
model = data.get("model") or {}

snapshot = {
    "captured_at_unix": int(time.time()),
    "session_id": data.get("session_id"),
    "model_id": model.get("id"),
    "model_name": model.get("display_name"),
    "effort": (data.get("effort") or {}).get("level"),
    "context_used_percent": context.get("used_percentage"),
    "five_hour_used_percent": five.get("used_percentage"),
    "five_hour_resets_at": five.get("resets_at"),
    "seven_day_used_percent": seven.get("used_percentage"),
    "seven_day_resets_at": seven.get("resets_at"),
}

target = runtime_dir / "usage.json"
temp = runtime_dir / "usage.json.tmp"

temp.write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
temp.replace(target)

five_pct = snapshot["five_hour_used_percent"]
seven_pct = snapshot["seven_day_used_percent"]
ctx_pct = snapshot["context_used_percent"]

def fmt(value):
    return "?" if value is None else f"{float(value):.0f}%"

print(
    f"[{snapshot['model_name'] or 'Claude'}] "
    f"5h:{fmt(five_pct)} "
    f"7d:{fmt(seven_pct)} "
    f"ctx:{fmt(ctx_pct)}"
)
