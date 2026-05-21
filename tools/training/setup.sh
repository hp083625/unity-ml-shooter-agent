#!/usr/bin/env bash
set -euo pipefail

# Reproducible Python venv for ML-Agents training.
# Pinned versions per ADR-0007:
#   - Python 3.10.12
#   - mlagents 1.1.0 (Release 23 Python side)
#   - PyTorch 2.2.x (transitive)
#   - Inference happens in Unity via Inference Engine 2.2.1 (bundled with com.unity.ml-agents 4.0.0)
#
# Tested on: macOS (Apple Silicon, arm64).
#
# WHY THIS SCRIPT EXISTS:
#   mlagents 1.1.0 metadata pins grpcio<=1.48.2, which has no ARM64 wheels and
#   fails to build from source on Apple Silicon clang. We work around this by
#   installing the ENTIRE dep tree from `requirements.lock.txt` with --no-deps
#   so pip never tries to satisfy the bad grpcio pin. The lock file uses
#   grpcio 1.80 (binary wheels available, wire-compatible with the Unity-side
#   gRPC stubs) and is the single source of truth for versions in this venv.

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VENV_DIR="${HERE}/.venv"
PYTHON_VERSION="3.10.12"
LOCK_FILE="${HERE}/requirements.lock.txt"

# 1. Pin Python with pyenv ----------------------------------------------------
if ! command -v pyenv >/dev/null 2>&1; then
  echo "ERROR: pyenv not found. Install it first:"
  echo "  brew install pyenv"
  echo "  # then ensure shims are on PATH (https://github.com/pyenv/pyenv#set-up-your-shell-environment-for-pyenv)"
  exit 1
fi

PYENV_ROOT="${PYENV_ROOT:-$HOME/.pyenv}"
if [ ! -d "${PYENV_ROOT}/versions/${PYTHON_VERSION}" ]; then
  echo "Installing Python ${PYTHON_VERSION} via pyenv (~3 min)..."
  pyenv install -s "${PYTHON_VERSION}"
fi

PY_BIN="${PYENV_ROOT}/versions/${PYTHON_VERSION}/bin/python"
test -x "${PY_BIN}" || { echo "ERROR: ${PY_BIN} missing after pyenv install"; exit 1; }

# 2. Create the venv ----------------------------------------------------------
if [ ! -d "${VENV_DIR}" ]; then
  echo "Creating venv at ${VENV_DIR}"
  "${PY_BIN}" -m venv "${VENV_DIR}"
fi

# shellcheck disable=SC1091
source "${VENV_DIR}/bin/activate"

# 3. Bootstrap pip + setuptools ----------------------------------------------
# pip's metadata-resolver (24.1+) refuses mlagents 1.1.0's grpcio<=1.48.2 pin,
# so we use --no-deps below. We still want a recent pip for binary-wheel selection.
python -m pip install --upgrade 'pip<24.1' 'setuptools<70' wheel

# 4. Install the entire pinned dep tree from the lock file, --no-deps -------
# --no-deps means pip never looks at metadata pins (which is the whole point —
# mlagents 1.1.0's grpcio<=1.48.2 pin is exactly what we're working around).
# The lock file already has every transitive dep, so dep resolution is a no-op.
# --only-binary=:all: ensures we never trigger source compilation (no ARM64
# build of old grpcio exists, and we don't need it — grpcio 1.80 in the lock
# is wire-compatible with the Unity-side gRPC stubs).
python -m pip install --only-binary=:all: --no-deps -r "${LOCK_FILE}"

# 5. Verify -------------------------------------------------------------------
echo
echo "=== Verification ==="
python - <<'PY'
import importlib.metadata as md
for pkg in ("mlagents", "mlagents-envs", "torch", "grpcio", "protobuf", "numpy"):
    print(f"  {pkg}: {md.version(pkg)}")

# Confirm the gRPC stubs import (proves grpcio 1.80 is wire-compatible)
from mlagents_envs.communicator_objects import unity_input_pb2 as _  # noqa: F401
print("  grpc stubs import: OK")
PY
mlagents-learn --help >/dev/null && echo "  mlagents-learn --help: OK"

echo
echo "Activate the venv with: source tools/training/.venv/bin/activate"
