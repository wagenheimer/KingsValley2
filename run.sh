#!/usr/bin/env bash
# King's Valley II launcher for Linux and macOS.
# Forwards all arguments to the game (e.g. ./run.sh --headless).
#
# Optional diagnostic environment variables before running:
#   export KINGSVALLEY_START_SCENE=level
#   export KINGSVALLEY_LEVEL=1
#   export KINGSVALLEY_GFX=msx2

set -e
cd "$(dirname "$0")"
dotnet run --project src/KingsValley.Desktop "$@"
