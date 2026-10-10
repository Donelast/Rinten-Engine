#!/bin/bash
# Builds libSDL3 from the pinned source release and installs it into game/bin/<platform>. ./build-sdl.sh              this machine's platform, skip if already there ./build-sdl.sh force        build it again even if it is Why this exists rather than a line in engine/natives.json: SDL publishes no Linux binary at all, and its only macOS asset is a .dmg, which fetch-natives cannot open - it reads .zip and .tar.gz. So the two Unixes build the one library they need from source, pinned to the same release win64 downloads, and nothing drifts between platforms. It used to be a sentence in Bootstrap.sh telling
# a person to do this by hand. Nobody did: the library in a long-lived checkout was put there once, years of commits ago, and a fresh clone had no SDL and could not open a window. Needs cmake, ninja and the development packages SDL's own README-linux lists - X11 or Wayland, xkbcommon, DBus and one of the audio stacks: sudo apt install cmake ninja-build libx11-dev libxext-dev libwayland-dev \ libxkbcommon-dev libdbus-1-dev libasound2-dev libpulse-dev On macOS the frameworks come with the Xcode command line tools and there is nothing else to install. Keep LF line endings or Linux will not execute this.

set -e

# The release win64 pins in engine/natives.json. One version for every platform: a window
# system bug that only reproduces on one of them is hard enough without two SDLs in the mix.
SDL_VERSION="3.4.14"
SDL_SHA256="30d4aa2b3037718142b32dffd4e72f917ebb6cc5227150e7bb9c45efb2153aeb"
SDL_URL="https://github.com/libsdl-org/SDL/releases/download/release-${SDL_VERSION}/SDL3-${SDL_VERSION}.tar.gz"

SRC="$(cd "$(dirname "$0")" && pwd)"

case "$(uname -s)" in
	Darwin) PLATFORM="osxarm64"; LIBNAME="libSDL3.0.dylib" ;;
	Linux)
		# arm64 is a Steam Frame's SteamOS - see build-linux.sh.
		case "$(uname -m)" in
			aarch64|arm64) PLATFORM="linuxsteamrtarm64" ;;
			*)             PLATFORM="linuxsteamrt64" ;;
		esac
		LIBNAME="libSDL3.so.0" ;;
	*)      echo "ERROR: build-sdl.sh runs on Linux and macOS. Windows downloads a binary." >&2; exit 1 ;;
esac

DEST="$SRC/../../game/bin/$PLATFORM"
WORK="$SRC/build/sdl/$PLATFORM"

# Already there is the common case, and rebuilding SDL on every engine build
# would add minutes to it for nothing.
if [ "$1" != "force" ] && [ -f "$DEST/$LIBNAME" ]; then
	echo "SDL3 $SDL_VERSION already in $DEST"
	exit 0
fi

for tool in cmake ninja curl; do
	if ! command -v "$tool" >/dev/null; then
		echo "ERROR: no $tool." >&2
		exit 1
	fi
done

mkdir -p "$WORK"

ARCHIVE="$WORK/SDL3-$SDL_VERSION.tar.gz"

if [ ! -f "$ARCHIVE" ]; then
	echo "Downloading SDL $SDL_VERSION"
	curl -fsSL -o "$ARCHIVE.part" "$SDL_URL"
	mv "$ARCHIVE.part" "$ARCHIVE"
fi

# Checked every time, not only after a download: a half-written file from an
# interrupted build looks exactly like a good one to the line above.
if command -v sha256sum >/dev/null; then
	GOT="$(sha256sum "$ARCHIVE" | cut -d' ' -f1)"
else
	GOT="$(shasum -a 256 "$ARCHIVE" | cut -d' ' -f1)"
fi

if [ "$GOT" != "$SDL_SHA256" ]; then
	echo "ERROR: $ARCHIVE is not the pinned release." >&2
	echo "  expected $SDL_SHA256" >&2
	echo "  got      $GOT" >&2
	rm -f "$ARCHIVE"
	exit 1
fi

UNPACKED="$WORK/SDL3-$SDL_VERSION"

if [ ! -d "$UNPACKED" ]; then
	tar -xzf "$ARCHIVE" -C "$WORK"
fi

# Shared only. The engine opens SDL by name at runtime through its own resolver,
# so a static SDL would be a second copy of the same code that nothing loads.
cmake -S "$UNPACKED" -B "$WORK/build" -G Ninja \
	-DCMAKE_BUILD_TYPE=Release \
	-DSDL_SHARED=ON \
	-DSDL_STATIC=OFF \
	-DSDL_TESTS=OFF \
	-DSDL_EXAMPLES=OFF \
	-DSDL_INSTALL=OFF

cmake --build "$WORK/build"

mkdir -p "$DEST"

# The real file and the names that point at it. The engine's resolver asks for "SDL3", which becomes libSDL3.so on Linux and libSDL3.dylib on
# macOS, and the soname the linker recorded is the middle one - all three have to be there or the one that is looked up is the one that is missing.
copied=0

for lib in "$WORK/build"/libSDL3.so* "$WORK/build"/libSDL3*.dylib; do
	[ -e "$lib" ] || continue

	if [ -L "$lib" ]; then
		cp -a "$lib" "$DEST/"
	else
		cp -f "$lib" "$DEST/"
	fi

	copied=$((copied + 1))
done

if [ "$copied" -eq 0 ]; then
	echo "ERROR: the build produced no libSDL3 to install." >&2
	exit 1
fi

echo
echo "libSDL3 $SDL_VERSION -> $DEST"
