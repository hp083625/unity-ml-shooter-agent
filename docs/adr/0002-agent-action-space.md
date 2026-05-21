# ADR-0002: Agent action space includes movement, not just aim

- **Status:** Accepted
- **Date:** 2026-05-21
- **Supersedes:** —

## Context

After deciding on RL + ML-Agents (ADR-0001), the next branch was scope of the
action space:

- A — aim only (`yaw_delta`, `pitch_delta`, `fire`)
- B — aim + body rotation (same actions; body yaw rides on camera yaw)
- C — aim + walking (`move_x`, `move_y` added)
- D — aim + walking + reload + weapon switch

Recommendation was B for fastest time-to-demo. User chose C explicitly.

## Decision

Action space is **5-dimensional**:

| Action       | Type        | Range  | Driven via                                      |
|--------------|-------------|--------|--------------------------------------------------|
| `yaw_delta`  | continuous  | [-1,1] | `Character.axisLook.x` (CameraLook reads it)    |
| `pitch_delta`| continuous  | [-1,1] | `Character.axisLook.y`                          |
| `move_x`     | continuous  | [-1,1] | `Character.axisMovement.x` (Movement reads it)  |
| `move_y`     | continuous  | [-1,1] | `Character.axisMovement.y`                      |
| `fire`       | discrete    | {0,1}  | `Character.holdingButtonFire`                   |

The Agent does **not** reload, holster, inspect, change weapon, jump, or run
in this revision — those are deferred. Ammo will be kept high enough that
reload is never required during an episode.

## Consequences

- **Navigation becomes a real problem.** The scene has multiple rooms; the
  agent may need to learn to walk to a position with line-of-sight to a
  target. This pushes training from minutes to hours and requires careful
  reward shaping or curriculum learning.
- **Observation space grows.** We need terrain awareness (wall raycasts /
  ray-perception sensor) on top of target awareness.
- **Reset complexity grows.** Episode reset must re-pose the agent to a
  known spawn (not just un-hit the targets), or randomize spawn for
  generalization.
- **Risk: collapse to "spin in place."** Early training, the policy may
  discover that yaw + fire alone clears the targets visible from spawn and
  never learns to walk. Mitigations: place targets that require movement,
  reward shaping for distance-to-nearest-unhit-target, or curriculum
  starting with movement-required scenarios.

## Considered alternatives

- **B (aim + body rotation only).** Faster time-to-demo (~3-4 days vs ~2
  weeks). Would have produced a "turret" agent. Rejected: user wants a bot
  that "plays the game" visibly.
- **D (full game).** Adds reload/holster/weapon-switch on top of C. Rejected:
  no interesting policy emerges from those — they are wait-for-animation
  buttons. Can be revisited later with no architectural change.
