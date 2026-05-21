# ADR-0004: Train in a separate scene with parallel training areas

- **Status:** Accepted
- **Date:** 2026-05-21

## Context

ML-Agents trains substantially faster when many copies of the environment
share a single PPO trainer. Each copy is a self-contained "training area"
prefab; the trainer aggregates experience from all copies in a single
gradient update. This is the canonical pattern in every official ML-Agents
example.

The demo scene `S_Content_Overview.unity` is a hand-crafted showroom layout
with multiple rooms, decorative props, and one player spawn — unsuitable as
a training environment because (a) only one agent can be hosted, and (b)
its layout is a fixed scenario rather than a randomizable training
distribution.

## Decision

Training and inference happen in **separate scenes**:

- **`Assets/Scenes/Training/S_Training.unity`** — new scene built for
  training. Contains **8 instances of a `TrainingArea` prefab**, spread out
  on a flat plane, far enough apart that one agent's projectiles cannot
  reach another's targets. Each prefab contains:
  - Player rig (`P_LPSP_FP_CH` prefab instance)
  - 4 targets (`P_LPSP_DMG_Target` instances), positions randomized on
    `Reset()`
  - Wall colliders bounding the area (so the agent can't walk into other
    areas)
  - One `AgentShooter` MonoBehaviour, all 8 sharing the same Behavior Name
    (e.g. `"Shooter"`)

- **`Assets/Infima Games/Low Poly Shooter Pack - Free Sample/Scenes/S_Content_Overview.unity`**
  — unmodified showroom. The trained `.onnx` model is plugged into a single
  `AgentShooter` here for the demo.

The `TrainingArea` prefab is the unit of randomization: spawn pose, target
positions, and (later) walls/occluders are all randomized per-episode at
the prefab level.

## Consequences

- **~6–8× wall-clock training speedup** for the same number of policy
  updates, vs. a single-area scene.
- **Two scenes to maintain.** Training scene must be kept in sync with
  inference scene's *physics* and *layer* setup but can diverge in layout.
  Both share the same `TagManager.asset` and the same `P_LPSP_FP_CH` /
  `P_LPSP_DMG_Target` prefabs, so changes propagate correctly.
- **Bootstrapper note.** `Code/Services/Bootstraper.cs` registers a single
  `IGameModeService` whose `GetPlayerCharacter()` is `FindObjectOfType<...>()`
  — this returns *one* arbitrary character, breaking when there are 8.
  The Agent and any code it calls must **not** rely on
  `GetPlayerCharacter()`; instead, the Agent holds a direct reference to
  *its own* `Character` component on the same `TrainingArea` instance.
  This is a real architectural constraint the implementation must respect.
- **Projectile cross-contamination.** Projectiles are physical Rigidbodies.
  If two areas are close, a stray bullet from area 1 could enter area 2 and
  hit a target there, awarding reward to the wrong agent. Mitigation:
  large inter-area spacing (>50m) plus invisible-wall colliders on the
  `Invisible Wall` layer (which already exists in the project per
  TagManager.asset:34).
- **Build settings.** `S_Training.unity` is added to Build Settings during
  development; can be excluded from the final shipped build.

## Considered alternatives

- **Train in `S_Content_Overview` directly with one area.** Rejected:
  6–8× slower training; layout is wrong for training distribution.
- **Train in `S_Content_Overview` with the prefab duplicated 8 times into
  the existing rooms.** Rejected: rooms are too small/irregular for clean
  parallel arenas, and modifying the showroom risks breaking it as a demo.
