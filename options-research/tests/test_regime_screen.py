import json
import re

import pytest

from options_research.cli import main
from options_research.config import PROJECT_ROOT, load_config
from options_research.connectors import ReplayHttpClient
from options_research.connectors.yahoo import YahooClient
from options_research.regime import ScreenLedger, ScreenRecord, render_screen, run_screen

CASSETTES = PROJECT_ROOT / "fixtures" / "cassettes" / "yahoo"
FORBIDDEN = re.compile(r"\b(buy|sell|shares to buy|position size|quantity)\b", re.I)


@pytest.fixture
def rec() -> ScreenRecord:
    cfg = load_config()
    progress = []
    r = run_screen(YahooClient(ReplayHttpClient(CASSETTES), cfg), cfg, max_price=10.0, min_price=1.0, top=4,
                   benchmark="BENCH", progress=progress.append)
    assert progress[0] == "benchmark BENCH" and progress[-1].endswith("NOPE")
    return r


def test_rows_follow_screener_order_and_each_outcome_is_explicit(rec):
    assert [r.symbol for r in rec.rows] == ["FIXT", "ACME", "NEWB", "NOPE"]
    assert rec.universe_total == 937 and rec.params.top == 4 and rec.params.max_price == 10.0
    fixt, acme, newb, nope = rec.rows
    assert fixt.assessment and fixt.assessment.flag == "FRESH" and fixt.assessment.turning_point
    assert acme.assessment and acme.assessment.current.startswith("BEAR") and not acme.assessment.turning_point
    assert newb.assessment and newb.assessment.unavailable_reason and "need 120" in newb.assessment.unavailable_reason
    assert nope.assessment is None and nope.error and "Yahoo error" in nope.error
    assert fixt.price == 6.42 and fixt.day_volume == 48_000_000 and fixt.quote_time is not None
    assert fixt.market_cap == int(6.42 * 4e8)
    assert fixt.sector == "Technology" and fixt.industry == "Semiconductors" and acme.sector == "Industrials"
    assert nope.sector is None


def test_benchmark_first_and_disclosures_present(rec):
    assert rec.benchmark is not None and rec.benchmark.symbol == "BENCH"
    assert rec.benchmark.current.startswith("BULL") and rec.benchmark.days_in_regime >= 20
    assert rec.benchmark.flag in ("STABLE", "OVERDUE") and rec.benchmark.p_current > 0.9
    assert any("not forecasts" in d for d in rec.disclosures) and any("no position size" in d for d in rec.disclosures)


def test_render_is_ascii_has_columns_and_no_advice(rec):
    text = render_screen(rec, detail=True)
    text.encode("ascii")
    assert "MARKET (BENCH" in text and "REGIME" in text and "FLAG" in text and "MKTCAP" in text
    assert "2.6B" in text                                     # FIXT fixture market cap 6.42 * 4e8
    assert "SECTOR" in text and "Technology" in text and "/ Semiconductors" in text
    assert "turning-point candidates (FRESH or WEAKENING): FIXT" in text
    assert "UNAVAIL" in text and "ERR" in text
    assert not FORBIDDEN.search(text.split("* Source")[0])   # body never says buy/sell; disclosures may


def test_ledger_round_trip_and_json(rec, tmp_path):
    led = ScreenLedger(tmp_path / "screens.jsonl")
    led.append(rec)
    led.append(rec)
    back = led.all()
    assert len(back) == 2 and back[0].screen_id == rec.screen_id
    assert back[0].rows[0].assessment.p_current == rec.rows[0].assessment.p_current
    json.loads(rec.model_dump_json())


def test_cli_screen_replay(tmp_path, capsys):
    code = main(["screen", "--replay-dir", str(CASSETTES), "--top", "3", "--benchmark", "BENCH", "--data-dir", str(tmp_path),
                 "--quiet"])
    assert code == 0
    out = capsys.readouterr().out
    assert "REGIME SCREEN" in out and " FIXT " in out and "NEWB" in out
    assert (tmp_path / "screens.jsonl").exists()
    code = main(["screen", "--replay-dir", str(CASSETTES), "--top", "3", "--benchmark", "", "--data-dir", str(tmp_path),
                 "--quiet", "--json"])
    assert code == 0
    rec = json.loads(capsys.readouterr().out)
    assert rec["benchmark"] is None and len(rec["rows"]) == 3


def test_cli_screen_missing_cassette_is_unavailable_exit_5(tmp_path, capsys):
    code = main(["screen", "--replay-dir", str(tmp_path), "--top", "3", "--benchmark", "", "--data-dir", str(tmp_path),
                 "--quiet"])
    assert code == 5
    assert "UNAVAILABLE" in capsys.readouterr().err


def test_watchlist_mode_skips_screener_and_fills_quote_from_profile(tmp_path, capsys):
    cfg = load_config()
    rec = run_screen(YahooClient(ReplayHttpClient(CASSETTES), cfg), cfg, benchmark="", symbols=["fixt", " ACME", "NOPE"])
    assert rec.params.symbols == ["FIXT", "ACME", "NOPE"] and rec.universe_total == 3 and rec.benchmark is None
    fixt = rec.rows[0]
    assert fixt.price == 6.42 and fixt.market_cap == int(6.42 * 4e8) and fixt.sector == "Technology"
    assert rec.rows[2].assessment is None and rec.rows[2].price is None
    text = render_screen(rec)
    assert "watchlist FIXT, ACME, NOPE" in text
    code = main(["screen", "--replay-dir", str(CASSETTES), "--symbols", "FIXT,ACME", "--benchmark", "", "--data-dir",
                 str(tmp_path), "--quiet"])
    assert code == 0 and "watchlist FIXT, ACME" in capsys.readouterr().out
