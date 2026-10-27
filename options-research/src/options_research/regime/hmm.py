"""Gaussian hidden Markov model, standard library only (numpy hangs on the build machine).

Diagonal-covariance emissions, Baum-Welch EM with scaled forward-backward,
seeded k-means++ initialisation and a fixed number of restarts, so a fit is
deterministic for (data, seed). Nothing here knows what the features mean.
"""
from __future__ import annotations

import math
import random
from dataclasses import dataclass, field


@dataclass(frozen=True)
class HMMFit:
    n_states: int
    n_features: int
    log_likelihood: float
    start_prob: list[float]
    transition: list[list[float]]
    means: list[list[float]]
    variances: list[list[float]]
    iterations: int
    converged: bool
    seed: int
    restarts_tried: int
    history: list[float] = field(default_factory=list)   # log-likelihood per EM iteration of the winning restart

    def expected_duration(self, k: int) -> float:
        stay = self.transition[k][k]
        return float("inf") if stay >= 1.0 else 1.0 / (1.0 - stay)

    def stationary(self) -> list[float]:
        """Stationary distribution by power iteration (rows of A sum to 1)."""
        k = self.n_states
        p = [1.0 / k] * k
        for _ in range(500):
            nxt = [sum(p[j] * self.transition[j][i] for j in range(k)) for i in range(k)]
            s = sum(nxt) or 1.0
            nxt = [v / s for v in nxt]
            if max(abs(a - b) for a, b in zip(nxt, p)) < 1e-12:
                p = nxt
                break
            p = nxt
        return p


class GaussianHMM:
    def __init__(self, n_states: int = 3, seed: int = 7, restarts: int = 3, max_iter: int = 150, tol: float = 1e-4,
                 var_floor: float = 1e-3, sticky: float = 0.9, sticky_prior: float = 0.0):
        """`sticky_prior` is a pseudo-count added to each self-transition in the M-step (a MAP
        estimate with a Dirichlet prior on the rows of A). 0 = plain maximum likelihood."""
        if n_states < 2:
            raise ValueError("n_states must be >= 2")
        self.k = n_states
        self.seed = seed
        self.restarts = max(1, restarts)
        self.max_iter = max_iter
        self.tol = tol
        self.var_floor = var_floor
        self.sticky = sticky
        self.sticky_prior = max(0.0, sticky_prior)

    # ---- emissions --------------------------------------------------------
    @staticmethod
    def _log_emissions(X: list[list[float]], means: list[list[float]], variances: list[list[float]]) -> list[list[float]]:
        k = len(means)
        d = len(means[0])
        const = [-0.5 * sum(math.log(2.0 * math.pi * variances[s][j]) for j in range(d)) for s in range(k)]
        inv2 = [[0.5 / variances[s][j] for j in range(d)] for s in range(k)]
        out = []
        for x in X:
            row = []
            for s in range(k):
                m, iv = means[s], inv2[s]
                acc = const[s]
                for j in range(d):
                    diff = x[j] - m[j]
                    acc -= diff * diff * iv[j]
                row.append(acc)
            out.append(row)
        return out

    # ---- scaled forward-backward --------------------------------------------
    @staticmethod
    def _forward_backward(logB: list[list[float]], pi: list[float], A: list[list[float]]):
        """Returns (log_likelihood, gamma[t][k], xi_sum[j][k], alpha_hat[t][k])."""
        T, k = len(logB), len(pi)
        shift = [max(r) for r in logB]
        B = [[math.exp(logB[t][s] - shift[t]) for s in range(k)] for t in range(T)]
        alpha = [[0.0] * k for _ in range(T)]
        c = [0.0] * T
        a0 = [pi[s] * B[0][s] for s in range(k)]
        c[0] = sum(a0) or 1e-300
        alpha[0] = [v / c[0] for v in a0]
        for t in range(1, T):
            prev, Bt = alpha[t - 1], B[t]
            at = []
            for s in range(k):
                tot = 0.0
                for j in range(k):
                    tot += prev[j] * A[j][s]
                at.append(tot * Bt[s])
            c[t] = sum(at) or 1e-300
            alpha[t] = [v / c[t] for v in at]
        loglik = sum(math.log(ct) for ct in c) + sum(shift)
        beta = [[1.0] * k for _ in range(T)]
        for t in range(T - 2, -1, -1):
            nb, Bn, ct = beta[t + 1], B[t + 1], c[t + 1]
            bt = []
            for j in range(k):
                tot = 0.0
                for s in range(k):
                    tot += A[j][s] * Bn[s] * nb[s]
                bt.append(tot / ct)
            beta[t] = bt
        gamma = []
        for t in range(T):
            g = [alpha[t][s] * beta[t][s] for s in range(k)]
            z = sum(g) or 1e-300
            gamma.append([v / z for v in g])
        xi_sum = [[0.0] * k for _ in range(k)]
        for t in range(T - 1):
            at, nb, Bn = alpha[t], beta[t + 1], B[t + 1]
            cells = [[at[j] * A[j][s] * Bn[s] * nb[s] for s in range(k)] for j in range(k)]
            z = sum(sum(r) for r in cells) or 1e-300
            for j in range(k):
                for s in range(k):
                    xi_sum[j][s] += cells[j][s] / z
        return loglik, gamma, xi_sum, alpha

    # ---- initialisation -----------------------------------------------------
    def _init(self, X: list[list[float]], rng: random.Random):
        k, d, n = self.k, len(X[0]), len(X)
        # k-means++ seeding
        centres = [list(X[rng.randrange(n)])]
        while len(centres) < k:
            dists = [min(sum((x[j] - c[j]) ** 2 for j in range(d)) for c in centres) for x in X]
            tot = sum(dists)
            if tot <= 0:
                centres.append(list(X[rng.randrange(n)]))
                continue
            r = rng.random() * tot
            acc = 0.0
            pick = n - 1
            for i, dv in enumerate(dists):
                acc += dv
                if acc >= r:
                    pick = i
                    break
            centres.append(list(X[pick]))
        assign = [0] * n
        for _ in range(10):
            changed = False
            for i, x in enumerate(X):
                best = min(range(k), key=lambda s: sum((x[j] - centres[s][j]) ** 2 for j in range(d)))
                if best != assign[i]:
                    assign[i] = best
                    changed = True
            for s in range(k):
                members = [X[i] for i in range(n) if assign[i] == s]
                if members:
                    centres[s] = [sum(m[j] for m in members) / len(members) for j in range(d)]
            if not changed:
                break
        variances = []
        for s in range(k):
            members = [X[i] for i in range(n) if assign[i] == s]
            if len(members) < 2:
                variances.append([1.0] * d)
            else:
                variances.append([max(self.var_floor, sum((m[j] - centres[s][j]) ** 2 for m in members) / len(members))
                                  for j in range(d)])
        off = (1.0 - self.sticky) / (k - 1)
        A = [[self.sticky if i == j else off for j in range(k)] for i in range(k)]
        pi = [1.0 / k] * k
        return pi, A, centres, variances

    # ---- EM -------------------------------------------------------------------
    def _em(self, X: list[list[float]], seed: int) -> HMMFit:
        rng = random.Random(seed)
        pi, A, means, variances = self._init(X, rng)
        k, d, T = self.k, len(X[0]), len(X)
        eps = 1e-6
        prev = -math.inf
        history: list[float] = []
        converged = False
        it = 0
        for it in range(1, self.max_iter + 1):
            logB = self._log_emissions(X, means, variances)
            ll, gamma, xi_sum, _ = self._forward_backward(logB, pi, A)
            history.append(ll)
            if ll - prev < self.tol * max(1.0, abs(ll)) / 10.0 and it > 1:
                converged = True
                break
            prev = ll
            pi = [max(eps, gamma[0][s]) for s in range(k)]
            z = sum(pi)
            pi = [v / z for v in pi]
            occ = [sum(gamma[t][s] for t in range(T - 1)) for s in range(k)]
            kappa = self.sticky_prior
            A = [[(xi_sum[j][s] + eps + (kappa if s == j else 0.0)) / (occ[j] + k * eps + kappa) for s in range(k)]
                 for j in range(k)]
            A = [[v / sum(row) for v in row] for row in A]
            new_means, new_vars = [], []
            for s in range(k):
                w = [gamma[t][s] for t in range(T)]
                sw = sum(w) + eps
                m = [sum(w[t] * X[t][j] for t in range(T)) / sw for j in range(d)]
                v = [max(self.var_floor, sum(w[t] * (X[t][j] - m[j]) ** 2 for t in range(T)) / sw) for j in range(d)]
                new_means.append(m)
                new_vars.append(v)
            means, variances = new_means, new_vars
        return HMMFit(n_states=k, n_features=d, log_likelihood=history[-1], start_prob=pi, transition=A, means=means,
                      variances=variances, iterations=it, converged=converged, seed=seed, restarts_tried=1,
                      history=history)

    def fit(self, X: list[list[float]]) -> HMMFit:
        if len(X) < 2 * self.k + 2:
            raise ValueError(f"need at least {2 * self.k + 2} observations, got {len(X)}")
        best: HMMFit | None = None
        for r in range(self.restarts):
            f = self._em(X, self.seed + r)
            if best is None or f.log_likelihood > best.log_likelihood:
                best = f
        assert best is not None
        return HMMFit(**{**best.__dict__, "restarts_tried": self.restarts})

    # ---- inference ------------------------------------------------------------
    def posteriors(self, fit: HMMFit, X: list[list[float]]) -> list[list[float]]:
        """Smoothed P(state_t | all observations) for every t."""
        logB = self._log_emissions(X, fit.means, fit.variances)
        _, gamma, _, _ = self._forward_backward(logB, fit.start_prob, fit.transition)
        return gamma

    def filtered(self, fit: HMMFit, X: list[list[float]]) -> list[list[float]]:
        """Causal P(state_t | observations up to t)."""
        logB = self._log_emissions(X, fit.means, fit.variances)
        _, _, _, alpha = self._forward_backward(logB, fit.start_prob, fit.transition)
        return alpha

    def viterbi(self, fit: HMMFit, X: list[list[float]]) -> list[int]:
        logB = self._log_emissions(X, fit.means, fit.variances)
        k, T = fit.n_states, len(X)
        logA = [[math.log(max(1e-300, v)) for v in row] for row in fit.transition]
        delta = [[math.log(max(1e-300, fit.start_prob[s])) + logB[0][s] for s in range(k)]]
        back: list[list[int]] = []
        for t in range(1, T):
            row, ptr = [], []
            for s in range(k):
                best_j = max(range(k), key=lambda j: delta[-1][j] + logA[j][s])
                row.append(delta[-1][best_j] + logA[best_j][s] + logB[t][s])
                ptr.append(best_j)
            delta.append(row)
            back.append(ptr)
        path = [max(range(k), key=lambda s: delta[-1][s])]
        for ptr in reversed(back):
            path.append(ptr[path[-1]])
        path.reverse()
        return path
