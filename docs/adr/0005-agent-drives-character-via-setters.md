# ADR-0005: Agent drives Character via minimal public setters

- **Status:** Accepted
- **Date:** 2026-05-21

## Context

The Agent (ADR-0001) needs to send synthetic look/move/fire input into the
existing `Character` component without going through the Input System. Four
patterns were on the table: reflection writes to private fields, public
setters on `Character`, fake Input System events, or a sibling bridge that
calls `Character.OnLook(...)` with synthesized `InputAction.CallbackContext`
values.

Constructing `InputAction.CallbackContext` from outside the Input System is
genuinely hard (it's a heavyweight struct internally tied to the action
state machine), and faking devices doesn't isolate per-instance — both
options fight the parallel training architecture from ADR-0004.

The project's CLAUDE.md documents a Behaviour/Implementation split:
abstract `XxxBehaviour : MonoBehaviour` paired with concrete `Xxx :
XxxBehaviour`. New API on the abstract side keeps animation events,
service locator lookups, and prefab inspector references working.

## Decision

Add three public setters to the existing `CharacterBehaviour` /
`Character` pair, plus an AI-input toggle:

```csharp
// CharacterBehaviour.cs (abstract, new methods)
public abstract void SetAxisLook(Vector2 value);
public abstract void SetAxisMovement(Vector2 value);
public abstract void SetHoldingFire(bool held);

// Character.cs (overrides — write to existing private fields)
public override void SetAxisLook(Vector2 value)     => axisLook = value;
public override void SetAxisMovement(Vector2 value) => axisMovement = value;
public override void SetHoldingFire(bool held)      => holdingButtonFire = held;
```

A `[SerializeField] private bool useAIInput = false` field on `Character`
gates the existing input callbacks (`OnLook`, `OnMove`, `OnTryFire`,
`OnTryAiming`, `OnTryRun`, `OnTryPlayReload`, `OnTryInspect`,
`OnTryHolster`, `OnTryInventoryNext`). When `useAIInput == true`, those
callbacks **early-return** without modifying any state. `OnLockCursor` is
*not* gated — escape-to-unlock-cursor remains useful in development.

The Agent owns a sibling `AIControllerBridge` MonoBehaviour on the same
GameObject. The bridge:
- Holds a reference to its sibling `Character` (no service locator).
- Per ML-Agents action received: scales `yaw_delta`/`pitch_delta` by a
  serialized `lookSpeedScale` (default 5.0, units = degrees/decision-step)
  and calls `SetAxisLook(scaled)`. Passes `move_x`/`move_y` straight to
  `SetAxisMovement` (Movement.cs:162–172 multiplies by `speedWalking=5.0`
  internally; no extra scaling needed). Calls `SetHoldingFire(fire == 1)`.

## Consequences

- **~15 lines added across `Character.cs` and `CharacterBehaviour.cs`.**
  No existing code is modified, only extended. The Behaviour/Implementation
  split is preserved.
- **Human and AI cannot drive the same Character simultaneously.** The
  `useAIInput` toggle is a hard switch. Acceptable: in training, only the
  AI plays; in the demo scene, the AI plays its dedicated Character while
  any human input is moot.
- **Scaling is in one place** (the bridge's `lookSpeedScale` field).
  Tuning aim speed during training is a single inspector tweak.
- **Cursor lock UX is preserved** by exempting `OnLockCursor` from the
  gate. Development affordance.

## Considered alternatives

- **Reflection writes to private fields.** Fragile; breaks if Infima
  renames a field in a future asset update. Rejected.
- **Fake Input System events via `QueueDeltaStateEvent`.** Doesn't isolate
  per-Character — one virtual mouse drives one PlayerInput, not per-instance.
  Breaks ADR-0004's parallel training. Rejected.
- **Sibling bridge calling `Character.OnLook(ctx)` with synthesized
  `InputAction.CallbackContext`.** That struct is hostile to construct
  from outside the Input System. Rejected.
