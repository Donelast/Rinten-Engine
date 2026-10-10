#!/bin/bash
# Makes librintenngx.so - NVIDIA's NGX, the library DLSS is reached through - and
# puts it, with NVIDIA's DLSS runtime, into game/bin/linuxsteamrt64. See CoreDlss.
#
#   ./build-ngx.sh           skip if already there
#   ./build-ngx.sh force     download and link again even if it is
#
# Why this rather than a line in engine/natives.json: NVIDIA ships NGX as a static
# library, not a shared one, so something has to link it into one the engine can
# open - and the SDK's licence lets a game carry its binaries but not a repository
# hold them under an open licence. So they are fetched here, at a pinned commit of
# NVIDIA's own repository, checked against the hashes below, and never committed:
# game/bin is ignored.
#
# The link is the whole archive and nothing else - its API is C, and CoreDlss
# fetches every function from the result by name. No NVIDIA header is compiled.
#
# A machine without an NVIDIA card loses nothing by having these: CoreDlss asks
# the card first, and a missing library only means DLSS is not offered.
#
# Needs curl, sha256sum and a C++ compiler (g++ or clang++) to link with.
#
# Keep LF line endings or Linux will not execute this.

set -e

# NVIDIA/DLSS at SDK 310.9.1.
DLSS_COMMIT="374959484e79a640feaba44c93ac8cfb0a03f5b5"
DLSS_RUNTIME="libnvidia-ngx-dlss.so.310.9.1"

NGX_SHA256="3321c4ca9adf71f345550d10bf2148ed38003151b03ec5f81c99d00949ab8202"
RUNTIME_SHA256="7561f16cab74e2b7ccf791bf44bb9cc43abb44bcf78f33c5663520ad4e857e02"

RAW="https://raw.githubusercontent.com/NVIDIA/DLSS/${DLSS_COMMIT}"

SRC="$(cd "$(dirname "$0")" && pwd)"

if [ "$(uname -s)" != "Linux" ]; then
	echo "ERROR: build-ngx.sh runs on Linux. Windows links with build-ngx.cmd." >&2
	exit 1
fi

# NVIDIA publishes an arm64 build as well, and nothing arm64 here has one of its
# cards; x86-64 only until something does.
case "$(uname -m)" in
	x86_64|amd64) PLATFORM="linuxsteamrt64"; ARCH="Linux_x86_64" ;;
	*) echo "No DLSS for $(uname -m) - skipped."; exit 0 ;;
esac

DEST="$SRC/../../game/bin/$PLATFORM"
WORK="$SRC/build/ngx/$PLATFORM"

if [ "$1" != "force" ] && [ -f "$DEST/librintenngx.so" ] && [ -f "$DEST/$DLSS_RUNTIME" ]; then
	echo "NGX and DLSS $DLSS_RUNTIME already in $DEST"
	exit 0
fi

CXX="${CXX:-}"
if [ -z "$CXX" ]; then
	if command -v g++ >/dev/null; then CXX=g++; elif command -v clang++ >/dev/null; then CXX=clang++; fi
fi

for tool in curl sha256sum "$CXX"; do
	if [ -z "$tool" ] || ! command -v "$tool" >/dev/null; then
		echo "ERROR: no ${tool:-C++ compiler}." >&2
		exit 1
	fi
done

mkdir -p "$WORK" "$DEST"

fetch() {
	local from="$1" into="$2" sha="$3"

	if [ -f "$into" ] && echo "$sha  $into" | sha256sum -c --status; then
		return 0
	fi

	echo "Downloading $(basename "$into")"
	curl -fsSL -o "$into.part" "$RAW/$from"

	if ! echo "$sha  $into.part" | sha256sum -c --status; then
		echo "ERROR: $(basename "$into") does not match its hash - NVIDIA's repository changed under the pin?" >&2
		rm -f "$into.part"
		exit 1
	fi

	mv "$into.part" "$into"
}

fetch "lib/$ARCH/libnvsdk_ngx.a" "$WORK/libnvsdk_ngx.a" "$NGX_SHA256"
fetch "lib/$ARCH/rel/$DLSS_RUNTIME" "$WORK/$DLSS_RUNTIME" "$RUNTIME_SHA256"

# The whole archive into one shared object, its C++ runtime linked in so it needs
# nothing a Steam runtime might not have.
"$CXX" -shared -fPIC -o "$WORK/librintenngx.so" \
	-Wl,--whole-archive "$WORK/libnvsdk_ngx.a" -Wl,--no-whole-archive \
	-ldl -static-libstdc++ -static-libgcc

cp "$WORK/librintenngx.so" "$DEST/librintenngx.so"
cp "$WORK/$DLSS_RUNTIME" "$DEST/$DLSS_RUNTIME"

echo
echo "librintenngx.so and $DLSS_RUNTIME -> $DEST"
