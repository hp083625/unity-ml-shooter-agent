# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Unity **2022.3.58f1** project built around the Infima Games **Low Poly Shooter Pack — Free Sample** (a first-person shooter starter). All gameplay code lives under `Assets/Infima Games/` and shares the namespace `InfimaGames.LowPolyShooterPack`. There is no custom assembly definition — everything compiles into `Assembly-CSharp`.

Two scenes ship in the repo:
- `Assets/Scenes/SampleScene.unity` — Unity's default empty scene.
- `Assets/Infima Games/Low Poly Shooter Pack - Free Sample/Scenes/S_Content_Overview.unity` — the playable demo. Use this when running the game.

## Working With Unity

The Editor is the source of truth. **Prefer the `UnityMCP` MCP tools over guessing or shelling out.** A quick map:

- Reading state — use the `mcpforunity://...` resources (e.g. `editor_state`, `project_info`, `scene/...`, `console`). Always read these *before* mutating, and check `editor_state.advice.ready_for_tools` / `isCompiling` after script edits.
- Performing actions — `manage_scene`, `manage_gameobject`, `manage_components`, `manage_prefabs`, `manage_asset`, `manage_editor` (play/pause/stop, tags, layers), `manage_build`.
- Running tests — `run_tests` (returns a `job_id`) → `get_test_job` to poll. EditMode and PlayMode supported via `com.unity.test-framework` 1.1.33.
- Compilation feedback — after creating or editing a `.cs` file with `manage_script` / `apply_text_edits`, call `read_console` to surface compile errors. New components only become usable once compilation succeeds.

Headless builds (when needed outside the MCP): `manage_build action=build target=...`. There is no makefile or CI config in the repo.

Asset paths in MCP calls are relative to `Assets/` and use forward slashes regardless of OS.

## Architecture

### Bootstrap and service locator

`Code/Services/Bootstraper.cs` is a static class with `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]` — it runs before any scene loads, creates `ServiceLocator.Current`, and registers:

- `IGameModeService` (plain object) — exposes `GetPlayerCharacter()`, which lazily `FindObjectOfType<CharacterBehaviour>()`s the player on first call.
- `IAudioManagerService` — instantiated as a new GameObject named "Sound Manager" with `DontDestroyOnLoad` so it persists across scenes.

Anything that needs services pulls them via `ServiceLocator.Current.Get<T>()`. Services must implement `IGameService` (empty marker interface) and are keyed by their interface's `typeof(T).Name`. Registering the same key twice logs an error and keeps the original — order matters.

### Behaviour/Implementation split

The codebase consistently pairs an abstract `XxxBehaviour : MonoBehaviour` with a concrete `Xxx : XxxBehaviour`:

- `CharacterBehaviour` ↔ `Character`
- `WeaponBehaviour` ↔ `Weapon`
- `MovementBehaviour` ↔ `Movement`
- `InventoryBehaviour` ↔ `Inventory`
- `MagazineBehaviour` ↔ `Magazine`, `MuzzleBehaviour` ↔ `Muzzle`, `ScopeBehaviour` ↔ `Scope`
- `WeaponAttachmentManagerBehaviour` ↔ `WeaponAttachmentManager`

When extending the asset, **subclass the concrete `Xxx` class or write a new sibling of `XxxBehaviour`** rather than editing the base — other systems (animation events, the service locator lookup, inspector references on prefabs) reference the abstract type.

### Character is the hub

`Code/Character/Character.cs` is the central coordinator. It:

- Holds the input callbacks (`OnTryFire`, `OnTryAiming`, `OnTryRun`, `OnTryHolster`, `OnTryInventoryNext`, `OnLockCursor`, `OnMove`, `OnLook`, `OnTryPlayReload`, `OnTryInspect`, `OnUpdateTutorial`) wired to the `PlayerInput` component on the prefab. The Input System asset is `Assets/Infima Games/Low Poly Shooter Pack - Free Sample/Input/IA_Player.inputactions`.
- Owns the `CharacterKinematics` reference and calls `Compute()` from `LateUpdate` — IK runs after animation but before render.
- Drives the Animator via three layers cached by name on `Start`: `"Layer Holster"`, `"Layer Actions"`, `"Layer Overlay"`. Animator parameters are hashed (`"Aiming"`, `"Movement"`, `"Aim"`, `"Running"`, `"Holstered"`).
- Implements `CanFire`/`CanAim`/`CanRun`/`CanChangeWeapon`/etc. — every action goes through these gates, so add new gating there rather than at call sites.

### Animation event routing

Animator events fire on the model's `CharacterAnimationEventHandler` MonoBehaviour, which on `Awake` resolves the player via `ServiceLocator.Current.Get<IGameModeService>().GetPlayerCharacter()` and forwards each event to the abstract methods on `CharacterBehaviour` (`EjectCasing`, `FillAmmunition`, `SetActiveMagazine`, `AnimationEndedReload`, `AnimationEndedInspect`, `AnimationEndedHolster`). If you add a new animation event, add an abstract method on `CharacterBehaviour`, override it in `Character`, and call it from the handler — don't reach into the Character directly.

### Logging

Use the project's `Log` helper (`Assets/Infima Games/Tools/Log.cs`) rather than `Debug.Log` for consistency with existing code:

- `Log.wtf(...)` → info, `Log.warn_me(...)` → warning, `Log.kill(...)` → error, `Log.oopsie(ex)` → exception.

## Key packages

From `Packages/manifest.json`:

- `com.unity.inputsystem` 1.11.2 — the new Input System is the only input pipeline; the legacy `Input.GetKey` API is not used.
- `com.unity.postprocessing` 3.4.0 — built-in render pipeline + PPv2, **not** URP/HDRP.
- `com.unity.test-framework` 1.1.33 — there are currently no test assemblies in `Assets/`; if you add one, place it under a folder with an `.asmdef` referencing `nunit.framework`.
- `com.coplaydev.unity-mcp` — provides the `UnityMCP` tooling described above.
