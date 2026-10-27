import math
import random

import pytest

from options_research.regime.hmm import GaussianHMM


def two_regime_series(seed: int = 1, n: int = 600):
    rng = random.Random(seed)
    mu = [[0.4, -0.5, -0.5], [-0.6, 0.9, 1.0]]
    sd = [[0.7, 0.8, 0.6], [1.2, 1.0, 1.1]]
    stay = [0.97, 0.95]
    s, true, X = 0, [], []
    for _ in range(n):
        if rng.random() > stay[s]:
            s = 1 - s
        true.append(s)
        X.append([rng.gauss(mu[s][j], sd[s][j]) for j in range(3)])
    return X, true


def _accuracy(fit, model, X, true):
    g = model.posteriors(fit, X)
    path = [max(range(2), key=lambda k: row[k]) for row in g]
    hi = 0 if fit.means[0][0] > fit.means[1][0] else 1     # the state with the higher return feature is regime 0
    return sum(1 for p, t in zip(path, true) if (0 if p == hi else 1) == t) / len(true)


def test_recovers_two_planted_regimes():
    X, true = two_regime_series()
    model = GaussianHMM(n_states=2, restarts=3)
    fit = model.fit(X)
    assert fit.converged
    assert _accuracy(fit, model, X, true) > 0.9
    assert all(fit.transition[k][k] > 0.85 for k in range(2))


def test_fit_is_deterministic_for_data_and_seed():
    X, _ = two_regime_series(seed=3)
    a = GaussianHMM(n_states=3, seed=11, restarts=2).fit(X)
    b = GaussianHMM(n_states=3, seed=11, restarts=2).fit(X)
    assert a.log_likelihood == b.log_likelihood and a.means == b.means and a.transition == b.transition
    assert a.seed == b.seed and a.restarts_tried == 2


def test_posteriors_are_distributions_and_loglik_increases():
    X, _ = two_regime_series(seed=5, n=300)
    model = GaussianHMM(n_states=3, restarts=1)
    fit = model.fit(X)
    g = model.posteriors(fit, X)
    assert len(g) == len(X) and all(abs(sum(r) - 1.0) < 1e-9 for r in g)
    assert all(b >= a - 1e-6 for a, b in zip(fit.history, fit.history[1:]))   # EM is monotone
    assert all(abs(sum(row) - 1.0) < 1e-9 for row in fit.transition)
    assert len(model.viterbi(fit, X)) == len(X)


def test_sticky_prior_raises_self_transition():
    X, _ = two_regime_series(seed=9)
    plain = GaussianHMM(n_states=3, restarts=1, sticky_prior=0.0).fit(X)
    sticky = GaussianHMM(n_states=3, restarts=1, sticky_prior=50.0).fit(X)
    assert min(sticky.transition[k][k] for k in range(3)) > min(plain.transition[k][k] for k in range(3))
    assert sticky.log_likelihood <= plain.log_likelihood + 1e-6      # MAP never beats ML on data likelihood


def test_expected_duration_and_stationary():
    X, _ = two_regime_series(seed=2)
    fit = GaussianHMM(n_states=2, restarts=1).fit(X)
    for k in range(2):
        assert math.isclose(fit.expected_duration(k), 1.0 / (1.0 - fit.transition[k][k]))
    st = fit.stationary()
    assert abs(sum(st) - 1.0) < 1e-9 and all(v > 0 for v in st)


def test_too_few_observations_raise():
    with pytest.raises(ValueError):
        GaussianHMM(n_states=3).fit([[0.0, 0.0, 0.0]] * 5)
    with pytest.raises(ValueError):
        GaussianHMM(n_states=1)


def test_outlier_does_not_break_numerics():
    X, _ = two_regime_series(seed=4, n=200)
    X[100] = [5.0, 5.0, 5.0]                         # winsor bound; still finite
    fit = GaussianHMM(n_states=3, restarts=1).fit(X)
    assert math.isfinite(fit.log_likelihood)
    assert all(math.isfinite(v) for row in fit.means for v in row)
