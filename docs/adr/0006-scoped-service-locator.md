# ADR-0006: Scoped service locator (per-area, with global fallback)

- **Status:** Accepted
- **Date:** 2026-05-21

## Context

The Infima Games Low Poly Shooter Pack uses a single global service locator
(`ServiceLocator.Current`) registered at `RuntimeInitializeOnLoadMethod`
time by `Code/Services/Bootstraper.cs`. The locator returns one
`IGameModeService` whose `GetPlayerCharacter()` is implemented as
`FindObjectOfType<CharacterBehaviour>()`.

Four scripts in the asset cache the "player character" via this service
during their `Awake()` and rely on the cached reference for all subsequent
behavior:

| Script                                     | Where the lookup happens          |
|--------------------------------------------|-----------------------------------|
| `Code/Weapons/Weapon.cs`                   | line 145                          |
| `Code/Character/Movement.cs`               | line 90                           |
| `Code/Camera/CameraLook.cs`                | line 61                           |
| `Code/Animation/CharacterAnimationEventHandler.cs` | (Awake)                  |

ADR-0004 introduces a parallel training scene `S_Training.unity` containing
**8 instances of a `TrainingArea` prefab**, each with its own player rig.
With 8 `CharacterBehaviour` components in the scene,
`FindObjectOfType<CharacterBehaviour>()` returns one arbitrary instance —
all 8 movement/camera/weapon scripts then read input, fire from, and
rotate the *same* Character. Training silently produces a useless policy
because eight agents share one body.

A simple workaround (replace each `GetPlayerCharacter()` call with
`GetComponentInParent<CharacterBehaviour>()`) was rejected because it
leaves the underlying service broken for any future code that asks for
"the player." The right fix is to give the locator a notion of scope.

## Decision

Introduce a **per-area service locator** that lives at the root of each
`TrainingArea` prefab. The existing global locator is kept as a fallback
for non-training contexts (e.g., `S_Content_Overview`).

### API shape

```csharp
public class ServiceLocator
{
    public static ServiceLocator Current { get; }       // existing global
    public static ServiceLocator For(Component c);      // NEW
    // walks c.transform up the hierarchy looking for an
    // AreaServiceLocator. If found, returns its locator;
    // otherwise returns Current.
}

// NEW MonoBehaviour, lives at the root of TrainingArea prefab.
public class AreaServiceLocator : MonoBehaviour
{
    public ServiceLocator Locator { get; private set; }

    private void Awake()
    {
        Locator = new ServiceLocator();
        // Register area-scoped Character into a fresh GameModeService:
        var ch = GetComponentInChildren<CharacterBehaviour>();
        Locator.Set<IGameModeService>(new GameModeService(ch));
    }
}
```

### Call-site change

The four scripts listed above each change exactly one identifier:

```diff
- gameModeService = ServiceLocator.Current.Get<IGameModeService>();
+ gameModeService = ServiceLocator.For(this).Get<IGameModeService>();
```

`ServiceLocator.For(this)`:
- Walks up the transform chain looking for an `AreaServiceLocator`.
- If found, returns its locator (area-scoped).
- If not found, returns `Current` (the existing global) — preserving
  exact existing behavior in `S_Content_Overview` and any other scene
  with no `AreaServiceLocator` ancestor.

### `GameModeService` change

`GameModeService` gains a constructor taking the area's
`CharacterBehaviour` directly, bypassing `FindObjectOfType`. The
parameterless constructor (and its `FindObjectOfType` lookup) is kept for
the global instance.

## Consequences

- **`S_Content_Overview` is not modified and behaves identically.** No
  `AreaServiceLocator` in that scene → all `For(this)` calls fall through
  to `Current` → existing behavior preserved.
- **Training scene works correctly.** Each of the 8 `TrainingArea`
  prefabs has its own `AreaServiceLocator` registering its own
  `IGameModeService` pointing at its own `Character`. Movement reads its
  own input, CameraLook rotates its own player, Weapon traces from its
  own camera.
- **The pattern is reusable.** Any future per-area service (scoring,
  target tracker, audio, etc.) gets the same scoping for free — register
  it on the area locator instead of the global one.
- **Bootstrapper still owns the global.**
  `IAudioManagerService` is genuinely global (`DontDestroyOnLoad`) and
  stays on `Current`. `IGameModeService` is registered on `Current`
  too, as a single-Character fallback for non-training scenes.
- **Diff size:** ~80 lines new (`AreaServiceLocator`,
  `ServiceLocator.For`, `GameModeService` constructor overload), 4
  single-line edits to existing scripts.
- **Risk:** if the asset is updated upstream by Infima, the four call-site
  edits are merge points. Acceptable; this is a free Unity asset, not a
  vendored library tracking upstream.

## Considered alternatives

- **A — `GetComponentInParent<CharacterBehaviour>()` at the four call
  sites.** Same diff size, but a workaround. The service locator stays
  broken for any future caller. Rejected because user explicitly preferred
  the proper fix over the smaller diff.
- **B — Sibling injection ("WireUp" component sets references).** Adds
  public setters to `Weapon` / `Movement` / `CameraLook`. More boilerplate
  for no architectural gain over A. Rejected.
- **C2 — Single global `GameModeService` with a per-call `this` argument
  (`gameModeService.GetPlayerCharacter(this)`).** Forces every call site
  to thread context, including future ones. Uglier API; same blast radius.
  Rejected in favor of C1.
- **D — Skip parallel training (reverse ADR-0004).** Accepts 6–8× longer
  training. Rejected; ADR-0004 stands.
