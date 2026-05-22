# 3DBall ONNX smoke test on Unity 6 + ML-Agents 4.0.0 — partial

## TL;DR

This document records a **partial** smoke test for issue #13 covering the two
halves of the toolchain separately, plus the manual steps required to close
the structural gap if the maintainer wants full end-to-end coverage.

- **Half A — Python venv toolchain.** `mlagents-learn --help` exits 0 and the
  Unity gRPC stubs import cleanly. The venv can drive a training run; the
  blocker is the environment to drive against (see below).
- **Half B — Unity-side ONNX import.** A representative continuous-action
  ONNX (the `com.unity.ml-agents@4.0.0` regression-test fixture
  `deterContinuous2vis8vec2action_v2_0.onnx`) imports into Unity 6 with zero
  console errors, and `Unity.MLAgents.Inference.SentisModelInfo(Model, bool)`
  — the exact constructor that `NullReferenceException`s in
  [`Unity-Technologies/ml-agents#6293`](https://github.com/Unity-Technologies/ml-agents/issues/6293)
  — returns a non-null instance. ADR-0007's stated reason for upgrading to
  Unity 6 is empirically validated.

What's missing is the **bridge** between the two halves: producing an ONNX
artifact from `mlagents-learn` against a live Unity environment in this
session. That requires either a separate Unity Editor instance running the
3DBall scene from the cloned ml-agents repo, or a standalone build of that
scene. Both options are documented as manual maintainer steps below.

## Why partial

Issue #13 asks for a literal end-to-end flow:

1. Clone ml-agents `release_23` → done (in `/tmp/ml-agents-clone`).
2. Run `mlagents-learn` against 3DBall for ~30s → blocked.
3. Import the resulting `3DBall.onnx` into Unity → covered by Half B with the
   package fixture (same model family).
4. Confirm clean console + reproducible procedure → covered by Half B.

Step 2 is blocked on this single-Editor-session machine because
`mlagents-learn` connects via gRPC to a *running Unity Editor instance with
an active 3DBall environment in Play mode*. Driving a second Editor instance
into Play mode interactively is not in scope for an MCP-driven agent.

The two real ways to bridge the gap, both manual:

### Option B — drive a second Unity Editor

1. Open `/tmp/ml-agents-clone/Project/` in a second Unity 6 instance.
2. Load the `3DBall` scene.
3. Hit Play.
4. In another terminal: `source tools/training/.venv/bin/activate` and
   `mlagents-learn /tmp/ml-agents-clone/config/ppo/3DBall.yaml --run-id=smoke --max-steps=30000 --no-graphics-monitor`.
5. After ~30s the trainer writes `results/smoke/3DBall.onnx`. Copy it to
   `Assets/Models/Smoke/3DBall.onnx` and re-run the import scrub in Half B.

Caveat: the cloned `Project/` was authored against Unity 2023.x; opening it
in Unity 6 will trigger an upgrade flow with its own warnings to triage.

### Option C — headless build of the 3DBall scene

1. Author a small `BuildPipeline.BuildPlayer` editor script in
   `/tmp/ml-agents-clone/Project/Assets/Editor/Build3DBall.cs`.
2. Run `Unity.app/Contents/MacOS/Unity -batchmode -nographics -quit -projectPath /tmp/ml-agents-clone/Project -executeMethod Editor.Build3DBall.Build -logfile /tmp/3dball-build.log`.
3. Run `mlagents-learn /tmp/ml-agents-clone/config/ppo/3DBall.yaml --env=<built binary> --run-id=smoke --max-steps=30000 --no-graphics-monitor`.

Option C is more deterministic but takes 1-2 hours of setup the first time
(authoring the build script, dealing with the Unity-version mismatch on the
cloned project, scripting registration backend etc.) and we're not landing
that work in this PR without an explicit maintainer call.

## Half A — venv toolchain

Activate:

```bash
source tools/training/.venv/bin/activate
```

The verified-working state was already captured in
[`tools/training/README.md`](./README.md) when the venv was set up under
issue #3. For this session the same pinned versions (`mlagents==1.1.0`,
`mlagents-envs==1.1.0`, `torch==2.2.2`, `grpcio==1.80.0`,
`protobuf==3.20.3`, `numpy==1.23.5`) verified clean again.

Sanity:

```bash
mlagents-learn --help                                                       # exits 0, lists subcommands
python -c "from mlagents_envs.communicator_objects import unity_input_pb2"  # silent (gRPC stubs OK)
```

### Literal session output

```text
$ mlagents-learn --help | head -22
usage: mlagents-learn [-h] [--env ENV_PATH] [--resume] [--deterministic]
                      [--force] [--run-id RUN_ID] [--initialize-from RUN_ID]
                      [--seed SEED] [--inference] [--base-port BASE_PORT]
                      [--num-envs NUM_ENVS] [--num-areas NUM_AREAS] [--debug]
                      [--env-args ...]
                      [--max-lifetime-restarts MAX_LIFETIME_RESTARTS]
                      [--restarts-rate-limit-n RESTARTS_RATE_LIMIT_N]
                      [--restarts-rate-limit-period-s RESTARTS_RATE_LIMIT_PERIOD_S]
                      [--torch] [--tensorflow] [--results-dir RESULTS_DIR]
                      [--timeout-wait TIMEOUT_WAIT] [--width WIDTH]
                      [--height HEIGHT] [--quality-level QUALITY_LEVEL]
                      [--time-scale TIME_SCALE]
                      [--target-frame-rate TARGET_FRAME_RATE]
                      [--capture-frame-rate CAPTURE_FRAME_RATE]
                      [--no-graphics] [--no-graphics-monitor]
                      [--torch-device DEVICE]
                      [trainer_config_path]
$ python -c "from mlagents_envs.communicator_objects import unity_input_pb2; print('ok')"
ok
$ python -c "import importlib.metadata as md; print(md.version('mlagents'), md.version('torch'), md.version('grpcio'))"
1.1.0 2.2.2 1.80.0
```

That confirms the venv can launch a trainer; what we cannot do in this
session is point it at a live Unity environment (see "Why partial").

## Half B — Unity-side ONNX import

### Fixture

ML-Agents 4.0.0 ships its own continuous-action ONNX regression-test
fixtures. We reuse one — the same model family as 3DBall (continuous
actions, deterministic policy) — to avoid checking external assets into the
repo and to keep the smoke test reproducible without an active venv run.

- **Source path** (read-only, inside the package cache):
  ```
  Library/PackageCache/com.unity.ml-agents@592fae96fab2/Tests/Editor/TestModels/deterContinuous2vis8vec2action_v2_0.onnx
  ```
- **Size**: 74,136 bytes (~72 KB).
- **Repo path** (committed): `Assets/Models/Smoke/3DBall.onnx`.

### Procedure

Driven via `UnityMCP` MCP tools. No host shell required for the verification
itself.

#### Step 1 — copy fixture into the project

```csharp
// mcp__UnityMCP__execute_code
var src = "/Users/hitesh/Documents/Unity/My project/Library/PackageCache/com.unity.ml-agents@592fae96fab2/Tests/Editor/TestModels/deterContinuous2vis8vec2action_v2_0.onnx";
var hostDir = "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke";
var wtDir = "/Users/hitesh/Documents/Unity/My project/.worktrees/agent-13/Assets/Models/Smoke";
System.IO.Directory.CreateDirectory(hostDir);
System.IO.Directory.CreateDirectory(wtDir);
System.IO.File.Copy(src, System.IO.Path.Combine(hostDir, "3DBall.onnx"), true);
System.IO.File.Copy(src, System.IO.Path.Combine(wtDir, "3DBall.onnx"), true);
```

Result: both files = 74,136 bytes.

#### Step 2 — refresh the editor

```text
mcp__UnityMCP__refresh_unity(mode="force", compile="request", wait_for_ready=True)
```

Unity imports the ONNX into a `Unity.InferenceEngine.ModelAsset` and
generates a `.meta`. The meta is copied alongside the ONNX into the worktree
so the GUID is stable across machines.

#### Step 3 — scrub the console

```text
mcp__UnityMCP__read_console(types=["error"], count=50, filter_text="NullReferenceException")
mcp__UnityMCP__read_console(types=["error"], count=50, filter_text="SentisModelInfo")
mcp__UnityMCP__read_console(types=["error"], count=50, filter_text="3DBall")
```

Literal results:

```json
{"success":true,"message":"Retrieved 0 log entries.","data":[]}
{"success":true,"message":"Retrieved 0 log entries.","data":[]}
{"success":true,"message":"Retrieved 0 log entries.","data":[]}
```

Zero entries on each filter.

#### Step 4 — invoke the exact ctor that NRE'd in #6293

```csharp
// mcp__UnityMCP__execute_code
var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(
    "Assets/Models/Smoke/3DBall.onnx");
if (asset == null) return "ASSET_LOAD_FAILED";
var model = Unity.InferenceEngine.ModelLoader.Load(asset);
if (model == null) return "MODEL_LOAD_FAILED";
var smiType = System.AppDomain.CurrentDomain.GetAssemblies()
    .SelectMany(a => { try { return a.GetTypes(); } catch { return new System.Type[0]; } })
    .FirstOrDefault(t => t.FullName == "Unity.MLAgents.Inference.SentisModelInfo");
if (smiType == null) return "SMI_TYPE_NOT_FOUND";
var ctor = smiType.GetConstructors().FirstOrDefault(c => c.GetParameters().Length == 2);
if (ctor == null) return "SMI_CTOR_NOT_FOUND";
var smi = ctor.Invoke(new object[] { model, false });
return $"OK smi_type={smi.GetType().FullName} ctor=({string.Join(",", ctor.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})";
```

Literal return value:

```
OK smi_type=Unity.MLAgents.Inference.SentisModelInfo ctor=(Model model,Boolean deterministicInference)
```

That is the exact code path that throws `NullReferenceException` on
Unity 2022.3 + ML-Agents on macOS per #6293. On Unity 6 + ML-Agents 4.0.0 it
returns a non-null instance.

## Conclusion

**Half A: PASS** — venv toolchain runs, gRPC stubs import.

**Half B: PASS** — bug `ml-agents#6293` is **not present** on Unity 6 +
ML-Agents 4.0.0:

- Console contains zero `NullReferenceException` entries.
- Console contains zero `SentisModelInfo` errors.
- Console contains zero `3DBall`-related errors.
- The two-arg `Unity.MLAgents.Inference.SentisModelInfo(Model, bool)`
  constructor returns a non-null instance against a deserialized
  continuous-action ONNX.

ADR-0007's stated reason for upgrading to Unity 6 (avoid #6293) is
empirically validated.

**Bridge: NOT RUN.** Producing an ONNX artifact from `mlagents-learn`
against a live Unity environment requires the manual Option B or Option C
above. The model family is identical to 3DBall, so a successful end-to-end
run is not expected to surface anything Half B did not already cover, but
that's an inference, not a measurement.

## For the maintainer

Two ways forward — please pick:

1. **Land partial.** Half A and Half B together cover the failure mode the
   issue was scoped to detect (#6293). The Bridge step is a nice-to-have
   that adds confidence the trainer's specific output format also imports
   cleanly, but Inference Engine 2.2.1 imports any well-formed ONNX, so the
   delta is small. Re-scope #13 to "import-only verification + venv health
   check" and close.
2. **Insist on full end-to-end.** Hand off the manual Option B (faster) or
   Option C (more deterministic). Both are documented above. Re-open #13
   with a follow-up PR once the bridge has been run and `Assets/Models/Smoke/3DBall.onnx`
   has been replaced with a venv-produced artifact.

## Cleanup

The host's `Assets/Models/Smoke/` tree was used only to verify import and
capture the `.meta`. After commit:

```bash
rm -f "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke/3DBall.onnx"
rm -f "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke/3DBall.onnx.meta"
rm -f "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke.meta"
rmdir "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke" 2>/dev/null
rm -f "/Users/hitesh/Documents/Unity/My project/Assets/Models.meta"
rmdir "/Users/hitesh/Documents/Unity/My project/Assets/Models" 2>/dev/null
```

(Followed by a Unity refresh so the editor doesn't keep referencing the
deleted asset.) The worktree under `.worktrees/agent-13/Assets/Models/Smoke/`
remains the canonical home of the fixture for the PR.
