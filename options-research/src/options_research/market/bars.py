"""Bar model shared by sources and the specialist."""
from __future__ import annotations

import math
from datetime import datetime

from pydantic import BaseModel, ConfigDict, field_validator

from options_research.timeutil import require_utc

INTERVAL_SECONDS = {"1m": 60, "5m": 300, "15m": 900, "1h": 3600, "4h": 14400, "1d": 86400}


class Bar(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    open_time: datetime
    close_time: datetime
    open: float
    high: float
    low: float
    close: float
    volume: float

    _utc = field_validator("open_time", "close_time")(require_utc)

    def finite(self) -> bool:
        return all(math.isfinite(x) for x in (self.open, self.high, self.low, self.close, self.volume))
