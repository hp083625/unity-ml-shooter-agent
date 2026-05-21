# Scope — ML Shooter Agent

End-to-end work breakdown for an ML-trained agent that walks, aims, and
shoots all 4 targets in `S_Content_Overview.unity`. Decisions live in the
ADRs; this document is **what to build**.

- See [CONTEXT.md](./CONTEXT.md) for the glossary
- See [docs/adr/](./docs/adr/) for the *why* behind each decision
- Authoritative architectural notes for the existing codebase: [CLAUDE.md](./CLAUDE.md)

---

## Final goal

A `.onnx` model trained via Unity ML-Agents PPO that, when loaded into a
single `AgentShooter` MonoBehaviour in `S_Content_Overview`, walks around
the spawn area and clears all 4 `TargetScript`-tagged targets within 60
game-seconds, with a small "AI vision" HUD overlay showing what the
agent's raycasts perceive.

---

## Phase 0 — Unity 6 upgrade (ADR-0007)

**Open the project in Unity 6 LTS** (latest 6000.0.x). Accept the
auto-migration prompt. Expected work:

- Compile errors from API removals (most likely in `com.unity.postprocessing`
  3.4.0 — PPv2 is legacy on U6 but should still compile). Fix or replace
  with Volume Framework if broken.
- Input System asset format may need re-import — open `IA_Player.inputactions`
  in the inspector; let Unity refresh.
- Animator Controllers should migrate cleanly.
- Verify `com.coplaydev.unity-mcp` works on U6 (the live MCP bridge).
  If it doesn't, agent work continues without live scene introspection
  (file-based workflow only).
- Estimated time: **0.5–1 day** of one-time fix-up.

**Validation gate:** open `S_Content_Overview`, press Play, fire weapon,
hit a target. If the original demo still plays, U6 is good. If not, fix
breakage before any agent work.

## Prerequisites (one-time setup)

| # | Item | Notes |
|---|------|-------|
| P1 | Install Unity packages (per ADR-0007): `com.unity.ml-agents` 4.0.0 (Release 23). The Inference Engine 2.2.1 runtime is bundled — no separate Sentis package needed. | Add to `Packages/manifest.json`. Verify compile via `read_console`. |
| P2 | Create Python venv pinned to **Python 3.10.12**. `pip install mlagents==1.1.0`. Verify `mlagents-learn --help` works. | Use `pyenv` for the Python pin. PyTorch will be pulled in as `~=2.2.1`. |
| P3 | Create `Assets/Tests/PlayMode/` with `.asmdef` referencing `nunit.framework`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner` | Per CLAUDE.md, the project currently has no test assemblies. |
| P4 | **ONNX-import smoke test.** Train the official `3DBall` example for ~30s in this venv against a 3DBall scene; import the resulting `.onnx` into the project; confirm the model loads without `NullReferenceException`. | Defensive check: bug #6293 hits Unity 2022.3 only, but verifying on U6 takes 5 minutes and proves the toolchain end-to-end before we invest in the real environment. |
| P5 | Tags `"Wall"` and `"Target"` already exist in `TagManager.asset` (verified). No tag work needed. | — |

---

## Phase 1 — Scoped service locator (ADR-0006)

**New files:**
- `Assets/Infima Games/Low Poly Shooter Pack - Free Sample/Code/Services/AreaServiceLocator.cs`
  - MonoBehaviour at root of `TrainingArea` prefab.
  - **Script Execution Order: `-1000`** (use `[DefaultExecutionOrder(-1000)]`). Required so this `Awake` runs before any of `Weapon.Awake`, `Movement.Awake`, `CameraLook.Awake`, or `CharacterAnimationEventHandler.Awake`, otherwise those four would fall through to `ServiceLocator.Current` and silently bind to the wrong Character in a multi-area scene.
  - On `Awake`: creates a private `ServiceLocator`, calls `GetComponentInChildren<CharacterBehaviour>()` to find the area's Character, registers `new GameModeService(character)` into the private locator.
  - Public read-only property `Locator { get; }` exposes the private locator.

**Modified files:**
- `Code/Services/ServiceLocator.cs` — add `static ServiceLocator For(Component c)` that walks `c.transform.parent` upward looking for an `AreaServiceLocator`; if found, returns its `Locator`; otherwise returns `Current`.
- `Code/Services/GameModeService.cs` — add `GameModeService(CharacterBehaviour ch)` constructor that stores `ch` and returns it directly from `GetPlayerCharacter()` (no `FindObjectOfType`). Keep existing parameterless ctor for the global fallback registered in `Bootstraper.cs`.
- `Code/Services/Bootstraper.cs` — **unchanged.** It still registers the global `IGameModeService` and `IAudioManagerService`. The global `IGameModeService` becomes a fallback for non-training scenes only.
- `Code/Weapons/Weapon.cs:143` — `ServiceLocator.Current` → `ServiceLocator.For(this)`. Lines 145 and 147 (which call `gameModeService.GetPlayerCharacter()` and `characterBehaviour.GetCameraWorld()`) stay as-is.
- `Code/Character/Movement.cs:90` — same `Current` → `For(this)`.
- `Code/Camera/CameraLook.cs:61` — same.
- `Code/Animation/CharacterAnimationEventHandler.cs:26` — same.

**Validation:** Open `S_Content_Overview`, press Play, fire weapon, hit target. Original demo behavior unchanged because no `AreaServiceLocator` exists in that scene → `For(this)` falls through to `Current` (which `Bootstraper` populated). Console must be free of `NullReferenceException` from the four migrated call sites.

---

## Phase 2 — Character input setters (ADR-0005)

**Modified files:**
- `Character/CharacterBehaviour.cs` — add three abstract methods:
  ```csharp
  public abstract void SetAxisLook(Vector2 value);
  public abstract void SetAxisMovement(Vector2 value);
  public abstract void SetHoldingFire(bool held);
  ```
- `Character/Character.cs`
  - Override the three abstract methods; each writes to the existing private field (`axisLook`, `axisMovement`, `holdingButtonFire`).
  - Add `[SerializeField] private bool useAIInput = false`.
  - Gate these existing input callbacks with `if (useAIInput) return;` at the top: `OnLook`, `OnMove`, `OnTryFire`, `OnTryAiming`, `OnTryRun`, `OnTryPlayReload`, `OnTryInspect`, `OnTryHolster`, `OnTryInventoryNext`. **Do not** gate `OnLockCursor`.

**Validation:** `S_Content_Overview` still plays normally with `useAIInput = false`. Toggle it on in the inspector → human input no longer drives the character; ContextMenu test from Phase 3 will exercise the setters.

---

## Phase 3 — AIControllerBridge

**New files:**
- `Assets/Scripts/Agent/AIControllerBridge.cs`
  - MonoBehaviour. Sibling of `Character`. References its sibling via `GetComponent<CharacterBehaviour>()`.
  - Public methods: `SetLookAction(Vector2 yawPitch)`, `SetMoveAction(Vector2 xy)`, `SetFireAction(bool held)`. These scale and forward to the Character's setters.
  - `[SerializeField] private float lookSpeedScale = 5.0f;` (degrees per decision step).
  - `[SerializeField] private bool autoEnableAIInput = true;` — sets `useAIInput = true` on the Character via a serialized reference (need a public setter for this on Character, or use a `[SerializeField] private bool useAIInput` exposed via inspector binding).
  - **Decision:** add `Character.SetUseAIInput(bool)` as a 4th setter rather than reflection.
  - `[ContextMenu("Test: Look up + walk forward + fire")]` method that calls all three setters with hardcoded values for 1 second, then resets. For manual smoke testing.

**Modified files:**
- `Character/CharacterBehaviour.cs` + `Character.cs` — add `SetUseAIInput(bool)` setter.

**Validation:** Drop bridge on player in `S_Content_Overview`. Right-click → "Test: Look up + walk forward + fire" in Play mode. Player visibly looks up, walks forward, fires. Disable bridge → human control restored.

---

## Phase 4 — TrainingArea prefab + TargetWaveManager

**New files:**
- `Assets/Scripts/Agent/TargetWaveManager.cs`
  - On `Awake`: caches all `TargetScript` instances under its transform.
  - `public event Action<TargetScript> TargetHit;` — polled each frame from `TargetScript.isHit` transitions (since `TargetScript` doesn't fire events natively).
  - `public void ResetWave()` — sets every `TargetScript.isHit = false`, plays "up" animation, repositions targets within configured bounds (randomization for training).
  - `public int RemainingCount`, `public int TotalCount`.

- `Assets/Scripts/Agent/AgentArea.cs` *(distinct from AreaServiceLocator — different responsibility)*
  - Holds references to the area's `Character`, `AIControllerBridge`, `TargetWaveManager`, and spawn-point transforms.
  - Provides `ResetArea()` which:
    1. Picks a random `SpawnPoint` transform.
    2. Calls `Rigidbody.position = spawn.position; Rigidbody.velocity = Vector3.zero; Rigidbody.angularVelocity = Vector3.zero;` on the player Rigidbody. **Do not** assign `transform.position` directly — it skips physics and can glitch the FixedUpdate step.
    3. Calls `targetWaveManager.ResetWave()`.
    4. **Refills ammo** via the existing public chain: `character.GetInventory().GetEquipped().FillAmmunition(-1)`. (`-1` is the "fill to magazine total" sentinel per `Weapon.FillAmmunition` semantics.) This avoids exposing `equippedWeapon` outside `Character`.

- `Assets/Prefabs/Agent/TrainingArea.prefab` (new prefab)
  - Hierarchy:
    ```
    TrainingArea (AreaServiceLocator, AgentArea, AgentShooter — Phase 5)
    ├── Player (instance of P_LPSP_FP_CH, with AIControllerBridge added,
    │           useAIInput=true, ammo refilled by ResetArea)
    ├── Targets (TargetWaveManager)
    │   ├── Target_0 (P_LPSP_DMG_Target)
    │   ├── Target_1
    │   ├── Target_2
    │   └── Target_3
    ├── Walls (4 box colliders on "Invisible Wall" layer, bounding the area)
    └── SpawnPoints (4 empty transforms for randomized agent spawn)
    ```
  - **Total area footprint: 20m × 20m.** Wall colliders are 4 thin boxes (`scale (20, 4, 0.5)` etc.) on layer `Invisible Wall` (already in `TagManager.asset`), positioned at `(±10, 2, 0)` and `(0, 2, ±10)` relative to the prefab root.
  - **Spawn points:** 4 empty transforms at `(±6, 0, ±6)` (the four corners 1m inside the walls), all rotated to face the area centre.
  - **Target randomization bounds:** in `TargetWaveManager.ResetWave()`, each target's local position is randomized to `(Random.Range(-7, 7), Random.Range(0.5, 2.5), Random.Range(-7, 7))` and rotation to face the area centre. The 0.5–2.5m Y range gives the three vertical-ray layers something to do.

**Validation:** Place one TrainingArea in an empty scene. Press Play → player spawns, targets visible. Press a context-menu "Reset" → targets fall + repop, player teleports to a spawn point.

---

## Phase 5 — AgentShooter + Heuristic + Smoke test (ADR-0003 / ADR-0004)

**New files:**
- `Assets/Scripts/Agent/AgentShooter.cs` — subclass of `Unity.MLAgents.Agent`.

  - **Sensors (auto-collected by ML-Agents) — three components for vertical fan:**
    - **3× `RayPerceptionSensorComponent3D`** on the Agent GameObject. `RaysPerDirection: 8` (= 17 horizontal rays per component), `MaxRayDegrees: 70`, `RayLength: 50`, `DetectableTags: ["Target", "Wall", "ExplosiveBarrel"]`, `UseBatchedRaycasts: true` (Unity Job System for the casts).
    - **`RayLayerMask`:** include `Default`, `Wall`, `Invisible Wall`, `Projectile`. **Exclude `First Person View` (layer 9)** because the player rig itself sits on that layer (verified — `P_LPSP_FP_CH` children all have `m_Layer: 9`); without exclusion the agent's own body blocks every ray. Easiest expression: `~(1 << LayerMask.NameToLayer("First Person View"))` — i.e. everything except First Person View.
    - The three components differ only in their `StartVerticalOffset` / `EndVerticalOffset`: **upper layer `(+1.0, +1.0)`, middle `(0, 0)`, lower `(-0.5, -0.5)`** — covers the 0.5–2.5m target Y range from a typical 1.6m camera height.
    - **`ObservationStacks: 1`** (this is *temporal* stacking — memory of past frames — and is unrelated to the vertical fan; leave it at the default).
    - **51 rays total, ~255 floats from rays** (formula: `ObservationStacks × (1 + 2 × RaysPerDirection) × (numTags + 2)` per component, summed).

  - **`CollectObservations(VectorSensor sensor)`:**
    - Local-space agent velocity XZ (2 floats from `Rigidbody.velocity` transformed to local).
    - Yaw + pitch as `(sin, cos, sin, cos)` (4 floats).
    - `ammoFraction` (1 float).
    - `targetsRemainingFraction` (1 float).
    - **Total scalar obs: 8 floats.**

  - **`OnActionReceived(ActionBuffers actions)`:**
    - Continuous: `[yaw, pitch, moveX, moveY]` ∈ [-1, 1].
    - Discrete: `[fire]` ∈ {0, 1}.
    - Forwards to bridge with scaling per ADR-0005.

  - **`Heuristic(in ActionBuffers actionsOut)`:**
    - Find nearest unhit `TargetScript` via `TargetWaveManager`.
    - Compute desired yaw/pitch deltas to face it; clamp to [-1, 1].
    - Compute desired forward movement if distance > 3m, else 0.
    - Fire = 1 if angle to target < 5° AND raycast from camera hits the target.

  - **Reward wiring (ADR-0003):**
    - On `TargetWaveManager.TargetHit`: `AddReward(+1.0)`. If `RemainingCount == 0`: `AddReward(+10.0)` and `EndEpisode()`.
    - On weapon fire that did not hit a `Target`-tagged collider: `AddReward(-0.05)`. Hook is `Weapon.OnFired` (see Modified files below — pinned, no longer "deferred").
    - In `OnActionReceived`: `-0.001` step penalty + shaped rewards `+0.05 * cos(angleToNearestUnhitTarget) * max(0, 1 - Academy.Instance.StepCount / 100000)` + `+0.01 * (lastDistance - currentDistance) * max(0, 1 - Academy.Instance.StepCount / 100000)`. Both shaping terms zero out by step 100k.
    - On 10s without a hit (tracked via `secondsSinceLastHit` accumulator on the Agent, ticked in `FixedUpdate`): `AddReward(-0.5)`, reset timer.
    - **Episode max-step:** `MaxStep` is set on the Agent inspector to **600**. Math: 60 game-seconds × 50Hz physics ÷ `DecisionPeriod=5` = 600 decisions. **Pin `Time.fixedDeltaTime = 0.02` in `Awake()` of `AgentArea`** — Unity 6's default is 0.02 but this guarantees correctness if a future `ProjectSettings/TimeManager.asset` change drifts it.
    - On `MaxStep` reached without success (handled in `OnEpisodeBegin` of the *next* episode by inspecting outcome flag set in `OnActionReceived`): `AddReward(-1.0)` once at termination.

  - **`OnEpisodeBegin()`:** call `area.ResetArea()`. Randomize target positions within bounds.

  - **`DecisionRequester` component:** `DecisionPeriod = 5`, `TakeActionsBetweenDecisions = true`.

  - **Behavior Parameters:** Behavior Name = `"Shooter"`. Action space: 4 continuous + 1 discrete (size 2). Observation size = 8 vector + sensor.

- `Assets/Tests/PlayMode/AgentSmokeTest.cs`
  - Loads `S_Training.unity`, sets all `AgentShooter.BehaviorType = HeuristicOnly`, runs for 60 game-seconds (using `WaitForFixedUpdate` × 3000 frames at the default 0.02s `fixedDeltaTime`), asserts that at least 4 of the 8 areas reached `RemainingCount == 0` at any point during the run. **Use `≥4/8 areas cleared` rather than `≥1` so a flaky heuristic (one lucky run) doesn't pass the gate.**

**Validation gate (CRITICAL — STOP HERE if this fails):**
- Set BehaviorType = `HeuristicOnly` on every agent in `S_Training`.
- Press Play. **Run 3 times.** Average ≥ 4 areas cleared per 60s run = pass.
- If heuristic can't clear targets, **PPO won't either.** Diagnose the
  environment before proceeding to Phase 6. Common failure modes:
  ammo not topping up (check `Weapon.GetAmmunitionCurrent()` in inspector),
  bridge scaling wrong (try setting `lookSpeedScale` from 5.0 → 2.0 →
  10.0), weapon raycast layer mask blocking the target (check `Weapon.mask`
  inspector field includes the target's layer), target reset broken
  (verify `TargetScript.isHit` flips back to false after `ResetWave`).

---

## Phase 6 — Training scene + PPO config + train (ADR-0001, ADR-0004)

**New files / scenes:**
- `Assets/Scenes/Training/S_Training.unity` — flat plane, 8 instances of `TrainingArea` prefab spaced 60m apart in a 4×2 grid. Add to Build Settings.
- `Assets/Scripts/Agent/TrainingConfig/shooter.yaml` (PPO config — values
  cross-checked against the current Walker/Pyramids configs on the
  ML-Agents `release_23` branch):

  ```yaml
  default_settings:
    engine_settings:
      time_scale: 20

  behaviors:
    Shooter:
      trainer_type: ppo
      hyperparameters:
        batch_size: 2048          # was 1024 — Walker-scale for continuous-dominant control
        buffer_size: 20480        # was 10240 — match raised batch
        learning_rate: 3.0e-4
        learning_rate_schedule: linear
        beta: 0.01                # was 5.0e-3 — Pyramids-scale; preserves entropy on sparse fire action
        epsilon: 0.2
        lambd: 0.95
        num_epoch: 3
      network_settings:
        normalize: true
        hidden_units: 256
        num_layers: 2
        vis_encode_type: simple
      reward_signals:
        extrinsic:
          gamma: 0.99
          strength: 1.0
        curiosity:
          strength: 0.02
          gamma: 0.99
          encoding_size: 256
          learning_rate: 3.0e-4
      max_steps: 1.0e7            # was 2.0e6 — Pyramids-scale for sparse-reward task with 8 areas
      time_horizon: 128           # was 64 — sparse rewards need longer GAE rollouts
      summary_freq: 10000
      keep_checkpoints: 5
      checkpoint_interval: 100000
  ```

**Workflow:**
1. From terminal in venv: `mlagents-learn shooter.yaml --run-id=shooter_v1 --train`
2. Press Play in Editor with `S_Training.unity` open.
3. Monitor TensorBoard: `tensorboard --logdir results`.
4. Expected reward curve: starts negative (stagnation + miss penalties), crosses zero at ~50–200k steps, asymptotes near +13 after 500k–1.5M steps.
5. On convergence (or `max_steps`), trainer saves `results/shooter_v1/Shooter.onnx`.

**Validation:** TensorBoard `Environment/Cumulative Reward` mean > 12. If the curve plateaus below +5, escalate (curriculum learning, reward re-tuning, or longer training).

---

## Phase 7 — Deploy to S_Content_Overview

**New work:**
- Import `Shooter.onnx` to `Assets/Models/Shooter.onnx`.
- In `S_Content_Overview`:
  - Add `AIControllerBridge` to existing player.
  - Add `AgentShooter` to existing player. Set `Behavior Type = InferenceOnly`. Drag `Shooter.onnx` into Model field.
  - Add a `TargetWaveManager` over the existing 4 targets in the scene.
  - Set `Character.useAIInput = true`.
- **AI vision overlay** (per Q5 / CONTEXT.md):
  - `Assets/Scripts/Agent/UI/RayVisionOverlay.cs` — MonoBehaviour with reference to the agent's `RayPerceptionSensorComponent3D`. Each `LateUpdate`, reads sensor's last raycast hit data, builds a 17×3 `Texture2D` (RGBA32, point-filtered), color-codes per ADR plan: hue=tag, brightness=closeness, black=no hit.
  - UGUI Canvas with `RawImage` (referencing the texture), positioned bottom-left, scaled to ~340×60 pixels on screen. Disable canvas scaler interpolation.

**Validation:** Press Play in `S_Content_Overview`. Agent walks around, aims at and shoots all 4 targets. Vision overlay updates in real time showing red pixels on targets, gray on walls.

---

## Files touched, created, or new — comprehensive list

### New files
```
Assets/Infima Games/Low Poly Shooter Pack - Free Sample/Code/Services/AreaServiceLocator.cs
Assets/Prefabs/Agent/TrainingArea.prefab
Assets/Scenes/Training/S_Training.unity
Assets/Scripts/Agent/AIControllerBridge.cs
Assets/Scripts/Agent/AgentArea.cs
Assets/Scripts/Agent/AgentShooter.cs
Assets/Scripts/Agent/TargetWaveManager.cs
Assets/Scripts/Agent/TrainingConfig/shooter.yaml
Assets/Scripts/Agent/UI/RayVisionOverlay.cs
Assets/Tests/PlayMode/AgentSmokeTest.cs
Assets/Tests/PlayMode/Tests.PlayMode.asmdef
Assets/Models/Shooter.onnx       (output of training)
```

### Modified files (existing Infima asset)
```
Code/Animation/CharacterAnimationEventHandler.cs   (1 line: ServiceLocator.For(this) at line 26)
Code/Camera/CameraLook.cs                          (1 line: ServiceLocator.For(this) at line 61)
Code/Character/Character.cs                        (~25 lines: 4 setters, useAIInput gate)
Code/Character/CharacterBehaviour.cs               (~10 lines: 4 abstract methods)
Code/Character/Movement.cs                         (1 line: ServiceLocator.For(this) at line 90)
Code/Services/GameModeService.cs                   (~5 lines: new constructor)
Code/Services/ServiceLocator.cs                    (~15 lines: For() static method)
Code/Weapons/Weapon.cs                             (1 line: ServiceLocator.For(this) at line 143;
                                                    plus ~15 lines: public OnFired event for wasted-shot hook)
```

### `Weapon.OnFired` event — wasted-shot detection hook (replaces Risk #1)

In `Weapon.cs`, add:

```csharp
public event System.Action<RaycastHit?, bool> OnFired;
```

Fire it at the end of `Weapon.Fire()` (Weapon.cs:204–238), immediately after the projectile is instantiated. The first arg is the `RaycastHit?` from the `Physics.Raycast` already performed at line 230 (nullable — null when nothing was hit). The second arg is `(hit.HasValue && hit.Value.collider.CompareTag("Target"))`. The Agent subscribes in `OnEnable`/`OnDisable` and routes the boolean to `RewardCalculator.OnShotFired(hitTarget)`.

This is the **selected** option from Risk #1 (was deferred). The alternative — mirroring the raycast in the Agent — is rejected because it duplicates work and can drift from the weapon's actual mask.

### Modified config
```
Packages/manifest.json                             (+2 packages)
ProjectSettings/EditorBuildSettings.asset          (add S_Training scene)
```

---

## Out of scope (explicit non-goals)

- Reload, holster, inspect, weapon switch — agent never invokes these (ADR-0002).
- Jump — never invoked.
- Multi-weapon training — single weapon (the default M4) for the entire project.
- Moving targets — targets are static. The pack's `TargetScript` only does up/down animation; lateral motion would require new code.
- Self-play / multi-agent combat — single agent, target-clearing only.
- Visual observations (camera pixels) — observations are RayPerception + scalars only (CONTEXT.md).
- Imitation learning / GAIL / behavioral cloning — pure RL only (ADR-0001).
- Cloud GPU training — local Mac CPU only (CONTEXT.md operational notes).
- Modifications to the demo scene's *layout* — only adding agent components to the existing player.
- Scripting `S_Content_Overview` for randomization — the demo scene stays as-is; randomization happens in `S_Training` only.

---

## Risks and unresolved details

1. **Wasted-shot detection hook — RESOLVED.** Implementation pinned: add
   `public event Action<RaycastHit?, bool> OnFired` to `Weapon.cs`,
   invoked at the end of `Fire()`. See "Modified files" above for the exact
   signature and call site.

2. **`TargetScript` random repop.** `TargetScript.cs:31` calls
   `Random.Range(minTime, maxTime)` every Update. Non-deterministic but
   harmless for training because `TargetWaveManager.ResetWave()` sets
   `isHit = false` synchronously at episode start, bypassing the
   coroutine path. No mitigation needed beyond what's already in Phase 4.

3. **`CharacterAnimationEventHandler` lookup timing — RESOLVED.**
   `AreaServiceLocator` carries `[DefaultExecutionOrder(-1000)]` per
   Phase 1, guaranteeing it registers before any Infima script's
   `Awake()` runs.

4. **Projectile cross-area contamination.** Projectiles are physical
   Rigidbodies (`Projectile.destroyAfter` in seconds, defaults to 5–30s
   per prefab). A stray bullet from area A could enter area B. Mitigation
   stack:
   - **60m inter-area spacing** in `S_Training.unity` (Phase 6).
   - **Invisible-wall colliders** on layer `Invisible Wall`, included in
     the player-weapon `LayerMask` so projectiles physically stop.
   - **Override `Projectile.destroyAfter` to 2.0s** on the projectile
     prefab variant used by training (set on the Weapon's
     `prefabProjectile` override in the `TrainingArea` prefab if the
     training weapon is set up as a prefab variant).

5. **Training-time per-area FPS — MITIGATION PINNED.** 8 areas with full
   FPS rendering would be CPU-bound on Mac. In `S_Training.unity`,
   disable the player camera's rendering (`Camera.enabled = false`) on 7
   of the 8 areas via a one-shot script that runs in `Awake` of any
   `TrainingArea` instance whose name is not `TrainingArea (0)`. Aim
   raycasts use the transform, not the camera render, so disabling
   render does not break observations or fire.

6. **Agent spawning collision — RESOLVED.** `AgentArea.ResetArea()` uses
   `Rigidbody.position` and zeroes velocity (Phase 4 spec).

7. **ML-Agents toolchain compatibility verified May 2026.** Pinned
   versions per ADR-0007: Unity 6 + `com.unity.ml-agents` 4.0.0 +
   `mlagents` 1.1.0 + Python 3.10.12 + PyTorch ~=2.2.1. P4 prerequisite
   trains a 3DBall ONNX and confirms it imports — proves the toolchain
   before we invest in the real environment.

8. **Infima Games asset on Unity 6 — RESOLVED.** Project verified
   running on Unity 6000.1.15f1 with zero CS-prefixed compile errors and
   the demo scene loaded cleanly. Phase 0 fix-up budget unspent. The
   only console noise is unrelated `JobTempAlloc` allocator
   info-messages from MCP polling. `com.coplaydev.unity-mcp` works on
   Unity 6.

9. **`Weapon.mask` LayerMask configuration.** This is an Inspector field
   on each weapon prefab (`P_LPSP_*` weapons). The agent that picks up
   this work must verify (in Phase 5 manual testing) that the equipped
   weapon's `mask` includes the layer that targets sit on (likely
   `Default`). If targets are invisible to the weapon's raycast,
   `Weapon.Fire` will spawn projectiles that fly toward `playerCamera.forward * 1000`
   instead of toward the target — manifests as "agent fires but never
   hits." Diagnose by inspecting the weapon's `mask` field and the
   `P_LPSP_DMG_Target` prefab's layer assignment.

---

## Validation gates summary

| After phase | Gate |
|---|---|
| 0 | Project opens cleanly in Unity 6; demo scene still plays. |
| P4 | A trivial 3DBall ONNX trained in the venv imports cleanly into Unity 6. |
| 1 | Demo scene plays unchanged. |
| 2 | Demo scene plays unchanged. Setters compile and write fields. |
| 3 | Bridge ContextMenu test moves player as expected. Toggling `useAIInput` cleanly hands off between human and AI. |
| 4 | One `TrainingArea` in isolation: Play, reset, targets repop, ammo refills, agent respawns. |
| 5 | **Heuristic mode clears ≥1 area in 60s.** PlayMode smoke test green. **STOP if this fails.** |
| 6 | TensorBoard mean episode reward > 12 within `max_steps`. |
| 7 | Trained `.onnx` plays `S_Content_Overview`, AI vision overlay renders. |

---

## Effort estimate

| Phase | Calendar effort | Risk |
|---|---|---|
| P0 (Unity 6 upgrade) | 0.5–1 day | medium (Infima asset compat) |
| P1–P5 setup (incl. ONNX smoke test) | 0.5 day | low |
| Phases 1–2 | 0.5 day | low |
| Phases 3–4 | 1 day | medium (target reset, prefab wiring) |
| Phase 5 | 1.5 days | **high — the heuristic gate decides everything** |
| Phase 6 | 0.5 day setup + 1–4 hours training | medium |
| Phase 7 | 0.5 day | low |
| **Total** | **~4.5–6 days** | — |
