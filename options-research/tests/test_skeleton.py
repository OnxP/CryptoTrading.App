import socket

import pytest

from options_research import __version__
from options_research.config import load_config
from tests.conftest import NetworkBlocked


def test_version():
    assert __version__ == "0.1.0"


def test_default_config_is_offline():
    cfg = load_config()
    assert cfg.project_id == "options-research"
    assert cfg.model.enabled is False
    assert cfg.news_window_days == 7
    assert cfg.inline_only is True
    assert cfg.ledger_dir.name == "data"


def test_network_is_blocked_in_tests():
    with pytest.raises(NetworkBlocked):
        socket.create_connection(("127.0.0.1", 9))
    with pytest.raises(NetworkBlocked):
        socket.socket().connect(("127.0.0.1", 9))


def test_no_execution_modules_exist():
    """The project must never grow an order/broker/execute code path."""
    import pkgutil

    import options_research

    names = [m.name for m in pkgutil.walk_packages(options_research.__path__, "options_research.")]
    forbidden = ("order", "broker", "execut", "trade_submit")
    offenders = [n for n in names if any(f in n.lower() for f in forbidden)]
    assert offenders == []
