#!/usr/bin/env bash
# Builds the two floor benchmarks. No engine, no Silk.NET, no build.sh - the whole point of them is that nothing of ours is in the measurement. sudo apt install libvulkan-dev     # for the headers, if they are
# missing ./build.sh && ./vkfloor && ./vkwrite Layers off while measuring: a validation layer sits in front of every command and would be the only thing these numbers said. VK_LOADER_LAYERS_DISABLE='*' ./vkfloor

set -euo pipefail
cd "$(dirname "$0")"

# The headers, wherever this machine keeps them. The engine carries none of its own since the core became C#, so
# the system's are the first choice and a sibling checkout that still has the C++ core's copy is the fallback.
include=""
for candidate in \
	/usr/include \
	"$HOME/Documents/Rinten-Engine-shaders/engine/Core/extern/vulkan" \
	"$HOME/Documents/Rinten-Engine-models/engine/Core/extern/vulkan"
do
	if [ -f "$candidate/vulkan/vulkan.h" ]; then include="$candidate"; break; fi
done

if [ -z "$include" ]; then
	echo "no vulkan headers - sudo apt install libvulkan-dev" >&2
	exit 1
fi

# The loader by soname: a machine with the runtime but not the dev package has
# libvulkan.so.1 and no libvulkan.so for -lvulkan to find.
loader=""
for candidate in \
	/usr/lib/x86_64-linux-gnu/libvulkan.so \
	/usr/lib/x86_64-linux-gnu/libvulkan.so.1 \
	/usr/lib/libvulkan.so.1
do
	if [ -f "$candidate" ]; then loader="$candidate"; break; fi
done

if [ -z "$loader" ]; then echo "no vulkan loader" >&2; exit 1; fi

for one in vkfloor vkwrite; do
	cc -O2 -Wall -I"$include" "$one.c" -o "$one" "$loader"
	echo "built $one"
done
