# ADR-0001: Use ML-Agents + PPO for the shooter agent

- **Status:** Accepted
- **Date:** 2026-05-21

## Context

The user wants an "ML-trained" agent that aims at and shoots the targets in
the demo scene `S_Content_Overview.unity`. Several approaches were on the
table (see "Considered alternatives" below). The gameplay loop in the Free
Sample is mechanically simple — a deterministic heuristic ("look at nearest
target, fire") would clear all stationary targets — so we are not adopting RL
because the problem demands it. We are adopting it because the user wants an
artifact that visibly learns.

## Decision

Train the policy with **Unity ML-Agents (PPO, Python-side)** and run inference
in-engine with **Unity Sentis** (loading an exported `.onnx`). The Agent
class observes the scene, emits continuous yaw/pitch/fire actions, and is
rewarded for hitting targets.

## Considered alternatives

- **Imitation learning from heuristic demonstrations.** Cheaper but produces
  a model that cannot exceed the demonstrator. Rejected: the point is to
  produce a *trained* policy, not a *cloned* one.
- **Heuristic with an ML perception layer (CNN over camera frames).** Closer
  to how real game-AI uses ML in 2026, and more interesting technically.
  Rejected for now: harder to evaluate, harder to debug, and the user wants
  the "agent learns" framing rather than the "perception module" framing.
- **No ML, classical bot.** Rejected: does not match the user's stated goal.

## Consequences

- New package dependencies: `com.unity.ml-agents` (training+inference shim)
  and `com.unity.sentis` (production inference). Note: ML-Agents historically
  shipped its own inference path; we will use Sentis if the project's
  ML-Agents version supports it, otherwise fall back to the bundled inference.
- A Python training environment is required (PyTorch + `mlagents` package).
  Training happens outside Unity; inference runs inside Unity from `.onnx`.
- The trivial-optimum problem (a 20-line if-statement matches the optimal
  policy on stationary targets) means we will need to add difficulty during
  training (occluders, moving targets, noise on observations) for the learned
  policy to look meaningfully different from the heuristic. This is tracked
  separately and decided per phase.
