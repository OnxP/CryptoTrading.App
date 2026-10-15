"""Deterministic intake: free text + explicit parameters -> ResearchRequest or
Clarification. No model is involved; natural-language interpretation (later)
may only *propose* these parameters, never bypass this validator.
"""
from __future__ import annotations

import uuid
from datetime import datetime, timedelta

from options_research.intake.lookup import InstrumentLookup
from options_research.intake.models import (
    TIME_DEPENDENT,
    Analysis,
    Clarification,
    Instrument,
    IntakeError,
    ResearchRequest,
    Window,
)
from options_research.timeutil import require_utc

_DEICTIC = ("this stock", "that stock", "the stock", "this company", "it ")


class IntakeValidator:
    def __init__(self, lookup: InstrumentLookup, default_window_days: int | None,
                 inline_only: bool = True):
        self._lookup = lookup
        self._default_days = default_window_days
        self._inline_only = inline_only

    def build(
        self,
        *,
        question: str,
        requester: str,
        request_time: datetime,
        as_of: datetime | None = None,
        window: tuple[datetime, datetime] | None = None,
        instrument_symbol: str | None = None,
        requested_analyses: list[Analysis] | None = None,
        horizon_days: int | None = None,
        delivery: str = "inline",
        parent_request_id: str | None = None,
        evidence_links: list[str] | None = None,
        pasted_text: str | None = None,
    ) -> ResearchRequest | Clarification:
        request_time = require_utc(request_time)
        disclosures: list[str] = []

        # --- delivery -------------------------------------------------------
        if delivery != "inline":
            if self._inline_only:
                raise IntakeError(f"delivery '{delivery}' is not available; M1 returns results inline only")
            raise IntakeError("non-inline delivery requires a separate authenticated delivery request")

        # --- as-of ----------------------------------------------------------
        if as_of is None:
            as_of_final, as_of_source = request_time, "request_time"
            disclosures.append(f"as_of defaulted to request time {request_time.isoformat()}")
        else:
            as_of_final = require_utc(as_of)
            as_of_source = "explicit"
            if as_of_final > request_time:
                raise IntakeError("as_of is in the future relative to the request time")

        # --- instrument -----------------------------------------------------
        if instrument_symbol:
            candidates = self._lookup.by_symbol(instrument_symbol)
        else:
            candidates = self._lookup.resolve_from_text(question)
        lowered = question.lower()
        if not candidates:
            hint = "which instrument" if any(d in lowered for d in _DEICTIC) else "no known instrument found"
            return Clarification(
                reason="instrument_unresolved",
                question=f"Which instrument do you mean ({hint})? Give a ticker and venue.",
            )
        if len(candidates) > 1:
            names = ", ".join(f"{c.name} ({c.canonical})" for c in candidates)
            return Clarification(
                reason="instrument_ambiguous",
                question=f"More than one instrument matches: {names}. Which one?",
                candidates=candidates,
            )
        instrument: Instrument = candidates[0]

        # --- window ---------------------------------------------------------
        if window is not None:
            start, end = require_utc(window[0]), require_utc(window[1])
            win = Window(start=start, end=end, source="explicit")
        elif self._default_days is not None:
            win = Window(start=as_of_final - timedelta(days=self._default_days), end=as_of_final,
                         source="default")
            disclosures.append(f"news window defaulted to the configured {self._default_days} days "
                               f"ending at as_of")
        else:
            return Clarification(reason="window_missing",
                                 question="What time window should the research cover?")
        if win.end > as_of_final:
            raise IntakeError("window end is after as_of")

        # --- analyses / horizon --------------------------------------------
        analyses = list(requested_analyses or [Analysis.NEWS_SUMMARY, Analysis.MARKET_REACTION,
                                               Analysis.EVIDENCE_VERIFICATION])
        if any(a in TIME_DEPENDENT for a in analyses) and horizon_days is None:
            return Clarification(
                reason="horizon_missing",
                question="A time-dependent trade analysis was requested. What trade horizon (days)?",
            )

        return ResearchRequest(
            request_id=uuid.uuid4().hex,
            requester=requester,
            original_question=question,
            instrument=instrument,
            evidence_links=list(evidence_links or []),
            pasted_text=pasted_text,
            window=win,
            as_of=as_of_final,
            as_of_source=as_of_source,
            horizon_days=horizon_days,
            requested_analyses=analyses,
            delivery="inline",
            parent_request_id=parent_request_id,
            disclosures=disclosures,
        )
