"""Trusted instrument lookup. M1 reads a fixture; the interface is the seam for
a real reference-data source later. Lookup never guesses: it returns every match.
"""
from __future__ import annotations

import json
import re
from abc import ABC, abstractmethod
from pathlib import Path

from options_research.intake.models import Instrument

_TOKEN = re.compile(r"[A-Za-z][A-Za-z.\-]{0,9}")


class InstrumentLookup(ABC):
    @abstractmethod
    def by_symbol(self, symbol: str) -> list[Instrument]: ...

    @abstractmethod
    def by_name(self, text: str) -> list[Instrument]: ...

    def resolve_from_text(self, text: str) -> list[Instrument]:
        """All instruments plausibly referenced in free text (symbols and names)."""
        found: dict[str, Instrument] = {}
        for ins in self.by_name(text):
            found[ins.canonical] = ins
        for tok in _TOKEN.findall(text):
            if tok.isupper() and 2 <= len(tok) <= 6:
                for ins in self.by_symbol(tok):
                    found[ins.canonical] = ins
        return list(found.values())


class FixtureInstrumentLookup(InstrumentLookup):
    def __init__(self, path: Path):
        with open(path, encoding="utf-8") as fh:
            raw = json.load(fh)
        self._instruments = [Instrument(**{k: v for k, v in i.items() if k != "aliases"}) for i in raw["instruments"]]
        self._aliases: list[tuple[str, Instrument]] = []
        for ins, rec in zip(self._instruments, raw["instruments"]):
            names = [ins.name] + list(rec.get("aliases", []))
            for n in names:
                self._aliases.append((n.lower(), ins))

    def by_symbol(self, symbol: str) -> list[Instrument]:
        s = symbol.upper()
        return [i for i in self._instruments if i.symbol == s]

    def by_name(self, text: str) -> list[Instrument]:
        t = text.lower()
        hits: dict[str, Instrument] = {}
        # Full alias hits first; then a bare first word shared by several names
        # (e.g. "Acme") counts as a hit for each, which makes it ambiguous.
        for alias, ins in self._aliases:
            if alias in t:
                hits[ins.canonical] = ins
        if not hits:
            words = set(re.findall(r"[a-z]{3,}", t))
            for alias, ins in self._aliases:
                first = alias.split()[0]
                if first in words and first not in {"the", "inc", "corp", "ltd", "plc"}:
                    hits[ins.canonical] = ins
        return list(hits.values())
