"""UTC-only datetime helpers. Naive datetimes are rejected everywhere."""
from __future__ import annotations

from datetime import datetime, timezone


def require_utc(value: datetime) -> datetime:
    if value.tzinfo is None or value.utcoffset() is None:
        raise ValueError("datetime must be timezone-aware (UTC)")
    if value.utcoffset().total_seconds() != 0:
        raise ValueError("datetime must be in UTC (offset 0)")
    return value.astimezone(timezone.utc)


def utc_now() -> datetime:
    return datetime.now(timezone.utc)
