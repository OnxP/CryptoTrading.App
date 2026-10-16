"""Black-Scholes-Merton pricing, greeks and implied volatility. Stdlib only.

European model used as an approximation for American-style US equity options.
Every receipt built on these numbers must carry the assumption flag
`AMERICAN_AS_EUROPEAN` (early exercise and discrete dividends not modelled).
"""
from __future__ import annotations

import math
from dataclasses import dataclass
from typing import Literal

Right = Literal["call", "put"]
AMERICAN_AS_EUROPEAN = "American options priced with a European (BSM) model: early exercise and discrete dividends not modelled"


class ImpliedVolUnavailable(ValueError):
    pass


def _ncdf(x: float) -> float:
    return 0.5 * math.erfc(-x / math.sqrt(2.0))


def _npdf(x: float) -> float:
    return math.exp(-0.5 * x * x) / math.sqrt(2.0 * math.pi)


def intrinsic(S: float, K: float, right: Right) -> float:
    return max(0.0, S - K) if right == "call" else max(0.0, K - S)


def bsm_price(S: float, K: float, T: float, r: float, q: float, sigma: float, right: Right) -> float:
    if S <= 0 or K <= 0:
        raise ValueError("S and K must be positive")
    if T <= 0 or sigma <= 0:
        # at expiry or with zero vol: discounted forward intrinsic collapses to intrinsic at T=0;
        # for sigma=0 and T>0 use the deterministic forward
        if T <= 0:
            return intrinsic(S, K, right)
        F = S * math.exp((r - q) * T)
        disc = math.exp(-r * T)
        return disc * intrinsic(F, K, right)
    sq = sigma * math.sqrt(T)
    d1 = (math.log(S / K) + (r - q + 0.5 * sigma * sigma) * T) / sq
    d2 = d1 - sq
    if right == "call":
        return S * math.exp(-q * T) * _ncdf(d1) - K * math.exp(-r * T) * _ncdf(d2)
    return K * math.exp(-r * T) * _ncdf(-d2) - S * math.exp(-q * T) * _ncdf(-d1)


@dataclass(frozen=True)
class Greeks:
    delta: float
    gamma: float
    theta_per_day: float
    vega_per_vol_point: float
    rho_per_rate_point: float


def bsm_greeks(S: float, K: float, T: float, r: float, q: float, sigma: float, right: Right) -> Greeks:
    if T <= 0 or sigma <= 0:
        itm = intrinsic(S, K, right) > 0
        d = (1.0 if right == "call" else -1.0) if itm else 0.0
        return Greeks(delta=d, gamma=0.0, theta_per_day=0.0, vega_per_vol_point=0.0, rho_per_rate_point=0.0)
    sq = sigma * math.sqrt(T)
    d1 = (math.log(S / K) + (r - q + 0.5 * sigma * sigma) * T) / sq
    d2 = d1 - sq
    eq, er = math.exp(-q * T), math.exp(-r * T)
    pdf = _npdf(d1)
    gamma = eq * pdf / (S * sq)
    vega = S * eq * pdf * math.sqrt(T)
    if right == "call":
        delta = eq * _ncdf(d1)
        theta = -S * eq * pdf * sigma / (2 * math.sqrt(T)) - r * K * er * _ncdf(d2) + q * S * eq * _ncdf(d1)
        rho = K * T * er * _ncdf(d2)
    else:
        delta = -eq * _ncdf(-d1)
        theta = -S * eq * pdf * sigma / (2 * math.sqrt(T)) + r * K * er * _ncdf(-d2) - q * S * eq * _ncdf(-d1)
        rho = -K * T * er * _ncdf(-d2)
    return Greeks(delta=delta, gamma=gamma, theta_per_day=theta / 365.0, vega_per_vol_point=vega / 100.0,
                  rho_per_rate_point=rho / 100.0)


def implied_vol(price: float, S: float, K: float, T: float, r: float, q: float, right: Right,
                lo: float = 1e-4, hi: float = 5.0, tol: float = 1e-10, max_iter: int = 200) -> float:
    """Bisection on a bracketed interval. Explicit failure outside no-arbitrage bounds."""
    if T <= 0:
        raise ImpliedVolUnavailable("option has expired (T <= 0)")
    lower = bsm_price(S, K, T, r, q, 0.0, right)  # deterministic-forward intrinsic (price floor)
    if price < lower - 1e-12:
        raise ImpliedVolUnavailable(f"price {price:.6f} below no-arbitrage floor {lower:.6f}")
    if price - lower < 1e-6:
        raise ImpliedVolUnavailable(f"price {price:.6f} is at the intrinsic floor; volatility is not identifiable")
    upper = bsm_price(S, K, T, r, q, hi, right)
    if price > upper + 1e-12:
        raise ImpliedVolUnavailable(f"price {price:.6f} above price at sigma={hi}")
    a, b = lo, hi
    fa = bsm_price(S, K, T, r, q, a, right) - price
    if abs(fa) < tol:
        return a
    for _ in range(max_iter):
        m = 0.5 * (a + b)
        fm = bsm_price(S, K, T, r, q, m, right) - price
        if abs(fm) < tol or (b - a) < 1e-12:
            return m
        if (fa < 0) == (fm < 0):
            a, fa = m, fm
        else:
            b = m
    return 0.5 * (a + b)


def year_fraction(start, end) -> float:
    return max(0.0, (end - start).total_seconds() / (365.0 * 86400.0))
