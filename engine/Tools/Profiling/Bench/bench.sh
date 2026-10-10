#!/bin/bash
# The engine's measuring bench: the same scenes, the same way, one command. bench.sh tree NAME [REV]         build a tree to measure, from the working tree or a commit bench.sh run [options] [scene..] measure every scene (or the ones named) in one tree bench.sh ab A B [options] [scene..] A and B interleaved, A B A B, then compared; each side is TREE, TREE@"convars" or @"convars" (the -t tree) bench.sh report DIR              the table for one run bench.sh compare DIR_A DIR_B     two runs side by side, pictures compared pixel by pixel run / ab options: -t TREE      tree name (under $BENCH_HOME/trees) or a path to one; default "main" -l LABEL     output folder name under $BENCH_HOME/out; default the time -f FRAMES    frames measured per scene (default 1500); -w SECONDS warm-up (default 20) -c CONVARS   convars for every run, "name value;name value" -r REPS      ab only: rounds of A B
# (default 2) --no-check   skip the validated run that also photographs the window What every scene gets: a timing run, with no validation and no photograph (both cost frame time), then a short check run under the Vulkan validation layers that writes the window's picture. The report fails a scene whose check run had any validation error. The clock is stopped (host_timescale 0, host_time 100) so the pictures of two runs can be compared, and the tree's saved settings are removed before each run so no run inherits what the last one left behind. Safe for the machine by construction: it builds only in its own trees, never while less than 7 GB is free, runs one measurement at a time under the shared GPU lock, kills nothing but the process it started, and asks the GPU for nothing a normal run does not - in particular no RGP/SQTT capture, which froze this machine's only card once. See README.md.

set -u

HERE=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
REPO=$(cd "$HERE/../../../.." && pwd)
BENCH_HOME=${BENCH_HOME:-$HOME/.cache/rinten-bench}
GPU_LOCK=${BENCH_GPU_LOCK:-$HOME/.cache/rinten-perf/gpu.lock}
DISPLAY_NAME=${BENCH_DISPLAY:-rinten-rp}
BASE_CONVARS="host_timescale 0;host_time 100"

source "$HERE/scenes.sh"

say() { printf '%s\n' "$*" >&2; }
die() { say "bench: $*"; exit 1; }

# -- the machine ----------------------------------------------------------------

# Waits until at least $1 MB of memory is free - a build beside another session's
# build or a running editor has taken the whole desktop down before.
wait_for_memory() {
	local want=$1 what=$2 free
	for _ in $(seq 1 120); do
		free=$(awk '/MemAvailable/ { print int( $2 / 1024 ) }' /proc/meminfo)
		[ "$free" -ge "$want" ] && return 0
		say "bench: $what waits for memory: ${free} MB free, wants ${want} MB"
		sleep 5
	done
	die "$what: never got ${want} MB of memory free"
}

# Percent of the processor idle over one second.
idle_percent() {
	local a b c d e f g h t1 i1 t2
	read -r _ a b c d e f g h _ < /proc/stat; t1=$((a+b+c+d+e+f+g+h)); i1=$d
	sleep 1
	read -r _ a b c d e f g h _ < /proc/stat; t2=$((a+b+c+d+e+f+g+h))
	echo $(( 100 * ( d - i1 ) / ( t2 - t1 ) ))
}

# Waits, briefly, for the machine to be quiet. Other programs on the card or the
# processor move the numbers, so the report says how quiet it was either way.
wait_for_quiet() {
	for _ in $(seq 1 30); do
		[ "$(idle_percent)" -ge 80 ] && return 0
		sleep 1
	done
	say "bench: the machine is busy - numbers from this run are noisier"
}

# Session buses whose nested compositor is gone. dbus-run-session ends its bus when the compositor exits, but one killed hard leaves the bus behind, reparented to systemd --user - and with it everything the bus started: gvfs, evolution,
# goa, a portal. About nine inotify instances a session, out of a per-user budget the desktop already mostly holds; on 09-26 twenty-eight of them had spent it, and no editor started that day could watch its files - no hotload at all.
reap_dead_sessions() {
	local manager
	manager=$(pgrep -u "$(id -u)" -x systemd | head -1)
	[ -n "$manager" ] || return 0

	ps -u "$(id -u)" -o pid=,ppid=,args= \
		| awk -v m="$manager" '$2 == m && /dbus-daemon/ && /--print-address/ && /--session/ { print $1 }' \
		| xargs -r kill 2>/dev/null
}

# A headless compositor of our own to run in, so a measured window is never the
# desktop's and a crash takes nothing of the desktop with it.
ensure_display() {
	local socket="/run/user/$(id -u)/$DISPLAY_NAME"

	# A compositor holds its socket's lock for as long as it lives. One killed hard leaves both files behind, and
	# a socket nobody answers on reads as a display: every run then started with "no window" and measured nothing.
	if [ -S "$socket" ]; then
		flock -n "$socket.lock" true 2>/dev/null || return 0
		rm -f "$socket" "$socket.lock"
	fi

	reap_dead_sessions
	( setsid dbus-run-session -- gnome-shell --wayland --no-x11 --headless \
		--virtual-monitor 1920x1200 --wayland-display="$DISPLAY_NAME" >/dev/null 2>&1 9>&- & )
	for _ in $(seq 1 20); do
		[ -S "/run/user/$(id -u)/$DISPLAY_NAME" ] && return 0
		sleep 0.5
	done
	die "the headless compositor '$DISPLAY_NAME' did not start"
}

# -- trees -------------------------------------------------------------------------

tree_path() {
	case "$1" in
		*/*) echo "$1" ;;
		*) echo "$BENCH_HOME/trees/$1" ;;
	esac
}

# Builds a tree to measure: the working tree as it is now, or REV's engine, addons and editor over it. A tree of its own because
# build.sh will not build while an editor runs, and so that a measurement never uses - or overwrites - the build somebody is working in.
cmd_tree() {
	[ $# -ge 1 ] || die "tree NAME [REV]"
	local name=$1 rev=${2:-} dest
	dest=$(tree_path "$name")
	mkdir -p "$dest"

	wait_for_memory 7000 "the build"

	say "bench: $name <- $REPO${rev:+ at $rev}"
	rsync -a --delete \
		--exclude=/.git --exclude=obj/ --exclude=/engine/Core/build/ --exclude="/engine/**/bin/" \
		--exclude=/game/logs/ --exclude=/game/config/ --exclude=/game/.vs/ \
		"$REPO/" "$dest/" || die "copy failed"

	if [ -n "$rev" ]; then
		local exp
		exp=$(mktemp -d "$BENCH_HOME/export.XXXXXX")
		git -C "$REPO" archive "$rev" engine game/addons game/editor | tar -x -C "$exp" || die "no such revision: $rev"
		for part in engine game/addons game/editor; do
			rsync -a --delete --exclude=obj/ --exclude=bin/ --exclude=/Core/build/ "$exp/$part/" "$dest/$part/"
		done
		rm -rf "$exp"
		git -C "$REPO" rev-parse "$rev" > "$dest/.bench-revision"
	else
		{ git -C "$REPO" rev-parse HEAD; git -C "$REPO" status --short | head -50; } > "$dest/.bench-revision"
	fi

	# build.sh points the desktop's launcher at the tree it built; that is the
	# person's launcher, so it is put back as it was.
	local launcher="$HOME/.local/share/applications/rinten.desktop" kept=""
	if [ -f "$launcher" ]; then kept=$(mktemp); cp "$launcher" "$kept"; fi

	local log="$dest/.bench-build.log" failed=0
	( cd "$dest" && MSBUILDDISABLENODEREUSE=1 ./build.sh managed ) > "$log" 2>&1 || failed=1
	if [ $failed = 0 ] && grep -aqE "error CS|Build FAILED" "$log"; then failed=1; fi
	if [ $failed = 0 ]; then
		( cd "$dest" && ./build.sh shaders ) >> "$log" 2>&1 || failed=1
	fi

	[ -n "$kept" ] && { cp "$kept" "$launcher"; rm -f "$kept"; }
	dotnet build-server shutdown >/dev/null 2>&1

	if [ $failed != 0 ]; then
		grep -aE "error CS" "$log" | sort -u | head -20 >&2
		die "$name did not build - $log"
	fi

	say "bench: $name built"
}

# -- runs --------------------------------------------------------------------------

# A copy of a project in the bench's own folder, fresh from its source.
project_copy() {
	local src=$1 tree=$2 dest
	[ -d "$src" ] || die "no project at $src (set it in scenes.sh or the environment)"
	# A copy a tree: two builds with different importers each rebuild the other's
	# compiled models, and would do it inside every measured window.
	dest="$BENCH_HOME/projects/$(basename "$tree")/$(basename "$src")"
	mkdir -p "$dest"
	# What a run compiled stays in the copy: --update keeps the compiled models, and .rinten/ keeps the record of what each was built from (ModelInputs) - without it every
	# model is stale. Copied over fresh, the editor rebuilt every model of the project inside every measured window: 17 GB allocated, the machine in swap, the numbers worthless.
	rsync -a --update --delete --exclude=/.git --exclude=/.rinten/ "$src/" "$dest/"
	local proj
	proj=$(ls "$dest"/*.rinproj 2>/dev/null | head -1)
	[ -n "$proj" ] || die "no .rinproj in $src"
	echo "$proj"
}

# One run of the game: run_one TREE OUT_BASE PROJECT SCENE EYE CONVARS FRAMES WARM CHECK
run_one() {
	local tree=$1 out=$2 proj=$3 scene=$4 eye=$5 convars=$6 frames=$7 warm=$8 check=$9
	local game="$tree/game"
	[ -x "$game/Rinten" ] || die "no game in $tree - run: bench.sh tree $(basename "$tree")"

	local args=( -project "$proj" -perfscene "$scene" -perfplay -perffullscreen -perfwindow max
		-perfwarmup "$warm" -perfframes "$frames" -perfreport 30 -perfreportfile "$out.txt"
		-perfconvar "$convars" )
	[ -n "$eye" ] && args+=( -perfeye "$eye" )
	# Switches of a run's own, e.g. BENCH_ARGS=-portable for the portable frame.
	[ -n "${BENCH_ARGS:-}" ] && args+=( $BENCH_ARGS )
	[ "$check" = 1 ] && args+=( -vkvalidate -perfwindowshot "$out.ppm" )

	wait_for_memory 6000 "a run"
	ensure_display

	# One measurement on the machine at a time - this lock is shared with the
	# other measuring scripts on this machine.
	mkdir -p "$(dirname "$GPU_LOCK")"
	exec 9>"$GPU_LOCK"
	flock 9

	wait_for_quiet
	local others
	others=$(pgrep -x Rinten | wc -l)

	# Nothing inherited: the saved settings of the last run in this tree go.
	rm -rf "$game/config/convar" "$game/config/video.json"
	rm -f "$out.txt" "$out.ppm"

	( cd "$game" && : > logs/Rinten.log 2>/dev/null
		env -u DISPLAY WAYLAND_DISPLAY="$DISPLAY_NAME" \
			timeout -s TERM -k 15 "${BENCH_LIMIT:-300}" ./Rinten "${args[@]}" ) > "$out.stdout" 2>&1 9>&-
	local code=$?

	flock -u 9
	exec 9>&-

	cp "$game/logs/Rinten.log" "$out.log" 2>/dev/null
	printf 'exit %s\nother Rinten processes %s\ntree %s\nconvars %s\n' "$code" "$others" "$tree" "$convars" > "$out.run"
	[ "$others" -gt 0 ] && say "bench: $others other Rinten process(es) were running - numbers are noisier"
	[ $code = 124 ] && say "bench: $(basename "$out") ran past ${BENCH_LIMIT:-300}s and was stopped"
	return 0
}

# Every chosen scene, into OUT: run_scenes TREE OUT CONVARS FRAMES WARM CHECK scene...
run_scenes() {
	local tree=$1 out=$2 convars=$3 frames=$4 warm=$5 check=$6; shift 6
	mkdir -p "$out"
	local line name dir scene eye proj
	for line in "${SCENES[@]}"; do
		IFS='|' read -r name dir scene eye <<< "$line"
		if [ $# -gt 0 ]; then
			local want=0 s
			for s in "$@"; do [ "$s" = "$name" ] && want=1; done
			[ $want = 1 ] || continue
		fi
		proj=$(project_copy "$dir" "$tree") || exit 1
		say "bench: $name"
		run_one "$tree" "$out/$name" "$proj" "$scene" "$eye" "$convars" "$frames" "$warm" 0
		[ "$check" = 1 ] && run_one "$tree" "$out/$name.check" "$proj" "$scene" "$eye" "$convars" 200 10 1
	done
}

parse_run_options() {
	TREE=main; LABEL=$(date +%m%d-%H%M%S); FRAMES=1500; WARM=20; CONVARS=""; REPS=2; CHECK=1; REST=()
	while [ $# -gt 0 ]; do
		case "$1" in
			-t) TREE=$2; shift 2 ;;
			-l) LABEL=$2; shift 2 ;;
			-f) FRAMES=$2; shift 2 ;;
			-w) WARM=$2; shift 2 ;;
			-c) CONVARS=$2; shift 2 ;;
			-r) REPS=$2; shift 2 ;;
			--no-check) CHECK=0; shift ;;
			-*) die "unknown option $1" ;;
			*) REST+=( "$1" ); shift ;;
		esac
	done
}

# The tables are drawn by BenchReport, built once into the bench's own folder
report_tool() {
	local tool="$BENCH_HOME/benchreport/benchreport"
	if [ ! -x "$tool" ] || [ -n "$(find "$HERE/../BenchReport" -newer "$tool" -name '*.cs*' -print -quit)" ]; then
		dotnet build "$HERE/../BenchReport/BenchReport.csproj" -c Release -nologo -v q -o "$BENCH_HOME/benchreport" >&2 || die "BenchReport did not build"
	fi
	"$tool" "$@"
}

joined() { [ -n "$2" ] && echo "$1;$2" || echo "$1"; }

cmd_run() {
	parse_run_options "$@"
	local tree out
	tree=$(tree_path "$TREE")
	out="$BENCH_HOME/out/$LABEL"
	run_scenes "$tree" "$out" "$(joined "$BASE_CONVARS" "$CONVARS")" "$FRAMES" "$WARM" "$CHECK" "${REST[@]}"
	report_tool report "$out" | tee "$out/summary.txt"
}

cmd_ab() {
	[ $# -ge 2 ] || die "ab A B [options] [scene..]"
	local a=$1 b=$2; shift 2
	parse_run_options "$@"
	local root="$BENCH_HOME/out/$LABEL" side spec tree convars r
	for r in $(seq 1 "$REPS"); do
		for side in A B; do
			[ $side = A ] && spec=$a || spec=$b
			tree=${spec%%@*}; [ -z "$tree" ] && tree=$TREE
			convars=""; [[ "$spec" == *@* ]] && convars=${spec#*@}
			convars=$(joined "$(joined "$BASE_CONVARS" "$CONVARS")" "$convars")
			say "bench: round $r, $side = $spec"
			# The check run once per side, on the first round.
			run_scenes "$(tree_path "$tree")" "$root/$side-$r" "$convars" "$FRAMES" "$WARM" \
				$( [ "$r" = 1 ] && echo "$CHECK" || echo 0 ) "${REST[@]}"
		done
	done
	report_tool ab "$root" "$a" "$b" | tee "$root/summary.txt"
}

case "${1:-}" in
	tree) shift; cmd_tree "$@" ;;
	run) shift; cmd_run "$@" ;;
	ab) shift; cmd_ab "$@" ;;
	report) shift; report_tool report "$@" ;;
	compare) shift; report_tool compare "$@" ;;
	*) sed -n '2,17p' "$0" | sed 's/^# \{0,1\}//'; exit 1 ;;
esac
