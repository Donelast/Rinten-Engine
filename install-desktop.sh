#!/bin/bash
# Puts Rinten into the desktop: a menu entry, an icon in the theme, and the file type that makes a .rinproj open the editor. ./install-desktop.sh              install, or bring an existing install up to date ./install-desktop.sh --status     say what is installed and whether it is current ./install-desktop.sh --remove     take it all out again ./install-desktop.sh --prefix DIR write under DIR/share instead of the user's own Everything lands under $XDG_DATA_HOME (~/.local/share by default), which is where a desktop looks without being told and where an
# unprivileged user is allowed to write. Nothing outside it is touched and no package manager is involved. The build runs this after every Linux build - see Steps/DesktopEntry.cs - so a fresh clone gets an entry the first time it is built and a checkout that moves gets its entry repointed the next time. It is written to be run twice: a file is only rewritten when its contents would change, and the caches are only refreshed when something did, so the ordinary rebuild costs nothing and leaves no mtimes behind. Keep LF line endings or Linux will not execute this.

set -eu

# ── what is being installed ───────────────────────────────────────────────────

# One name, used four ways: the .desktop basename, the icon name in the theme, the Wayland app_id and X11 WM_CLASS the editor sets for itself (CoreWindow.AppId, which lowercases the executable name
# to arrive here), and therefore the string a desktop matches a window to this entry by. They have to agree - a window whose app_id names no .desktop is a window with the wrong icon in the dock.
APP=rinten
MIME=application/x-rinten-project

ICON_SIZES="16 24 32 48 64 128 256 512"

# ── where this copy of the engine is ──────────────────────────────────────────

self="$(cd "$(dirname "$(readlink -f "$0")")" && pwd)"

# Two layouts, the same two the launcher itself knows about (LauncherEnvironment.Init): a source tree
# with the binaries under game/, and a release with the binaries at the top and the content in engine/.
if [ -x "$self/game/Rinten" ]; then
	exe="$self/game/Rinten"
	brand="$self/brand/linux"
elif [ -x "$self/Rinten" ]; then
	exe="$self/Rinten"
	brand="$self/engine/brand/linux"
else
	echo "No Rinten next to $0 - build the engine first (./build.sh)." >&2
	exit 1
fi

workdir="$(dirname "$exe")"

# ── where it is going ─────────────────────────────────────────────────────────

mode=install
prefix=""

while [ $# -gt 0 ]; do
	case "$1" in
		--remove|--uninstall) mode=remove ;;
		--status) mode=status ;;
		--prefix) shift; prefix="${1:-}"; [ -n "$prefix" ] || { echo "--prefix needs a directory" >&2; exit 2; } ;;
		-h|--help) sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
		*) echo "Unknown argument: $1" >&2; exit 2 ;;
	esac
	shift
done

if [ -n "$prefix" ]; then
	data="$prefix/share"
else
	data="${XDG_DATA_HOME:-${HOME:?HOME is not set and no --prefix was given}/.local/share}"
fi

apps="$data/applications"
icons="$data/icons/hicolor"
mimedir="$data/mime"

desktop_file="$apps/$APP.desktop"
mime_file="$mimedir/packages/$APP.xml"

# ── the entry itself ──────────────────────────────────────────────────────────

# Desktop-spec quoting for Exec: the value is one quoted argument, and a backslash, a quote, a backtick
# or a dollar inside it is escaped. Paths with spaces in them are the reason this exists at all.
quote_exec() {
	printf '"%s"' "$(printf '%s' "$1" | sed 's/[\\"`$]/\\&/g')"
}

desktop_entry() {
	# sed drops the TryExec line when there is no TryExec to write - see below.
	sed '/^$/d' <<-ENTRY
		[Desktop Entry]
		Version=1.5
		Type=Application
		Name=Rinten
		GenericName=Game Engine
		Comment=Build and edit games with the Rinten engine
		Exec=$(quote_exec "$exe") %f
		$try_exec
		Path=$workdir
		Icon=$APP
		Terminal=false
		StartupNotify=true
		StartupWMClass=$APP
		Categories=Development;IDE;
		Keywords=game;engine;editor;scene;3d;rinten;
		MimeType=$MIME;
	ENTRY
}

# TryExec is a bare path with no quoting in the spec, so a path with whitespace in it cannot be expressed - and an unparseable TryExec hides the
# entry altogether. Better to have no TryExec than no menu entry; the only thing lost is the entry hiding itself after the engine folder is deleted.
case "$exe" in
	*[[:space:]]*) try_exec="" ;;
	*) try_exec="TryExec=$exe" ;;
esac

mime_package() {
	cat <<-XML
		<?xml version="1.0" encoding="UTF-8"?>
		<mime-info xmlns="http://www.freedesktop.org/standards/shared-mime-info">
		  <mime-type type="$MIME">
		    <comment>Rinten project</comment>
		    <sub-class-of type="application/json"/>
		    <glob pattern="*.rinproj"/>
		  </mime-type>
		</mime-info>
	XML
}

# ── doing it ──────────────────────────────────────────────────────────────────

changed=0

say() { [ "$mode" = status ] || echo "$@"; }

# Every write goes through a temporary file beside its target so a replacement is one rename and never a
# half-written .desktop. Named here so an interrupted run does not leave one behind for a desktop to try and read.
tmpfile=""
cleanup() { [ -z "$tmpfile" ] || rm -f "$tmpfile"; }
trap cleanup EXIT INT TERM

# Writes $2 to the file $1, but only when that would change it. A rebuild that changes nothing must not touch a single mtime: every write here is a file a desktop watches, and a touched file is a cache
# rebuild and a flicker in somebody's menu. The content is an argument and not something piped in, because the right-hand side of a pipe is a subshell and the `changed` it sets there would go nowhere.
write_if_changed() {
	local target="$1" content="$2"
	tmpfile="$(mktemp "$(dirname "$target")/.rinten.XXXXXX")" || return 1
	printf '%s\n' "$content" >"$tmpfile"

	if [ -f "$target" ] && cmp -s "$tmpfile" "$target"; then
		rm -f "$tmpfile"
		tmpfile=""
		return 0
	fi

	chmod 0644 "$tmpfile"
	mv -f "$tmpfile" "$target"
	tmpfile=""
	changed=1
	say "  wrote $target"
}

copy_if_changed() {
	local source="$1" target="$2"
	if [ -f "$target" ] && cmp -s "$source" "$target"; then
		return 0
	fi
	install -m 0644 "$source" "$target"
	changed=1
	say "  wrote $target"
}

remove_if_present() {
	[ -e "$1" ] || return 0
	rm -f "$1"
	changed=1
	say "  removed $1"
}

install_all() {
	mkdir -p "$apps" "$mimedir/packages"

	write_if_changed "$desktop_file" "$(desktop_entry)"
	write_if_changed "$mime_file" "$(mime_package)"

	# The icon goes in under two names: the application's, which the entry's Icon= key names, and the one a file
	# manager looks for when it wants a picture for a .rinproj (the mime type with its slash turned into a dash).
	for size in $ICON_SIZES; do
		[ -f "$brand/$APP-$size.png" ] || continue
		mkdir -p "$icons/${size}x${size}/apps" "$icons/${size}x${size}/mimetypes"
		copy_if_changed "$brand/$APP-$size.png" "$icons/${size}x${size}/apps/$APP.png"
		copy_if_changed "$brand/$APP-$size.png" \
			"$icons/${size}x${size}/mimetypes/$(echo "$MIME" | tr / -).png"
	done

	if [ -f "$brand/$APP.svg" ]; then
		mkdir -p "$icons/scalable/apps" "$icons/scalable/mimetypes"
		copy_if_changed "$brand/$APP.svg" "$icons/scalable/apps/$APP.svg"
		copy_if_changed "$brand/$APP.svg" \
			"$icons/scalable/mimetypes/$(echo "$MIME" | tr / -).svg"
	fi
}

remove_all() {
	remove_if_present "$desktop_file"
	remove_if_present "$mime_file"

	for size in $ICON_SIZES; do
		remove_if_present "$icons/${size}x${size}/apps/$APP.png"
		remove_if_present "$icons/${size}x${size}/mimetypes/$(echo "$MIME" | tr / -).png"
	done

	remove_if_present "$icons/scalable/apps/$APP.svg"
	remove_if_present "$icons/scalable/mimetypes/$(echo "$MIME" | tr / -).svg"
}

# The caches. Each of these is a program that may not be installed - a desktop without GTK has no gtk-update-icon-cache - and none of them
# failing is worth an error, because the files are in place either way and every desktop rescans these directories on its own eventually.
refresh() {
	[ "$changed" = 1 ] || return 0

	have() { command -v "$1" >/dev/null 2>&1; }

	have update-desktop-database && update-desktop-database "$apps" 2>/dev/null || true
	have update-mime-database && update-mime-database "$mimedir" 2>/dev/null || true

	# Only where the theme has an index. gtk-update-icon-cache refuses to write a cache for a directory that is not a theme, and a hicolor under the user's
	# own data directory usually has no index.theme of its own - GTK reads it uncached, which is exactly as correct and one fewer thing to get stale.
	if [ -f "$icons/index.theme" ] && have gtk-update-icon-cache; then
		gtk-update-icon-cache -q -t -f "$icons" 2>/dev/null || true
	fi
}

# The default handler for a .rinproj, but only where the desktop has no answer of its own yet. Somebody who
# pointed .rinproj at their editor of choice meant it, and a rebuild is not the moment to overrule them.
claim_mime_default() {
	# A per-user default belongs to the user, not to a prefix. --prefix writes a system-wide install,
	# where the default handler is whatever the desktop works out from the entries it can see.
	[ -z "$prefix" ] || return 0

	command -v xdg-mime >/dev/null 2>&1 || return 0

	local current
	current="$(xdg-mime query default "$MIME" 2>/dev/null || true)"

	[ -z "$current" ] || return 0

	xdg-mime default "$APP.desktop" "$MIME" 2>/dev/null || true
	say "  $MIME opens with $APP.desktop"
}

validate() {
	command -v desktop-file-validate >/dev/null 2>&1 || return 0
	desktop-file-validate "$desktop_file" || echo "  (the entry was written anyway)" >&2
}

# A .NET that only the shell knows about. game/Rinten is an apphost, and it finds the runtime through /etc/dotnet/install_location or an install under /usr/share/dotnet or /usr/lib/dotnet - and failing those, through $DOTNET_ROOT. A desktop starts a .desktop from the session, which has never read a shell profile, so a .NET put there
# by mise, asdf or a tarball is one the menu entry cannot see even though the build in the terminal worked perfectly. Said out loud rather than written into Exec=: a runtime path baked into the entry is a path that goes stale the next time the toolchain moves, and the machine-wide answer below is the one the host itself documents.
check_runtime() {
	for location in /etc/dotnet/install_location /etc/dotnet/install_location_* \
		/usr/share/dotnet/host/fxr /usr/lib/dotnet/host/fxr; do
		if [ -e "$location" ]; then return 0; fi
	done

	echo
	echo "Warning: no machine-wide .NET runtime is installed."

	if [ -n "${DOTNET_ROOT:-}" ]; then
		echo "  This shell has DOTNET_ROOT=$DOTNET_ROOT, which a desktop session"
		echo "  does not - started from the menu, the editor will say .NET is missing."
		echo "  Tell the host about it once, for every session:"
		echo
		echo "    sudo mkdir -p /etc/dotnet"
		echo "    echo '$DOTNET_ROOT' | sudo tee /etc/dotnet/install_location"
	else
		echo "  The entry will not start the editor until one is."
	fi

	echo
}

case "$mode" in
	install)
		echo "Installing the Rinten desktop entry for $exe"
		install_all
		refresh
		claim_mime_default
		validate
		check_runtime
		if [ "$changed" = 1 ]; then
			echo "Done. It may take a moment to appear in the menu."
		else
			echo "Already up to date."
		fi
		;;

	remove)
		echo "Removing the Rinten desktop entry"
		remove_all
		refresh
		[ "$changed" = 1 ] || echo "Nothing was installed."
		;;

	status)
		echo "engine   $exe"
		echo "icons    $brand"
		echo "data     $data"
		if [ -f "$desktop_file" ]; then
			installed="$(sed -n 's/^Exec=//p' "$desktop_file" | head -n 1)"
			echo "entry    $desktop_file"
			echo "         $installed"
			if [ "$installed" = "$(quote_exec "$exe") %f" ]; then
				echo "         points at this engine"
			else
				echo "         points somewhere else - run $0 to repoint it"
			fi
		else
			echo "entry    not installed"
		fi
		handler="$(xdg-mime query default "$MIME" 2>/dev/null || true)"
		echo "handler  ${handler:-none}"
		;;
esac
