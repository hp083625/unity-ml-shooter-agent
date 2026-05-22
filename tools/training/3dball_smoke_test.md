# 3DBall ONNX import smoke test on Unity 6 + ML-Agents 4.0.0

## Purpose

Confirm that bug [`Unity-Technologies/ml-agents#6293`](https://github.com/Unity-Technologies/ml-agents/issues/6293)
(NullReferenceException on continuous-action ONNX import) is **not present**
on Unity 6 + ML-Agents 4.0.0. ADR-0007 ([docs/adr/0007-upgrade-to-unity-6.md](../../docs/adr/0007-upgrade-to-unity-6.md))
tracked the upgrade specifically to dodge this bug; this document is the
verification record proving the dodge worked.

## Environment

- Unity 6 LTS (per ADR-0007 — formerly Unity 2022.3.58f1).
- ML-Agents package: `com.unity.ml-agents@592fae96fab2` (Unity 6 + ML-Agents 4.0.0 toolchain).
- Inference engine: `com.unity.ai.inference@803814f81708` (`Unity.InferenceEngine.ModelAsset` / `ModelLoader`).
- Host: macOS (the platform the original bug manifested on).

## Fixture

ML-Agents 4.0.0 ships its own continuous-action ONNX regression-test
fixtures. We reuse one of them to avoid checking large external assets
into the repo.

- **Source path** (read-only, inside the package cache):
  ```
  /Users/hitesh/Documents/Unity/My project/Library/PackageCache/com.unity.ml-agents@592fae96fab2/Tests/Editor/TestModels/deterContinuous2vis8vec2action_v2_0.onnx
  ```
- **Size**: `74136` bytes (~72 KB) — well under the 100 KB threshold for
  committing the fixture into the worktree.
- **Repo path** (committed): `Assets/Models/Smoke/3DBallSmoke.onnx`.

The file is a deterministic continuous-action policy with 2 visual + 8
vector observations and 2 continuous actions — same family of model as
the 3DBall policy that originally tripped #6293.

## Procedure

Each step uses the `UnityMCP` MCP tools described in the project
`CLAUDE.md`. No host shell required for the verification itself.

### Step 1 — locate fixture

```csharp
// mcp__UnityMCP__execute_code
var files = System.IO.Directory.GetFiles(
    "/Users/hitesh/Documents/Unity/My project/Library/PackageCache",
    "*.onnx", System.IO.SearchOption.AllDirectories);
return string.Join("\n", files.Select(f => $"{new System.IO.FileInfo(f).Length}B {f}"));
```

Among the results, the fixture used was:

```
74136B /Users/hitesh/Documents/Unity/My project/Library/PackageCache/com.unity.ml-agents@592fae96fab2/Tests/Editor/TestModels/deterContinuous2vis8vec2action_v2_0.onnx
```

### Step 2 — copy into the project

Copied identically to both the host project and the worktree so Unity
can import it and so the worktree commit is self-contained:

```csharp
// mcp__UnityMCP__execute_code
var src = "/Users/hitesh/Documents/Unity/My project/Library/PackageCache/com.unity.ml-agents@592fae96fab2/Tests/Editor/TestModels/deterContinuous2vis8vec2action_v2_0.onnx";
System.IO.Directory.CreateDirectory("/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke");
System.IO.Directory.CreateDirectory("/Users/hitesh/Documents/Unity/My project/.worktrees/agent-13/Assets/Models/Smoke");
System.IO.File.Copy(src, "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke/3DBallSmoke.onnx", true);
System.IO.File.Copy(src, "/Users/hitesh/Documents/Unity/My project/.worktrees/agent-13/Assets/Models/Smoke/3DBallSmoke.onnx", true);
```

Result: both files = `74136` bytes.

### Step 3 — refresh the editor

```text
mcp__UnityMCP__refresh_unity(mode="force", compile="request", wait_for_ready=True)
```

Unity imports the ONNX into a `Unity.InferenceEngine.ModelAsset` and
generates a `.meta` (GUID `b1a931f3c22fd427d9a43a72dcdf3930`). The meta
is copied alongside the ONNX into the worktree so the GUID is stable
across machines.

### Step 4 — scrub the console

```text
mcp__UnityMCP__read_console(types=["error"], count=50, filter_text="NullReferenceException")
mcp__UnityMCP__read_console(types=["error"], count=50, filter_text="SentisModelInfo")
mcp__UnityMCP__read_console(types=["error"], count=50, filter_text="3DBallSmoke")
```

Literal results from this run:

```json
{"success":true,"message":"Retrieved 0 log entries.","data":[]}
{"success":true,"message":"Retrieved 0 log entries.","data":[]}
{"success":true,"message":"Retrieved 0 log entries.","data":[]}
```

Zero entries on each filter. The unfiltered tail of the error stream
shows only `MCP-FOR-UNITY: Client handler exited` infra noise from
parallel agents — nothing related to ONNX import or ML-Agents.

### Step 5 — invoke the exact ctor that NRE'd in #6293

```csharp
// mcp__UnityMCP__execute_code
var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(
    "Assets/Models/Smoke/3DBallSmoke.onnx");
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

The ctor returns a non-null `Unity.MLAgents.Inference.SentisModelInfo`
instance against the deserialized continuous-action policy. This is the
exact code path that throws `NullReferenceException` on Unity 2022.3 +
ML-Agents on macOS per #6293.

## Conclusion

**PASS.** Bug `ml-agents#6293` is **not present** on Unity 6 + ML-Agents 4.0.0:

- Console contains zero `NullReferenceException` entries.
- Console contains zero `SentisModelInfo` errors.
- Console contains zero `3DBallSmoke`-related errors.
- The two-arg `Unity.MLAgents.Inference.SentisModelInfo(Model, bool)`
  constructor returns a non-null instance against a deserialized
  continuous-action ONNX.

ADR-0007's stated reason for upgrading to Unity 6 (avoid #6293) is
empirically validated. Unblocks downstream ML-Agents work on macOS.

## Cleanup

The host's `Assets/Models/Smoke/` tree was used only to verify import
and capture the `.meta`. After commit, the host can be cleaned up so it
doesn't keep an unused asset around:

```bash
rm -f "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke/3DBallSmoke.onnx"
rm -f "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke/3DBallSmoke.onnx.meta"
rm -f "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke.meta"
rmdir "/Users/hitesh/Documents/Unity/My project/Assets/Models/Smoke" 2>/dev/null
rm -f "/Users/hitesh/Documents/Unity/My project/Assets/Models.meta"
rmdir "/Users/hitesh/Documents/Unity/My project/Assets/Models" 2>/dev/null
```

(Followed by a Unity refresh so the editor doesn't keep referencing the
deleted asset.) The worktree under `.worktrees/agent-13/Assets/Models/Smoke/`
remains the canonical home of the fixture for the PR.
