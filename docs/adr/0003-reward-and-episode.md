# ADR-0003: Episode termination and reward shape

- **Status:** Accepted
- **Date:** 2026-05-21

## Context

After choosing RL (ADR-0001) and a movement-inclusive action space (ADR-0002),
the next decision is when an episode ends and what reward signal drives
learning. The reward function determines what the agent actually optimizes
for; getting it wrong produces a policy that "wins the metric" but does the
wrong thing in practice.

User proposed a sparse reward (per-hit, clear-bonus, wasted-bullet penalty,
stagnation penalty). This is recorded faithfully but augmented with shaped
scaffolding rewards — agreed by the user — to avoid the "agent never fires
and collapses to stand-still" failure mode common with sparse rewards in
movement-inclusive environments.

## Decision

### Episode termination

An episode ends when **either**:
- All 4 targets in `S_Content_Overview` have `TargetScript.isHit == true`
  (success), **or**
- 60 game-seconds elapsed (timeout).

### Reward function

| Term                                                        | Value                                       | Sparse / Shaped |
|-------------------------------------------------------------|---------------------------------------------|-----------------|
| Target hit                                                  | `+1.0`                                      | sparse          |
| All 4 targets cleared                                       | `+10.0`                                     | sparse          |
| Shot fired that did not hit a `Target`-tagged collider      | `-0.05`                                     | sparse          |
| Per 10 game-seconds without a hit                           | `-0.5`                                      | sparse          |
| Per agent step                                              | `-0.001`                                    | sparse          |
| Timeout reached without success                             | `-1.0`                                      | sparse          |
| `cos(angle to nearest unhit target)`                        | `+0.05 × cos(θ)` per step, annealed to 0    | shaped          |
| Decrease in distance to nearest unhit target (`-Δd`)        | `+0.01 × Δd` per step, annealed to 0        | shaped          |

### Definitions

- **"Wasted shot"** = a `Weapon.Fire()` call whose camera raycast did *not*
  hit a collider with tag `"Target"`. Implementation reads the raycast
  result already computed in `Weapon.Fire` (Weapon.cs:230) and surfaces it
  to the Agent via an event.
- **"Without a hit"** counter: a `float secondsSinceLastHit` accumulator on
  the Agent, reset on every `+1.0` hit reward. Penalty fires every 10s.
- **Shaped reward annealing**: linear decay from full multiplier at step 0
  to 0 at step 100,000 (per agent). Implemented as a coefficient computed
  from `Academy.Instance.StepCount`.

## Consequences

- The shaped rewards (`cos(θ)`, `Δd`) are scaffolding — they **must** be
  annealed to zero, otherwise the trained policy will optimize for "be
  close to and facing targets" instead of "hit them." The decay must be
  baked into training and removed entirely at inference.
- The wasted-shot penalty (`-0.05`) is small relative to a hit (`+1.0`).
  This is intentional: we don't want the agent to refuse to fire on
  partially-occluded targets because the expected value is borderline.
- Together, the maximum theoretical episode return is approximately
  `4 × 1.0 + 10.0 = +14.0` minus a tiny step cost. Minimum (timeout
  failure) is around `-1.0 - (60 / 10) × 0.5 - 60s × step_cost ≈ -4.0`.
  TensorBoard mean episode reward should asymptote near +13 if learning
  works.
- Stagnation penalty (`-0.5 / 10s`) means a fully-idle 60s episode
  accumulates `-3.0` before the timeout penalty — this is more punitive
  than the timeout itself, which is intentional (idleness should be the
  worst behavior).

## Considered alternatives

- **Pure sparse rewards (no shaping).** Higher risk of the policy
  collapsing to "don't fire." Rejected; shaping is annealed so the final
  policy is unaffected.
- **Curriculum learning instead of shaping.** Valid alternative. Defer
  unless shaping proves insufficient.
- **Per-bullet penalty for *all* shots** (not just misses). Rejected: would
  also penalize legitimate hits and discourage firing in general.
