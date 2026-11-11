#!/usr/bin/env bash
# Builds the native budgetcalc library into native/runtimes/<target>/.
# Usage (from any folder):  bash native/build.sh [win-x64|linux-x64]
# Without an argument the current platform is used (Git Bash on Windows -> win-x64).
set -euo pipefail
cd "$(dirname "$0")"

target="${1:-}"
if [ -z "$target" ]; then
  case "$(uname -s)" in
    MINGW*|MSYS*|CYGWIN*) target="win-x64" ;;
    Linux) target="linux-x64" ;;
    *) echo "Unsupported platform $(uname -s); pass win-x64 or linux-x64" >&2; exit 1 ;;
  esac
fi

case "$target" in
  win-x64)
    # clang + lld-link (LLVM). No C runtime is needed, so no Visual Studio libraries are required.
    mkdir -p runtimes/win-x64
    clang --target=x86_64-pc-windows-msvc -O2 -ffreestanding -nostdlib -shared -fuse-ld=lld \
      -Wl,-noentry -o runtimes/win-x64/budgetcalc.dll budgetcalc.c
    rm -f runtimes/win-x64/budgetcalc.lib runtimes/win-x64/budgetcalc.exp runtimes/win-x64/budgetcalc.pdb
    ;;
  linux-x64)
    mkdir -p runtimes/linux-x64
    gcc -O2 -shared -fPIC -fvisibility=hidden -o runtimes/linux-x64/libbudgetcalc.so budgetcalc.c
    ;;
  *)
    echo "Unknown target '$target' (use win-x64 or linux-x64)" >&2
    exit 1
    ;;
esac

echo "Built native library for $target in native/runtimes/$target"
