#!/bin/bash
# Builds the OpenXR loader from the pinned Khronos source release and installs it into game/bin/linuxsteamrt64 (linuxsteamrtarm64 on an arm64 machine). ./build-openxr.sh           skip if already there ./build-openxr.sh force     build it again even if it is Why this rather than a line in engine/natives.json: Khronos publishes the loader built for Windows and for Android and not for Linux, so the one Unix that runs a headset builds it
# - pinned to the release the other two download, so every platform finds its runtime through the same loader. It is what a game carries: SteamVR and Monado are runtimes, and neither puts a loader beside the game. Needs cmake, ninja and python3 (the loader's sources are generated from the spec at configure time). Nothing else: no graphics, no window system - only the loader is built. Keep LF line endings or Linux will not execute this.

set -e

XR_VERSION="1.1.63"
XR_SHA256="a3b97a36f11abe256a7ea1668a0a468aac9b738e94bea6b468f0ae31ad537a46"
XR_URL="https://github.com/KhronosGroup/OpenXR-SDK-Source/releases/download/release-${XR_VERSION}/OpenXR-SDK-Source-release-${XR_VERSION}.tar.gz"

SRC="$(cd "$(dirname "$0")" && pwd)"

if [ "$(uname -s)" != "Linux" ]; then
	echo "ERROR: build-openxr.sh runs on Linux. Windows and Android download a binary - see engine/natives.json." >&2
	exit 1
fi

case "$(uname -m)" in
	aarch64|arm64) PLATFORM="linuxsteamrtarm64" ;;
	*)             PLATFORM="linuxsteamrt64" ;;
esac

DEST="$SRC/../../game/bin/$PLATFORM"
WORK="$SRC/build/openxr/$PLATFORM"

if [ "$1" != "force" ] && [ -f "$DEST/libopenxr_loader.so.1" ]; then
	echo "OpenXR loader $XR_VERSION already in $DEST"
	exit 0
fi

for tool in cmake ninja curl python3; do
	if ! command -v "$tool" >/dev/null; then
		echo "ERROR: no $tool." >&2
		exit 1
	fi
done

mkdir -p "$WORK"

ARCHIVE="$WORK/OpenXR-SDK-Source-$XR_VERSION.tar.gz"

if [ ! -f "$ARCHIVE" ]; then
	echo "Downloading the OpenXR SDK source $XR_VERSION"
	curl -fsSL -o "$ARCHIVE.part" "$XR_URL"
	mv "$ARCHIVE.part" "$ARCHIVE"
fi

# Checked every time, not only after a download - see build-sdl.sh.
GOT="$(sha256sum "$ARCHIVE" | cut -d' ' -f1)"

if [ "$GOT" != "$XR_SHA256" ]; then
	echo "ERROR: $ARCHIVE is not the pinned release." >&2
	echo "  expected $XR_SHA256" >&2
	echo "  got      $GOT" >&2
	rm -f "$ARCHIVE"
	exit 1
fi

UNPACKED="$WORK/source"

if [ ! -d "$UNPACKED" ]; then
	mkdir -p "$UNPACKED"
	tar -xzf "$ARCHIVE" -C "$UNPACKED" --strip-components=1
fi

# The loader alone. The samples and the test helpers want X11 and OpenGL development packages the engine does not,
# so they are switched off - and OpenGL is kept from being found, which is what builds the helper regardless.
cmake -S "$UNPACKED" -B "$WORK/build" -G Ninja \
	-DCMAKE_BUILD_TYPE=Release \
	-DDYNAMIC_LOADER=ON \
	-DBUILD_API_LAYERS=OFF \
	-DBUILD_TESTS=OFF \
	-DBUILD_CONFORMANCE_TESTS=OFF \
	-DBUILD_SDK_TESTS=OFF \
	-DCMAKE_DISABLE_FIND_PACKAGE_OpenGL=ON \
	-DCMAKE_DISABLE_FIND_PACKAGE_OpenGLES=ON

cmake --build "$WORK/build" --target openxr_loader

mkdir -p "$DEST"

# The soname is what the engine opens - see CoreXr - and the other two names
# point at the real file, as the linker made them.
cp -a "$WORK/build/src/loader"/libopenxr_loader.so* "$DEST/"

echo
echo "OpenXR loader $XR_VERSION -> $DEST"
