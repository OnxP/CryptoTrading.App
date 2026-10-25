"""SEC EDGAR connector: instrument reference data and filing evidence.

Endpoints (public, free, official):
  https://www.sec.gov/files/company_tickers_exchange.json   {"fields": [...], "data": [[cik, name, ticker, exchange], ...]}
  https://data.sec.gov/submissions/CIK##########.json       {"cik", "name", "tickers", "exchanges", "filings": {"recent": {...columns...}}}

SEC fair-access policy requires a descriptive User-Agent with contact details
and at most 10 requests per second. Both are enforced here.

Availability time of a filing is its `acceptanceDateTime` (when EDGAR accepted
it), NOT `filingDate`, which is a calendar date. Point-in-time research uses the
former (OPTIONS_M2_PLAN decision 3; CLAUDE_SETUP "Forecast and history store").
"""
from __future__ import annotations

from datetime import datetime, timezone

from options_research.config import Config
from options_research.connectors.http import HttpClient
from options_research.connectors.ratelimit import RateLimiter
from options_research.evidence import EvidenceRecord
from options_research.intake.lookup import InstrumentLookup
from options_research.intake.models import Instrument
from options_research.timeutil import utc_now

TICKERS_URL = "https://www.sec.gov/files/company_tickers_exchange.json"
SUBMISSIONS_URL = "https://data.sec.gov/submissions/CIK{cik:010d}.json"
EVIDENCE_FORMS = ("8-K", "8-K/A", "10-Q", "10-K", "10-K/A", "6-K", "20-F")

_EXCHANGE_TO_MIC = {"Nasdaq": "XNAS", "NYSE": "XNYS", "NYSE American": "XASE", "NYSE Arca": "ARCX",
                    "CBOE": "XCBO", "OTC": "OTCM"}


class EdgarConfigError(RuntimeError):
    pass


class EdgarClient:
    def __init__(self, http: HttpClient, config: Config, limiter: RateLimiter | None = None):
        edgar = config.raw.get("edgar", {})
        ua = (edgar.get("user_agent") or "").strip()
        if not ua or "@" not in ua:
            raise EdgarConfigError("[edgar] user_agent must be set to 'name email' per SEC fair-access policy")
        self._http = http
        self._headers = {"User-Agent": ua, "Accept": "application/json"}
        self._limiter = limiter or RateLimiter(float(edgar.get("max_rps", 10)))
        self._tickers_cache: list[dict] | None = None

    # ---- reference data ----------------------------------------------------
    def company_tickers(self) -> list[dict]:
        if self._tickers_cache is None:
            self._limiter.acquire()
            resp = self._http.get(TICKERS_URL, headers=self._headers)
            if resp.status != 200:
                raise RuntimeError(f"EDGAR company_tickers returned {resp.status}")
            fields = resp.json["fields"]
            self._tickers_cache = [dict(zip(fields, row)) for row in resp.json["data"]]
        return self._tickers_cache

    def lookup(self) -> "EdgarInstrumentLookup":
        return EdgarInstrumentLookup(self.company_tickers())

    # ---- filings as evidence ----------------------------------------------
    def submissions(self, symbol: str) -> dict:
        matches = self.lookup().by_symbol(symbol)
        if len(matches) != 1:
            raise LookupError(f"symbol {symbol!r} resolves to {len(matches)} instruments")
        cik = self.lookup().cik_for(symbol)
        self._limiter.acquire()
        resp = self._http.get(SUBMISSIONS_URL.format(cik=cik), headers=self._headers)
        if resp.status != 200:
            raise RuntimeError(f"EDGAR submissions returned {resp.status}")
        return resp.json

    def filing_evidence(self, symbol: str, retrieved_at: datetime | None = None,
                        forms: tuple[str, ...] = EVIDENCE_FORMS) -> list[EvidenceRecord]:
        sub = self.submissions(symbol)
        retrieved_at = retrieved_at or utc_now()
        recent = sub["filings"]["recent"]
        cols = {k: recent[k] for k in ("accessionNumber", "filingDate", "acceptanceDateTime", "form",
                                       "primaryDocument", "primaryDocDescription")}
        cik = int(sub["cik"])
        out: list[EvidenceRecord] = []
        for i in range(len(cols["accessionNumber"])):
            form = cols["form"][i]
            if form not in forms:
                continue
            acc = cols["accessionNumber"][i]
            acc_nodash = acc.replace("-", "")
            filing_date = datetime.fromisoformat(cols["filingDate"][i]).replace(tzinfo=timezone.utc)
            accepted = datetime.fromisoformat(cols["acceptanceDateTime"][i].replace("Z", "+00:00")).astimezone(timezone.utc)
            # acceptance can precede the filing calendar date in UTC terms (late-evening ET filings); the
            # record keeps both and the store filters on acceptance.
            url = f"https://www.sec.gov/Archives/edgar/data/{cik}/{acc_nodash}/{cols['primaryDocument'][i]}"
            desc = cols["primaryDocDescription"][i] or form
            out.append(EvidenceRecord(
                id=f"edgar:{acc}",
                source_id=url,
                publisher="SEC EDGAR",
                origin_id=None,
                published_at=min(filing_date, accepted),
                first_available_at=accepted,
                retrieved_at=retrieved_at,
                title=f"{form}: {desc}",
                text=f"EDGAR filing {form} ({desc}) by {sub.get('name', symbol)}; accession {acc}.",
                entities=[symbol.upper()],
            ))
        return out


class EdgarInstrumentLookup(InstrumentLookup):
    """Trusted lookup over SEC company_tickers_exchange rows."""

    def __init__(self, rows: list[dict]):
        self._rows = rows
        self._by_symbol: dict[str, list[dict]] = {}
        for r in rows:
            self._by_symbol.setdefault(str(r["ticker"]).upper(), []).append(r)

    @staticmethod
    def _to_instrument(r: dict) -> Instrument:
        return Instrument(symbol=str(r["ticker"]).upper(), venue=_EXCHANGE_TO_MIC.get(r.get("exchange") or "", "XXXX"),
                          currency="USD", name=str(r["name"]))

    def cik_for(self, symbol: str) -> int:
        rows = self._by_symbol.get(symbol.upper(), [])
        if len(rows) != 1:
            raise LookupError(symbol)
        return int(rows[0]["cik"])

    def by_symbol(self, symbol: str) -> list[Instrument]:
        return [self._to_instrument(r) for r in self._by_symbol.get(symbol.upper(), [])]

    def by_name(self, text: str) -> list[Instrument]:
        t = text.lower()
        hits: dict[str, Instrument] = {}
        import re
        words = set(re.findall(r"[a-z]{3,}", t))
        for r in self._rows:
            name = str(r["name"]).lower()
            if name in t:
                ins = self._to_instrument(r); hits[ins.canonical] = ins
        if not hits:
            for r in self._rows:
                first = str(r["name"]).lower().split()[0] if r["name"] else ""
                if first and first in words and first not in {"the", "inc", "corp", "ltd", "plc", "co"}:
                    ins = self._to_instrument(r); hits[ins.canonical] = ins
        return list(hits.values())
