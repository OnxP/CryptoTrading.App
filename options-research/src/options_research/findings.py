"""Typed findings every agent returns inside an Envelope.

Each finding carries `evidence_refs`. A finding is a claim about the world or
a reference to a code-produced record; it is never an instruction to act.
"""
from __future__ import annotations

from datetime import date, datetime
from enum import Enum
from typing import Annotated, Literal, Union

from pydantic import BaseModel, ConfigDict, Field, field_validator

from options_research.timeutil import require_utc


class EvidenceRef(BaseModel):
    """Pointer to an immutable record: evidence, cluster, snapshot or receipt."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    kind: Literal["evidence", "cluster", "snapshot", "receipt", "report", "envelope"]
    id: str = Field(min_length=1)
    content_hash: str | None = None


class Severity(str, Enum):
    LOW = "LOW"
    MEDIUM = "MEDIUM"
    HIGH = "HIGH"


class _Finding(BaseModel):
    model_config = ConfigDict(frozen=True, extra="forbid")

    evidence_refs: list[EvidenceRef] = Field(min_length=1)
    note: str = ""


class EventCandidate(_Finding):
    """News collector output: one source cluster labelled, not a trade view."""

    type: Literal["event_candidate"] = "event_candidate"
    cluster_id: str
    headline: str
    entities: list[str] = Field(default_factory=list)
    event_time: datetime | None = None
    fact_or_rumour: Literal["fact", "rumour", "unclear"] = "unclear"
    novelty: Literal["new", "update", "repeat"] = "new"
    independent_sources: int = Field(ge=0)
    missing_corroboration: list[str] = Field(default_factory=list)
    implication: Literal["bullish", "bearish", "mixed", "unclear"] = "unclear"
    catalyst: str | None = Field(default=None, description="scheduled future event named in the story, if any")
    injection_suspected: bool = False

    _utc = field_validator("event_time")(lambda v: require_utc(v) if v is not None else v)


class ClaimStatus(str, Enum):
    SOURCE_SUPPORTED = "SOURCE_SUPPORTED"
    CONTRADICTED = "CONTRADICTED"
    INSUFFICIENT = "INSUFFICIENT"
    STALE = "STALE"


class ClaimVerification(_Finding):
    """Evidence verifier output for one claim."""

    type: Literal["claim_verification"] = "claim_verification"
    claim_id: str
    claim_text: str
    status: ClaimStatus
    checks_performed: list[str] = Field(default_factory=list)
    severity: Severity = Severity.LOW
    unresolved_disagreements: list[str] = Field(default_factory=list)
    expires_at: datetime | None = None

    _utc = field_validator("expires_at")(lambda v: require_utc(v) if v is not None else v)


class MarketSnapshotRef(_Finding):
    """Reference to a code-produced market snapshot and its quality."""

    type: Literal["market_snapshot_ref"] = "market_snapshot_ref"
    snapshot_id: str
    instrument: str
    as_of: datetime
    last_complete_bar: datetime | None = None
    quality: Literal["OK", "DEGRADED", "UNAVAILABLE"]
    quality_failures: list[str] = Field(default_factory=list)

    _utc = field_validator("as_of", "last_complete_bar")(
        lambda v: require_utc(v) if v is not None else v
    )


class BriefingSection(_Finding):
    """One assembled section of a briefing; BLOCKED/PARTIAL when a specialist failed."""

    type: Literal["briefing_section"] = "briefing_section"
    section: Literal[
        "instrument",
        "scope",
        "material_news",
        "market_reaction",
        "bullish_evidence",
        "bearish_evidence",
        "catalysts",
        "uncertainties",
        "invalidation_conditions",
        "next_investigations",
        "trade_ideas_advisory",
    ]
    status: Literal["OK", "PARTIAL", "BLOCKED"]
    text: str
    blocked_reason: str | None = None


# ---- M2: options structures (advisory) ------------------------------------------
Direction = Literal["bullish", "bearish"]
Alternative = Literal["structure", "underlying_long_100", "no_trade"]
CORRECT_DIRECTION_CAVEAT = "Expensive volatility can make a correct directional thesis lose money."


class StructureLeg(BaseModel):
    """One quoted leg, exactly as the chain snapshot had it. Never a fabricated quote."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    occ_symbol: str
    right: Literal["call", "put"]
    strike: float
    expiry: date
    side: Literal[1, -1]
    qty: Literal[1] = 1
    bid: float
    ask: float
    quote_time: datetime
    bid_size: int | None = None
    ask_size: int | None = None
    open_interest: int | None = None
    volume: int | None = None
    liquidity_flags: list[str] = Field(default_factory=list)

    _utc = field_validator("quote_time")(require_utc)


class ScenarioRow(BaseModel):
    """Compact scenario table row: one spot move, P&L per one contract set after fees."""

    model_config = ConfigDict(frozen=True, extra="forbid")

    spot_pct: float
    spot: float
    pnl_expiry: float | None
    pnl_horizon_iv_down: float | None
    pnl_horizon_iv_flat: float | None
    pnl_horizon_iv_up: float | None
    underlying_long_100_pnl: float


class StructureComparison(_Finding):
    """Pricing specialist output: one alternative (structure, underlying, or no trade).

    Direction is an INPUT restated here, never a forecast. No quantities beyond
    one contract set, no probabilities, no expected value.
    """

    type: Literal["structure_comparison"] = "structure_comparison"
    comparison_id: str
    alternative: Alternative
    kind: str | None = None                          # StructureKind for alternative == "structure"
    direction: Direction | None = None               # thesis the alternative expresses, stated as input
    expiry: date | None = None
    expiry_at: datetime | None = None
    legs: list[StructureLeg] = Field(default_factory=list)
    net_debit_per_share: float | None = None         # negative for a credit
    net_per_contract_set: float | None = None        # debit paid (positive) or credit received (negative), before fees
    fees_total: float | None = None
    fees_assumption: str = ""
    max_loss: float | None = None
    max_gain: float | None = None                    # None with a structure = unbounded (long call)
    max_gain_unbounded: bool = False
    breakevens: list[float] = Field(default_factory=list)
    scenario_grid_id: str | None = None
    scenario_rows: list[ScenarioRow] = Field(default_factory=list)
    horizon_end: datetime | None = None
    liquidity_flags: list[str] = Field(default_factory=list)
    correct_direction_loss_cells: list[str] = Field(default_factory=list)
    caveats: list[str] = Field(default_factory=list)
    receipt_ids: list[str] = Field(default_factory=list)
    thesis_only: bool = False

    _utc = field_validator("expiry_at", "horizon_end")(lambda v: require_utc(v) if v is not None else v)

    @property
    def quote_times(self) -> list[datetime]:
        return [l.quote_time for l in self.legs]


class VolAssessment(_Finding):
    """Vol specialist output (code only): what the underlying realised vs what the chain implies.

    Numbers are annualised vol fractions unless named *_points (percentage points).
    No probability, no direction, no recommendation to buy or sell volatility.
    """

    type: Literal["vol_assessment"] = "vol_assessment"
    instrument: str
    as_of: datetime
    bars_used: int
    realised_close_to_close: float | None = None
    realised_parkinson: float | None = None
    realised_yang_zhang: float | None = None
    realised_ewma: float | None = None
    forecast: float | None = None
    forecast_low: float | None = None
    forecast_high: float | None = None
    forecast_method: str = ""
    realised_unavailable_reason: str | None = None
    expiry: str | None = None
    atm_iv: float | None = None
    atm_legs: list[str] = Field(default_factory=list)
    skew_proxy: float | None = None
    skew_legs: list[str] = Field(default_factory=list)
    iv_rank: float | None = None
    iv_rank_reason: str = "unavailable: no IV history store"
    premium_points: float | None = None
    implied_unavailable_reason: str | None = None
    receipt_ids: list[str] = Field(default_factory=list)

    _utc = field_validator("as_of")(require_utc)


class RiskVerdict(str, Enum):
    PASS = "PASS"
    BLOCK = "BLOCK"
    REVIEW = "REVIEW"


class RiskReview(_Finding):
    """Risk reviewer output for one comparison. PASS means no blocking finding, never permission."""

    type: Literal["risk_review"] = "risk_review"
    comparison_id: str
    verdict: RiskVerdict
    reasons: list[str] = Field(default_factory=list)
    checks_performed: list[str] = Field(default_factory=list)
    expires_at: datetime

    _utc = field_validator("expires_at")(require_utc)


Finding = Annotated[
    Union[EventCandidate, ClaimVerification, MarketSnapshotRef, BriefingSection, StructureComparison, RiskReview,
          VolAssessment],
    Field(discriminator="type"),
]
