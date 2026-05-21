# Project Context — ML Shooter Agent

This document captures domain language and design decisions for the ML-trained
shooter agent built on top of the Infima Games Low Poly Shooter Pack.

- For the existing Unity project conventions, see [CLAUDE.md](./CLAUDE.md).
- For architectural decisions, see [docs/adr/](./docs/adr/).
- For the **end-to-end build plan**, see [SCOPE.md](./SCOPE.md).

## Glossary

- **Agent** — the ML-driven actor that replaces the human player. A Unity
  `MonoBehaviour` (subclass of ML-Agents `Agent`) that observes the scene,
  chooses actions, and receives rewards. Distinct from the Unity term
  "GameObject" and from the Infima term "Character".
- **Character** — the existing first-person player rig from the Low Poly
  Shooter Pack (`InfimaGames.LowPolyShooterPack.Character`). The Agent
  drives the Character; it does not replace it.
- **Target** — a GameObject tagged `"Target"` carrying a `TargetScript`
  component. Hit detection flips `TargetScript.isHit = true`.
- **Episode** — one training trial. Begins with a reset (all targets un-hit,
  agent re-spawned), ends when a terminal condition is met (all targets hit,
  timeout, or out-of-bounds).
- **Wave** — a single set of targets to clear within one episode. (Currently
  one wave per episode; "wave" and "episode" are 1:1 for now.)
- **Heuristic policy** — the deterministic baseline (aim-at-nearest-unhit-target
  + fire) used for sanity checks and to compare against the trained policy.
- **Bridge** — the C# adapter (`AIControllerBridge`) that writes synthetic
  `axisLook`/`holdingButtonFire` values into the Character without going
  through Unity's Input System.
- **TrainingArea** — a prefab containing one Player rig + N targets +
  bounding walls + an `AreaServiceLocator`. Eight copies are placed in
  the training scene (`S_Training.unity`); the demo scene uses zero copies
  and falls through to the global service locator.
- **AreaServiceLocator** — a MonoBehaviour at the root of a `TrainingArea`
  that owns a private `ServiceLocator` and registers the area's
  `IGameModeService`. Looked up via `ServiceLocator.For(this)`; falls
  through to `ServiceLocator.Current` if no `AreaServiceLocator` is found
  on the parent chain.

## Decisions

See ADRs:
- [ADR-0001: Use ML-Agents + PPO for the shooter agent](./docs/adr/0001-rl-with-ml-agents.md)
- [ADR-0002: Agent action space includes movement, not just aim](./docs/adr/0002-agent-action-space.md)
- [ADR-0003: Episode termination and reward shape](./docs/adr/0003-reward-and-episode.md)
- [ADR-0004: Train in a separate scene with parallel training areas](./docs/adr/0004-parallel-training-scene.md)
- [ADR-0005: Agent drives Character via minimal public setters](./docs/adr/0005-agent-drives-character-via-setters.md)
- [ADR-0006: Scoped service locator (per-area, with global fallback)](./docs/adr/0006-scoped-service-locator.md)
- [ADR-0007: Upgrade project to Unity 6 + ML-Agents Release 23](./docs/adr/0007-upgrade-to-unity-6.md)

## Operational notes (reversible, not ADR-worthy)

- **Training host:** local Mac, native Python venv, CPU. Escalate to cloud
  GPU only if Phase-3 training exceeds a few hours.
- **Pinned versions** (per [ADR-0007](./docs/adr/0007-upgrade-to-unity-6.md)):
  Unity 6 LTS (6000.0.x), `com.unity.ml-agents` 4.0.0, `mlagents` Python 1.1.0,
  Python 3.10.12, PyTorch ~=2.2.1, Inference Engine 2.2.1 (bundled with
  ML-Agents).
- **Observation design:** **three** `RayPerceptionSensorComponent3D`
  components on the Agent (one per vertical layer) — each configured with
  `RaysPerDirection: 8` (= 17 horizontal rays per layer), different
  `StartVerticalOffset` / `EndVerticalOffset` to span pitch ±~15°,
  `RayLength: 50`, `DetectableTags: ["Target","Wall","ExplosiveBarrel"]`,
  `UseBatchedRaycasts: true` (Job System). **51 rays total, 3 sensors,
  ~255 ray-observation floats.** *Note:* `ObservationStacks` is a
  *temporal* stacking parameter (memory of past frames) — unrelated to
  vertical layers; we leave it at 1. Plus ~6 scalar floats: agent velocity
  (xz, 2), yaw+pitch encoded as sin/cos (4), ammo fraction (1),
  targets-remaining fraction (1). Camera pixels are *not* used.
- **"AI vision" debug overlay:** small in-game HUD `RawImage` (17×3 pixels,
  point-filtered, scaled up) that paints what the rays hit. Hue = tag (red
  Target, gray Wall, orange ExplosiveBarrel), brightness = closeness, black
  = no hit. Reads from **all three** sensor components (one row per
  component); not a separate observation.
- **Reward signals (in PPO config YAML):** `extrinsic` (strength 1.0,
  gamma 0.99) for the hand-designed reward in ADR-0003 + `curiosity`
  intrinsic signal (strength ~0.02, gamma 0.99) for exploration in the
  sparse-reward early phase.
- **Decision period:** `DecisionRequester` every 5 FixedUpdate steps
  (= 0.1s @ 50Hz physics). No action masking (ammo is always full, so
  the fire action is never invalid).
- **Validation strategy:** the agent's `Heuristic(...)` override
  ("aim at nearest unhit target, walk toward it, fire") is implemented
  *first*, before any training. It must reliably clear all 4 targets
  via `mlagents-learn --run-heuristic` (or Unity's Heuristic-Only
  Behavior Type) before PPO is touched — this proves the environment,
  reward wiring, and bridge end-to-end. The heuristic is also the
  baseline that PPO must beat to count as "trained." A single PlayMode
  test in `Assets/Tests/PlayMode/AgentSmokeTest.cs` loads `S_Training`,
  runs all 8 areas in heuristic mode for 60 game-seconds, and asserts
  ≥1 area cleared. EditMode tests are skipped for now.
