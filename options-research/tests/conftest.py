"""Test harness: no network, ever. Any socket connection attempt fails the test."""
from __future__ import annotations

import socket

import pytest


class NetworkBlocked(RuntimeError):
    pass


_real_connect = socket.socket.connect
_real_create_connection = socket.create_connection


def _blocked(*_args, **_kwargs):
    raise NetworkBlocked("network access is forbidden in options-research tests")


@pytest.fixture(autouse=True)
def no_network(monkeypatch):
    monkeypatch.setattr(socket.socket, "connect", _blocked)
    monkeypatch.setattr(socket, "create_connection", _blocked)
    yield
