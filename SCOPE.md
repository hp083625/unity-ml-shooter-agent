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

## Prerequisites (one-time setup)

| # | Item | Notes |
|---|------|-------|
| P1 | Install Unity packages: `com.unity.ml-agents` (latest 2.x for Unity 2022.3 LTS), `com.unity.sentis` (latest 2.x) | Add to `Packages/manifest.json`. Verify both compile cleanly via `read_console`. |
| P2 | Create Python venv with `mlagents` package | Use `pyenv` to pin Python 3.10. `pip install mlagents`. Verify `mlagents-learn --help` works. |
| P3 | Create `Assets/Tests/PlayMode/` with `.asmdef` referencing `nunit.framework`, `UnityEngine.TestRunner`, `UnityEditor.TestRunner` | Per CLAUDE.md, the project currently has no test assemblies. |
| P4 | Add `"Wall"` and `"Target"` tags already exist in `TagManager.asset` (verified). No tag work needed. | — |

---

## Phase 1 — Scoped service locator (ADR-0006)

**New files:**
- `Assets/Infima Games/Low Poly Shooter Pack - Free Sample/Code/Services/AreaServiceLocator.cs`
  - MonoBehaviour at root of `TrainingArea` prefab. On `Awake`, creates a private `ServiceLocator`, finds its child `CharacterBehaviour`, registers `new GameModeService(character)` into it.

**Modified files:**
- `ServiceLocator.cs` — add `static ServiceLocator For(Component c)` that walks `c.transform` upward for an `AreaServiceLocator`; returns `Current` as fallback.
- `GameModeService.cs` — add `GameModeService(CharacterBehaviour ch)` constructor; existing parameterless ctor still uses `FindObjectOfType` for the global fallback.
- `Weapons/Weapon.cs:143` — `ServiceLocator.Current` → `ServiceLocator.For(this)`.
- `Character/Movement.cs:90` — same.
- `Camera/CameraLook.cs:61` — same.
- `Animation/CharacterAnimationEventHandler.cs` — same.

**Validation:** Open `S_Content_Overview`, press Play, fire weapon, hit target. Original demo behavior unchanged because no `AreaServiceLocator` exists in that scene → `For(this)` falls through to `Current`.

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

- `Assets/Scripts/Agent/AgentArea.cs` *(optional helper, may be folded into AreaServiceLocator)*
  - Holds references to the area's `Character`, `AIControllerBridge`, `TargetWaveManager`, and spawn points.
  - Provides `ResetArea()` which respawns the player at a random spawn point and resets the wave.

- `Assets/Prefabs/Agent/TrainingArea.prefab` (new prefab)
  - Hierarchy:
    ```
    TrainingArea (AreaServiceLocator, AgentArea, AgentShooter — Phase 5)
    ├── Player (instance of P_LPSP_FP_CH, with AIControllerBridge added,
    │           useAIInput=true, ammo set high)
    ├── Targets (TargetWaveManager)
    │   ├── Target_0 (P_LPSP_DMG_Target)
    │   ├── Target_1
    │   ├── Target_2
    │   └── Target_3
    ├── Walls (4 box colliders on "Invisible Wall" layer, bounding the area)
    └── SpawnPoints (4 empty transforms for randomized agent spawn)
    ```
  - Total area footprint: ~25m × 25m.
  - **Ammo handling:** call `equippedWeapon.FillAmmunition(-1)` from `ResetArea()` to top up — guarantees no reload during episodes per ADR-0002.

**Validation:** Place one TrainingArea in an empty scene. Press Play → player spawns, targets visible. Press a context-menu "Reset" → targets fall + repop, player teleports to a spawn point.

---

## Phase 5 — AgentShooter + Heuristic + Smoke test (ADR-0003 / ADR-0004)

**New files:**
- `Assets/Scripts/Agent/AgentShooter.cs` — subclass of `Unity.MLAgents.Agent`.

  - **Sensors (auto-collected by ML-Agents):**
    - `RayPerceptionSensorComponent3D` configured: `RaysPerDirection: 8` (= 17 horizontal rays), `StackedRaycasts: 3`, `MaxRayDegrees: 70`, `RayLength: 50`, `DetectableTags: ["Target", "Wall", "ExplosiveBarrel"]`. **51 rays total.**

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
    - On `TargetWaveManager.TargetHit`: `+1.0`. If `RemainingCount == 0`: `+10.0` and `EndEpisode()`.
    - On `Weapon.Fire()` raycast hit not tagged `Target`: `-0.05` (need a hook — see "Risks" below).
    - In `OnActionReceived`: `-0.001` step penalty + shaped rewards `+0.05 * cos(angleToTarget) * (1 - stepCount/100000)` + `+0.01 * (lastDist - currentDist) * (1 - stepCount/100000)`. Both shaping terms clamped to ≥ 0 multiplier.
    - On 10s without a hit (tracked in `FixedUpdate`): `-0.5`, reset timer.
    - On `OnEpisodeBegin` after `MaxStep` reached without success: `-1.0` (set `MaxStep = 60s × 50Hz / decisionPeriod = 600` decision steps).

  - **`OnEpisodeBegin()`:** call `area.ResetArea()`. Randomize target positions within bounds.

  - **`DecisionRequester` component:** `DecisionPeriod = 5`, `TakeActionsBetweenDecisions = true`.

  - **Behavior Parameters:** Behavior Name = `"Shooter"`. Action space: 4 continuous + 1 discrete (size 2). Observation size = 8 vector + sensor.

- `Assets/Tests/PlayMode/AgentSmokeTest.cs`
  - Loads `S_Training.unity`, sets all `AgentShooter.Behavior Type = HeuristicOnly`, runs for 60 game-seconds (60 / `Time.fixedDeltaTime` FixedUpdates), asserts at least one area has `RemainingCount == 0`.

**Validation gate (CRITICAL — STOP HERE if this fails):**
- Set Behavior Type = `HeuristicOnly` on every agent in `S_Training`.
- Press Play. Heuristic should reliably clear most areas in <60s.
- If heuristic can't clear targets, **PPO won't either.** Diagnose the
  environment before proceeding to Phase 6. Common failure modes:
  ammo not topping up, bridge scaling wrong, weapon raycast layer mask
  blocking the target, target reset broken.

---

## Phase 6 — Training scene + PPO config + train (ADR-0001, ADR-0004)

**New files / scenes:**
- `Assets/Scenes/Training/S_Training.unity` — flat plane, 8 instances of `TrainingArea` prefab spaced 60m apart in a 4×2 grid. Add to Build Settings.
- `Assets/Scripts/Agent/TrainingConfig/shooter.yaml` (canonical PPO config):

  ```yaml
  behaviors:
    Shooter:
      trainer_type: ppo
      hyperparameters:
        batch_size: 1024
        buffer_size: 10240
        learning_rate: 3.0e-4
        learning_rate_schedule: linear
        beta: 5.0e-3
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
      max_steps: 2.0e6
      time_horizon: 64
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
Code/Animation/CharacterAnimationEventHandler.cs   (1 line: ServiceLocator.For(this))
Code/Camera/CameraLook.cs                          (1 line: ServiceLocator.For(this))
Code/Character/Character.cs                        (~25 lines: 4 setters, useAIInput gate)
Code/Character/CharacterBehaviour.cs               (~10 lines: 4 abstract methods)
Code/Character/Movement.cs                         (1 line: ServiceLocator.For(this))
Code/Services/GameModeService.cs                   (~5 lines: new constructor)
Code/Services/ServiceLocator.cs                    (~15 lines: For() static method)
Code/Weapons/Weapon.cs                             (1 line: ServiceLocator.For(this))
```

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

1. **Wasted-shot detection hook.** ADR-0003 says `-0.05` for shots whose
   raycast didn't hit a `Target`. `Weapon.Fire()` (Weapon.cs:230) does the
   raycast internally but does not expose the result. Options:
   - **Add a `public event Action<RaycastHit?, bool> OnFired` to `Weapon.cs`** that fires after the raycast, with the hit + a "did it hit a Target" flag. Simplest. Requires another Infima asset edit. **Recommended.**
   - Reflectively mirror the raycast in `AgentShooter` (Physics.Raycast on the same camera ray, same mask) every fire — duplicates work, may diverge.
   - **Decision deferred to Phase 5 implementation.**

2. **`TargetScript` random repop.** `TargetScript.cs:31` calls `Random.Range(minTime, maxTime)` every Update. Non-deterministic but harmless for training (we override via `TargetWaveManager.ResetWave()` at episode start). Flagged for awareness.

3. **`CharacterAnimationEventHandler` lookup timing.** If it `Awake`s before the `AreaServiceLocator` registers, lookup falls through to `Current` (single-character fallback) and silently picks the wrong character in a multi-area scene. **Mitigation:** ensure `AreaServiceLocator` has a low Script Execution Order, OR have it register in `OnEnable` rather than `Awake`. **Verify in Phase 1 testing.**

4. **Projectile cross-area contamination.** Projectiles are physical Rigidbodies with ~30s despawn. A stray bullet from area A could enter area B. Mitigation: 60m+ inter-area spacing AND invisible walls AND short bullet despawn (override `Projectile.destroyAfter` in training-area prefab override).

5. **Training-time per-area FPS.** 8 areas with full FPS rendering may be CPU-bound on Mac. **Mitigation:** in the training scene, disable the player camera's rendering (`Camera.enabled = false`) on 7 of the 8 areas, keep only one rendering. Aim raycasts use the transform, not the camera render. Document in Phase 6.

6. **Agent spawning collision.** Re-spawning the player Rigidbody mid-FixedUpdate can cause the physics step to glitch. **Mitigation:** zero velocity + use `Rigidbody.position` (not `transform.position`) inside `OnEpisodeBegin()`.

7. **ML-Agents / Sentis version compatibility.** ML-Agents historically shipped its own inference path (Barracuda); newer versions integrate Sentis but support varies by version. **Verify in P1 setup** that the chosen ML-Agents package version reads `.onnx` via Sentis or its own runtime correctly.

---

## Validation gates summary

| After phase | Gate |
|---|---|
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
| P1–P3 setup | 0.5 day | low |
| Phases 1–2 | 0.5 day | low |
| Phases 3–4 | 1 day | medium (target reset, prefab wiring) |
| Phase 5 | 1.5 days | **high — the heuristic gate decides everything** |
| Phase 6 | 0.5 day setup + 1–4 hours training | medium |
| Phase 7 | 0.5 day | low |
| **Total** | **~4–5 days** | — |
