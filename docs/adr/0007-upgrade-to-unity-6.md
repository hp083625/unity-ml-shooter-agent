# ADR-0007: Upgrade project to Unity 6 + ML-Agents Release 23

- **Status:** Accepted
- **Date:** 2026-05-21
- **Amends:** [ADR-0001](./0001-rl-with-ml-agents.md)

## Context

A deep-dive verification (May 2026) of the ML-Agents toolchain surfaced
that the project's pinned Unity version (**2022.3.58f1**) is no longer on
the supported path:

| ML-Agents Release | Package | Min Unity | Released |
|---|---|---|---|
| Release 23 | `com.unity.ml-agents` 4.0.0 | **Unity 6000.0** | Aug 2025 |
| Release 22 | `com.unity.ml-agents` 3.0.0 | Unity 2023.2 | Sep 2024 |
| Release 21 | `com.unity.ml-agents` 3.0.0-exp.1 | **Unity 2022.3** ✓ | Oct 2023 |

The latest version that supports Unity 2022.3 is an *experimental preview*
from Oct 2023. In addition, **open issue
[Unity-Technologies/ml-agents#6293](https://github.com/Unity-Technologies/ml-agents/issues/6293)**
(filed 2026-05-06, no maintainer reply) reports that on macOS + Unity
2022.3 + PyTorch 2.x, ONNX import for *continuous-action* models throws
`NullReferenceException`. PPO with continuous actions is exactly our
workload. No workaround posted.

Three paths were on the table:
- **A** — Stay on Unity 2022.3 + ML-Agents Release 21 (exp). Stale, may
  hit bug #6293 at deploy time.
- **B** — Upgrade to Unity 2023.2 + ML-Agents Release 22. Unity 2023.2
  is itself non-LTS and superseded by Unity 6 — would force a second
  upgrade soon.
- **C** — Upgrade to Unity 6 (LTS) + ML-Agents Release 23. Current
  everything. Bug #6293 does not apply.

User chose C explicitly with "more work doesn't matter" framing.

## Decision

Upgrade the project from **Unity 2022.3.58f1** to **Unity 6 LTS**
(6000.0.x latest at time of upgrade), and pin the ML-Agents stack to
**Release 23**:

- `com.unity.ml-agents` Unity package: **4.0.0**
- `mlagents` Python package: **1.1.0**
- Python: **3.10.1 ≤ x ≤ 3.10.12** (3.10.12 recommended)
- PyTorch: **~=2.2.1**
- Inference runtime: **Unity Inference Engine 2.2.1** (formerly "Sentis")

The upgrade is performed as a new prerequisite phase **P0** in
[SCOPE.md](../../SCOPE.md): open the project in Unity 6, accept the
auto-migration, fix any compile errors, and verify the
`S_Content_Overview` demo still plays before any agent code lands.

## Consequences

- **Inference Engine integration is native.** ML-Agents 4.0.0 loads ONNX
  via the bundled Inference Engine runtime through
  `Unity.MLAgents.Inference.SentisModelInfo`/`SentisModelParamLoader` —
  no manual Sentis loading required. The "Sentis" branding mentioned in
  earlier ADRs is now "Inference Engine."
- **Bug #6293 does not apply.** It is specific to Unity 2022.3.
- **`UseBatchedRaycasts: true`** is available on `RayPerceptionSensorComponent3D`
  (uses Unity Job System). Important for 8 parallel agents × 51 rays.
- **Infima Games Low Poly Shooter Pack compatibility risk.** The pack
  was published for Unity 2021/2022. Auto-migration is *probably* clean
  but `com.unity.postprocessing` 3.4.0 (PPv2) is legacy on Unity 6, the
  pack's input system bindings may need refresh, and shader/animator
  references could break. Estimated impact: 0.5–1 day of fix work in
  P0. Tracked as a risk in SCOPE.
- **CLAUDE.md** must be updated — the project conventions document
  references Unity 2022.3.58f1 in two places.
- **Repository churn.** Opening the 1.8GB project in Unity 6 will
  regenerate `Library/` (gitignored) and may modify `ProjectSettings/`
  files (committed). Expect a one-time large diff after migration.
- **`com.coplaydev.unity-mcp`** (MCP for Unity bridge) — verify it
  supports Unity 6. If not, a stub workflow without MCP is acceptable
  during P0 (we'd lose live-scene introspection but keep file-based
  workflow).

## Considered alternatives

- **A — Stay on 2022.3 + Release 21 (exp).** Rejected: experimental
  preview, two years stale, blocks `UseBatchedRaycasts`, hits bug #6293
  on macOS.
- **B — Upgrade to 2023.2 + Release 22.** Rejected: Unity 2023.2 is
  non-LTS and already superseded by Unity 6. Would force a second
  upgrade in months.
