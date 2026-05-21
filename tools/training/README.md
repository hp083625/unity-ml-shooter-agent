# Training Toolchain

Python venv used for `mlagents-learn` against the Unity Editor.

Pinned per [ADR-0007](../../docs/adr/0007-upgrade-to-unity-6.md):

| Component | Version |
|---|---|
| Python | 3.10.12 |
| `mlagents` | 1.1.0 |
| `mlagents-envs` | 1.1.0 |
| PyTorch | 2.2.2 (range `~=2.2.1`) |

The `com.unity.ml-agents` Unity package (4.0.0) bundles Inference Engine 2.2.1 for in-engine ONNX inference — that's a separate concern from this venv.

## First-time setup

```bash
brew install pyenv          # if not already installed
./tools/training/setup.sh   # ~5 min on first run; idempotent on re-runs
```

Then activate:

```bash
source tools/training/.venv/bin/activate
```

You should see `(.venv)` in your prompt and `mlagents-learn --help` should work.

## What `setup.sh` does (and why)

mlagents 1.1.0's metadata pins `grpcio<=1.48.2`, which **has no ARM64 wheel** and fails to build from source on Apple Silicon clang. Standard `pip install mlagents==1.1.0` therefore fails on this Mac.

The setup script installs the entire pinned dep tree from [`requirements.lock.txt`](./requirements.lock.txt) using `pip install --no-deps --only-binary=:all:`. Two flags doing two jobs:

- `--no-deps` bypasses pip's metadata resolver entirely, so mlagents 1.1.0's bad `grpcio<=1.48.2` pin is ignored. The lock file already has every transitive dep pinned, so dep resolution is a no-op.
- `--only-binary=:all:` forbids source builds. There is no ARM64 wheel for the old grpcio pin and no need for one — the lock file uses `grpcio==1.80.0` (binary wheels available, wire-compatible with the Unity-side gRPC stubs, verified by importing `mlagents_envs.communicator_objects` cleanly).

The lock file is the single source of truth for versions in this venv. To update a dep, edit the lock file, re-run `setup.sh` against a fresh `.venv/`, and confirm verification passes before committing.

## Verification

After activation, all three of these must work:

```bash
mlagents-learn --help                                                # exits 0, lists subcommands
python -c "import importlib.metadata as md; print(md.version('mlagents'))"   # 1.1.0
python -c "import torch; print(torch.__version__)"                   # 2.2.x
python -c "from mlagents_envs.communicator_objects import unity_input_pb2"  # silent (gRPC stubs OK)
```

`setup.sh` itself runs these checks at the end and prints `mlagents-learn --help: OK`.

## Apple Silicon notes

- Tested on `arm64` (Apple Silicon). PyTorch 2.2.x and grpcio 1.62+ both ship ARM64 wheels — no Rosetta needed.
- If `pyenv install 3.10.12` fails complaining about a build dependency, run `brew install openssl readline sqlite3 xz zlib tcl-tk` and retry.

## Day-to-day

```bash
source tools/training/.venv/bin/activate                    # enter the venv
mlagents-learn config/ppo/Shooter.yaml --run-id=run_001     # train (after Phase 1 lands)
deactivate                                                   # leave the venv
```
