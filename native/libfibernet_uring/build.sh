#!/usr/bin/env bash
# build.sh — Convenience script for building libfibernet_uring.so
#
# Usage:
#   ./build.sh                 # Release build in ./build/
#   ./build.sh --debug         # Debug build
#   ./build.sh --install       # Build + install to /usr/local
#
# The compiled .so is automatically copied to the .NET runtimes folder
# so the managed project can find it:
#   ../src/FiberNet.Transport.Kestrel.IoUring/runtimes/linux-x64/native/
#
# Prerequisites: cmake, liburing-dev (apt) / liburing-devel (dnf)

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_TYPE="Release"
DO_INSTALL=false

for arg in "$@"; do
    case "$arg" in
        --debug)   BUILD_TYPE="Debug"   ;;
        --install) DO_INSTALL=true      ;;
    esac
done

BUILD_DIR="$SCRIPT_DIR/build"
mkdir -p "$BUILD_DIR"

echo "==> Configuring (${BUILD_TYPE})…"
cmake -B "$BUILD_DIR" -S "$SCRIPT_DIR" \
      -DCMAKE_BUILD_TYPE="$BUILD_TYPE" \
      -DCMAKE_EXPORT_COMPILE_COMMANDS=ON

echo "==> Building…"
cmake --build "$BUILD_DIR" --parallel "$(nproc)"

# Copy the .so into the runtimes folder consumed by the .NET project
ARCH="$(uname -m)"
case "$ARCH" in
    x86_64)  RID="linux-x64"   ;;
    aarch64) RID="linux-arm64" ;;
    *)        RID="linux-$ARCH" ;;
esac

RUNTIMES_DIR="$SCRIPT_DIR/../../src/FiberNet.Transport.Kestrel.IoUring/runtimes/$RID/native"
mkdir -p "$RUNTIMES_DIR"

SO_SRC="$BUILD_DIR/libfibernet_uring.so"
echo "==> Copying ${SO_SRC} → ${RUNTIMES_DIR}/"
cp -f "$SO_SRC" "$RUNTIMES_DIR/"

if [[ "$DO_INSTALL" == true ]]; then
    echo "==> Installing (may require sudo)…"
    cmake --install "$BUILD_DIR"
fi

echo ""
echo "Done. Native library at:"
echo "  $RUNTIMES_DIR/libfibernet_uring.so"
echo ""
echo "To run the Kestrel benchmark with io_uring:"
echo "  cd $SCRIPT_DIR/../../benchmarks/FiberNet.Benchmarks.Kestrel"
echo "  dotnet run -c Release -- --connections 64"
