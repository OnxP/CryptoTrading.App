import json
from datetime import datetime, timezone

import pytest

from options_research.connectors import (
    Cassette,
    NoCassette,
    RecordingHttpClient,
    ReplayHttpClient,
    cassette_key,
)
from options_research.connectors.cassette import scrub_header_names, write_cassette
from options_research.connectors.http import HttpResponse

T0 = datetime(2026, 10, 16, 12, 0, tzinfo=timezone.utc)


class StubReal:
    def __init__(self):
        self.calls = []

    def get(self, url, params=None, headers=None):
        self.calls.append((url, dict(params or {}), dict(headers or {})))
        return HttpResponse(status=200, json={"ok": True, "echo": dict(params or {})}, received_at=T0)


def test_key_stable_under_param_reordering():
    a = cassette_key("GET", "https://x/y", {"b": "2", "a": "1"})
    b = cassette_key("get", "https://x/y", {"a": "1", "b": "2"})
    assert a == b
    assert cassette_key("GET", "https://x/y", {"a": "1"}) != a


def test_replay_returns_identical_and_misses_raise(tmp_path):
    c = Cassette(key=cassette_key("GET", "https://x/y", {"s": "FIXT"}), method="GET", url="https://x/y",
                 params={"s": "FIXT"}, header_names=["Accept"], status=200, json={"v": [1, 2]},
                 received_at=T0, source="docs")
    write_cassette(tmp_path, c)
    client = ReplayHttpClient(tmp_path)
    r = client.get("https://x/y", {"s": "FIXT"})
    assert r.status == 200 and r.json == {"v": [1, 2]} and r.received_at == T0 and r.source == "docs"
    with pytest.raises(NoCassette):
        client.get("https://x/y", {"s": "OTHER"})


def test_recording_strips_secret_headers(tmp_path):
    real = StubReal()
    rec = RecordingHttpClient(real, tmp_path, source="live")
    r = rec.get("https://api.example/v1/q", {"symbols": "FIXT"},
                {"Authorization": "Bearer SECRET123", "Accept": "application/json", "X-Api-Key": "k"})
    assert r.json["ok"] is True
    files = list(tmp_path.glob("*.json"))
    assert len(files) == 1
    text = files[0].read_text()
    assert "SECRET123" not in text and '"k"' not in text
    d = json.loads(text)
    assert d["request"]["header_names"] == ["Accept", "Authorization (redacted)", "X-Api-Key (redacted)"]
    assert d["source"] == "live"
    # and the recording is replayable
    assert ReplayHttpClient(tmp_path).get("https://api.example/v1/q", {"symbols": "FIXT"}).json == r.json


def test_scrub_marks_token_like_names():
    assert scrub_header_names({"Cookie": "x", "User-Agent": "u"}) == ["Cookie (redacted)", "User-Agent"]
