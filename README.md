# Unity ML Shooter Agent

Design and architecture for a reinforcement-learning agent that walks, aims,
and shoots all the targets in the demo scene of the **Infima Games Low Poly
Shooter Pack — Free Sample** (Unity 2022.3.58f1).

> **This repository currently contains design documents only.**
> Implementation lives in a separate Unity project on disk; see
> [SCOPE.md](./SCOPE.md) for the full build plan.

---

## What this is

The goal is an `.onnx` policy trained with **Unity ML-Agents (PPO)** and run
in-engine with **Unity Sentis**. When loaded into a single agent in the
demo scene, the bot walks around and clears all 4 `TargetScript`-tagged
targets within 60 game-seconds, with a small "AI vision" HUD that
visualises what its raycasts perceive.

| Capability | Choice |
|---|---|
| Training algorithm | PPO via `mlagents-learn` (Python) |
| Inference runtime | Unity Sentis (`.onnx`) |
| Action space | 5-dim: `yaw`, `pitch`, `moveX`, `moveY` continuous + `fire` discrete |
| Observation space | `RayPerceptionSensor3D` (51 rays as a 17×3 grid, tagged) + 8 scalar floats |
| Episode | Success = all 4 targets hit, or 60s timeout |
| Reward | per-hit, clear-bonus, miss penalty, stagnation penalty + annealed shaping |
| Training scene | 8 parallel `TrainingArea` prefabs in a separate scene |
| Inference scene | The original Infima demo `S_Content_Overview` |

For the *why* behind each of these, see the ADRs.

---

## Repository layout

```
.
├── README.md                 ← you are here
├── CLAUDE.md                 ← project conventions for the underlying Unity project
├── CONTEXT.md                ← glossary + operational notes + ADR index
├── SCOPE.md                  ← end-to-end build plan (7 phases, files, gates, risks)
└── docs/
    └── adr/
        ├── 0001-rl-with-ml-agents.md
        ├── 0002-agent-action-space.md
        ├── 0003-reward-and-episode.md
        ├── 0004-parallel-training-scene.md
        ├── 0005-agent-drives-character-via-setters.md
        └── 0006-scoped-service-locator.md
```

---

## The plan in one page

Build order, with a hard validation gate at each boundary. See
[SCOPE.md](./SCOPE.md) for the full version.

| # | Phase | Deliverable | Gate |
|---|---|---|---|
| 1 | **Scoped service locator** ([ADR-0006](./docs/adr/0006-scoped-service-locator.md)) | `AreaServiceLocator`, `ServiceLocator.For()`, 4 call-site edits | Demo scene plays unchanged |
| 2 | **Character input setters** ([ADR-0005](./docs/adr/0005-agent-drives-character-via-setters.md)) | 4 abstract setters + `useAIInput` toggle | Demo scene plays unchanged |
| 3 | **`AIControllerBridge` MonoBehaviour** | Sibling component, scaling, ContextMenu test | Manual test moves the player |
| 4 | **`TrainingArea` prefab + `TargetWaveManager`** | Self-contained arena: player + 4 targets + walls + spawn points | One area in isolation: reset works, ammo refills |
| 5 | **`AgentShooter` + Heuristic + smoke test** ([ADR-0003](./docs/adr/0003-reward-and-episode.md)) | Agent class + deterministic baseline + PlayMode test | **Heuristic clears ≥1 area in 60s. Stop here if it doesn't.** |
| 6 | **Training scene + PPO + train** ([ADR-0001](./docs/adr/0001-rl-with-ml-agents.md), [ADR-0004](./docs/adr/0004-parallel-training-scene.md)) | `S_Training.unity` (8 areas), `shooter.yaml`, train | TensorBoard mean reward > 12 |
| 7 | **Deploy to demo scene** | `.onnx` plugged into the demo, AI vision overlay | Agent visibly clears targets in `S_Content_Overview` |

Estimated calendar effort: **~4–5 days** for a single developer.

---

## Decisions at a glance

- **[ADR-0001](./docs/adr/0001-rl-with-ml-agents.md)** — RL + ML-Agents + Sentis (not imitation, not heuristic-only).
- **[ADR-0002](./docs/adr/0002-agent-action-space.md)** — Action space includes movement, not just aim.
- **[ADR-0003](./docs/adr/0003-reward-and-episode.md)** — 60s episodes; sparse reward + annealed shaping for early-training credit assignment.
- **[ADR-0004](./docs/adr/0004-parallel-training-scene.md)** — Train in a dedicated scene with 8 parallel arenas; deploy to the demo scene.
- **[ADR-0005](./docs/adr/0005-agent-drives-character-via-setters.md)** — Agent drives the existing `Character` via minimal public setters, not synthetic Input System events.
- **[ADR-0006](./docs/adr/0006-scoped-service-locator.md)** — Service locator becomes scope-aware so 8 parallel agents don't share one body.

---

## Stack

- Unity **2022.3.58f1** (LTS), built-in render pipeline + PPv2.
- Infima Games **Low Poly Shooter Pack — Free Sample** (asset, not redistributed in this repo).
- `com.unity.ml-agents` (latest 2.x), `com.unity.sentis` (latest 2.x), `com.unity.inputsystem` 1.11.2.
- Python 3.10 venv with `mlagents` package for training.

---

## Out of scope

- Reload / holster / inspect / weapon switch / jump (agent never invokes these).
- Multi-weapon training, moving targets, multi-agent combat.
- Visual observations (camera pixels). Observations are RayPerception + scalars only.
- Imitation learning / GAIL / behavioural cloning.
- Cloud-GPU training; local CPU only.

---

## Status

Design complete. No implementation has started. The repository will be
expanded with code (and a proper Unity `.gitignore`) once Phase 1 begins.
