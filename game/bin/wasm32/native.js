// What the page provides that a wasm module cannot get for itself.
//
// This used to be the binding between the engine and the core: the renderer was
// C++ compiled to a second wasm module and every draw the engine made crossed
// here as a ccall. It is not any more - the browser backend is C# in the engine
// and reaches WebGPU through webgpu.js. What is left is the page itself: the
// canvas and its size, the pointer and the keys, the loading text, and the
// files a build was shipped with, which are fetched before the first frame
// because the engine reads content synchronously and a page cannot.
//
// Bound by index.html as globalThis.RintenNative before .NET starts.

function RintenNativeBind(host) {
	var canvas = (host && host.canvas) || document.getElementById("canvas");

	var pointerX = 0;
	var pointerY = 0;
	var buttons = 0;
	// Buttons that went down since the engine last asked, kept until it has seen
	// them: a tap on a touch screen is often down and up again between two
	// frames, and a mask read once a frame never saw it at all.
	var pressed = 0;
	var deltaX = 0;
	var deltaY = 0;

	// What the last fetchBytes brought back, handed over once and dropped.
	var fetched = null;

	// The canvas's size in the screen's own pixels, which is what it is drawn at.
	//
	// clientWidth is the CSS width rounded to a whole CSS pixel, and that times a
	// fractional ratio - 1.25, 1.5, a zoomed page - floored, came out a pixel or
	// two short of the box the canvas is shown in. The browser then stretched
	// every frame by that fraction to fit: a resample of the whole picture, which
	// softens it and makes every edge crawl as the camera moves. The box's size in
	// device pixels is what the browser itself lays the canvas out in, and a
	// ResizeObserver asked for it hands over exactly that; where it has not got
	// it (Safari) the fractional CSS size times the ratio, rounded, is the
	// nearest guess.
	var devicePixels = null;

	if (canvas && typeof ResizeObserver !== "undefined") {
		try {
			new ResizeObserver(function (entries) {
				var entry = entries[entries.length - 1];
				var box = entry.devicePixelContentBoxSize && entry.devicePixelContentBoxSize[0];
				devicePixels = box ? { w: box.inlineSize, h: box.blockSize } : null;
			}).observe(canvas, { box: "device-pixel-content-box" });
		} catch (err) {
			devicePixels = null;
		}
	}

	function canvasCssSize() {
		if (!canvas) return { w: 1280, h: 720 };

		if (devicePixels && devicePixels.w > 0 && devicePixels.h > 0)
			return { w: devicePixels.w, h: devicePixels.h };

		var dpr = window.devicePixelRatio || 1;
		var rect = canvas.getBoundingClientRect();
		return {
			w: Math.max(1, Math.round((rect.width || 1280) * dpr)),
			h: Math.max(1, Math.round((rect.height || 720) * dpr))
		};
	}

	function fitCanvas() {
		var size = canvasCssSize();
		if (!canvas) return size;
		if (canvas.width !== size.w) canvas.width = size.w;
		if (canvas.height !== size.h) canvas.height = size.h;
		return size;
	}

	// What a build was shipped with, downloaded before the first frame and
	// handed to the engine one file at a time.
	// One file of the build: as it is, or - marked "+" - packed beside its name as
	// .gz and opened here as it arrives (WebBuild.Packed). A host that unpacks it on its
	// own (Content-Encoding) hands it over open. Null where it would not come.
	function fetchFile(path) {
		var packed = path.charAt(0) === "+";
		var url = packed ? path.slice(1) + ".gz" : path;

		return fetch(url).then(function (r) {
			if (!r.ok) return null;
			if (!packed || !r.body || (r.headers.get("Content-Encoding") || "") !== "") return r.arrayBuffer();
			return new Response(r.body.pipeThrough(new DecompressionStream("gzip"))).arrayBuffer();
		}).catch(function () {
			return null;
		});
	}

	var prefetched = null;
	var prefetchDone = 0;
	var prefetchTotal = 0;

	var keys = Object.create(null);

	// Moves whenever any key goes down or up, so the engine can ask one number a
	// frame instead of every key it knows - see CoreInput.PumpWeb.
	var keyVersion = 0;
	var wantsPointerLock = false;

	// Keys a page would otherwise act on itself: arrows and space scroll, tab
	// walks the focus out of the canvas and into whatever is around it - which
	// on itch.io is the page the game is embedded in.
	var swallowed = {
		ArrowUp: 1, ArrowDown: 1, ArrowLeft: 1, ArrowRight: 1,
		Space: 1, Tab: 1, Backspace: 1, F1: 1, F5: 1, Slash: 1, Quote: 1
	};

	function lockPointer(target) {
		if (!target || !target.requestPointerLock) return;
		if (document.pointerLockElement === target) return;
		try { target.requestPointerLock(); } catch (err) {}
	}

	// Full screen.
	//
	// A browser only allows it from a gesture, so a game that asks for it outside
	// one - from its own update, a second after the click that chose it in a menu -
	// is refused. The wish is remembered instead and taken on the next click or
	// key, which is what a player does next anyway. Escape leaves it, and the
	// browser tells nobody it is going to, so the wish is dropped when it does or
	// the next click would put the game straight back.
	//
	// The page itself goes full screen rather than the canvas: the canvas already
	// fills it, and everything else the page draws over the game - the loading
	// text, the error line, the button below - stays where it is. A canvas on its
	// own would have hidden them.
	var wantsFullscreen = false;

	function fullscreenRoot() {
		return document.documentElement || document.body;
	}

	function fullscreenElement() {
		return document.fullscreenElement || document.webkitFullscreenElement || null;
	}

	function fullscreenAllowed() {
		var root = fullscreenRoot();
		if (!root) return false;
		if (document.fullscreenEnabled === false) return false;
		return !!(root.requestFullscreen || root.webkitRequestFullscreen);
	}

	function enterFullscreen() {
		var root = fullscreenRoot();
		var ask = root && (root.requestFullscreen || root.webkitRequestFullscreen);
		if (!ask) return;

		try {
			// The address bar and the like folded away where a browser offers it;
			// the option is ignored where it does not.
			var going = ask.call(root, { navigationUI: "hide" });
			if (going && going.catch) going.catch(function () {});
		} catch (err) {}
	}

	function leaveFullscreen() {
		var drop = document.exitFullscreen || document.webkitExitFullscreen;
		if (!drop) return;

		try {
			var going = drop.call(document);
			if (going && going.catch) going.catch(function () {});
		} catch (err) {}
	}

	// The wish, taken now that there is a gesture to take it with.
	function settleFullscreen() {
		if (wantsFullscreen && !fullscreenElement()) enterFullscreen();
	}

	function fullscreenChanged() {
		wantsFullscreen = !!fullscreenElement();
		fitCanvas();
	}

	document.addEventListener("fullscreenchange", fullscreenChanged);
	document.addEventListener("webkitfullscreenchange", fullscreenChanged);

	function bindInput(target) {
		if (!target || target._rintenInput) return;
		target._rintenInput = true;
		// Where the pointer is, in the canvas's own pixels. Taken on a press and a
		// release as well as a move: a finger arrives with no move before it, and
		// the press used to be read where the last touch had been.
		function place(e) {
			var box = target.getBoundingClientRect();
			if (box.width <= 0 || box.height <= 0) return;
			pointerX = (e.clientX - box.left) / box.width * target.width;
			pointerY = (e.clientY - box.top) / box.height * target.height;
		}
		target.addEventListener("pointermove", function (e) {
			if (document.pointerLockElement === target) {
				deltaX += e.movementX;
				deltaY += e.movementY;
				return;
			}
			place(e);
		});
		target.addEventListener("pointerdown", function (e) {
			if (document.pointerLockElement !== target) place(e);
			buttons |= (1 << e.button);
			pressed |= (1 << e.button);
			try { target.setPointerCapture(e.pointerId); } catch (err) {}
		});
		target.addEventListener("click", function () {
			target.focus();
			if (wantsPointerLock) lockPointer(target);
			settleFullscreen();
		});
		function release(e) {
			if (document.pointerLockElement !== target) place(e);
			buttons &= ~(1 << e.button);
			try {
				if (target.hasPointerCapture && target.hasPointerCapture(e.pointerId))
					target.releasePointerCapture(e.pointerId);
			} catch (err) {}
		}
		target.addEventListener("pointerup", release);
		// A touch the browser took back - a system gesture, the page losing the
		// finger - ends without a pointerup, and the button stayed down for ever.
		target.addEventListener("pointercancel", function (e) {
			buttons &= ~(1 << Math.max(e.button, 0));
		});
		window.addEventListener("keydown", function (e) {
			if (!keys[e.code]) keyVersion++;
			keys[e.code] = 1;
			if (swallowed[e.code] && !e.ctrlKey && !e.metaKey) e.preventDefault();
			settleFullscreen();
		});
		window.addEventListener("blur", function () { keys = Object.create(null); buttons = 0; pressed = 0; keyVersion++; });
		window.addEventListener("keyup", function (e) {
			keys[e.code] = 0;
			keyVersion++;
		});
	}

	bindInput(canvas);
	fitCanvas();

	// The game's loop: the engine's frame called straight from here, once a turn
	// of the page's event loop.
	//
	// It used to be a C# loop awaiting a promise between frames, and that is two
	// hops, not one: the promise resolves in the page's task, and the .NET
	// runtime then runs the continuation from a timer of its own - which the
	// browser holds to four milliseconds whenever it decides the timers are
	// nested. So a frame of one millisecond ran at 245 a second or at 490,
	// depending on the run. Called from here there is no timer anywhere.
	//
	// The body answers what to wait for next: 0 stop, 1 nothing (vsync off - a
	// message to the page itself, which hands the thread back for one task, long
	// enough for input and fetches, and is not stretched the way setTimeout( 0 )
	// is), 2 the display (vsync on, or a headset driving - its own animation
	// frame calls the engine and this only keeps turning). A hidden page always
	// waits for the display, which the browser stops for a hidden tab; an
	// uncapped game in a background tab would otherwise burn a core for nothing.
	var loopBody = null;
	var loopDone = null;
	var channel = typeof MessageChannel !== "undefined" ? new MessageChannel() : null;

	function loopStep() {
		if (!loopBody) return;

		var next = 0;
		try { next = loopBody(); } catch (err) { console.error(err); next = 0; }

		if (!next) {
			loopBody = null;
			if (loopDone) loopDone();
			return;
		}

		if (next === 1 && channel && !document.hidden) channel.port2.postMessage(0);
		else requestAnimationFrame(loopStep);
	}

	if (channel) channel.port1.onmessage = loopStep;

	function runLoop(body) {
		return new Promise(function (resolve) {
			loopBody = body;
			loopDone = resolve;
			requestAnimationFrame(loopStep);
		});
	}

	return {
		canvas: canvas,
		whenReady: function () { return globalThis.nativeReady || Promise.resolve(); },
		canvasWidth: function () { return (canvas && canvas.width) || 1280; },
		canvasHeight: function () { return (canvas && canvas.height) || 720; },
		canvasCssWidth: function () { return canvasCssSize().w; },
		canvasCssHeight: function () { return canvasCssSize().h; },
		fitCanvas: function () { var size = fitCanvas(); return size.w; },
		pointerX: function () { return pointerX; },
		pointerY: function () { return pointerY; },
		pointerDeltaX: function () { var v = deltaX; deltaX = 0; return v; },
		pointerDeltaY: function () { var v = deltaY; deltaY = 0; return v; },
		pointerLocked: function () { return document.pointerLockElement === canvas ? 1 : 0; },
		setPointerLock: function (state) {
			wantsPointerLock = !!state;
			if (wantsPointerLock) lockPointer(canvas);
			else if (document.pointerLockElement === canvas) document.exitPointerLock();
		},
		fullscreen: function () { return fullscreenElement() ? 1 : 0; },
		fullscreenAllowed: function () { return fullscreenAllowed() ? 1 : 0; },
		setFullscreen: function (state) {
			wantsFullscreen = !!state;

			if (!fullscreenAllowed()) return;
			if (wantsFullscreen) enterFullscreen();
			else if (fullscreenElement()) leaveFullscreen();
		},
		// Held, or pressed since the last ask - which is the engine's frame - so a
		// press is seen for a frame even when it was already let go.
		buttons: function () { var v = buttons | pressed; pressed = 0; return v; },
		keyDown: function (code) { return keys[code] ? 1 : 0; },
		keyVersion: function () { return keyVersion; },
		nextFrame: function () {
			return new Promise(function (resolve) { requestAnimationFrame(function () { resolve(); }); });
		},
		runLoop: runLoop,
		say: function (text) {
			if (typeof globalThis.rintenSay === "function") globalThis.rintenSay(text);
		},
		progress: function (done, total) {
			if (typeof globalThis.rintenProgress === "function") globalThis.rintenProgress(done, total);
		},
		setTitle: function (text) {
			if (text) document.title = text;
		},
		fetchText: function (url) {
			return fetch(url).then(function (r) {
				if (!r.ok) return "";
				return r.text();
			}).catch(function () { return ""; });
		},
		fetchBytes: function (url) {
			return fetchFile(url).then(function (buf) {
				fetched = buf ? new Uint8Array(buf) : null;
				return fetched ? fetched.length : 0;
			});
		},
		takeFetch: function () {
			var data = fetched;
			fetched = null;
			return data || new Uint8Array(0);
		},
		// The same, written into the module's own memory where .NET asks - a view of a
		// buffer it holds outside its heap.
		takeFetchInto: function (view) {
			if (fetched) view.set(fetched, 0);
			fetched = null;
		},
		prefetch: function (listText, concurrency) {
			var paths = String(listText || "").split("\n")
				.map(function (line) { return line.trim(); })
				.filter(function (line) { return line && line[0] !== "#"; });

			prefetched = new Map();
			prefetchDone = 0;
			prefetchTotal = paths.length;

			var next = 0;

			// One fetch at a time is one round trip at a time, and a game is
			// hundreds of files - so a handful of them run side by side, which
			// is what a browser will keep in flight to one host anyway.
			function pump() {
				if (next >= paths.length) return Promise.resolve();

				var path = paths[next++];
				return fetchFile(path).then(function (buf) {
					if (buf) prefetched.set(path, new Uint8Array(buf));
					prefetchDone++;
					if (typeof globalThis.rintenProgress === "function")
						globalThis.rintenProgress(prefetchDone, prefetchTotal);
					return pump();
				});
			}

			var workers = Math.max(1, Math.min(concurrency | 0 || 8, 32));
			var running = [];
			for (var i = 0; i < workers; i++) running.push(pump());

			return Promise.all(running).then(function () { return prefetched.size; });
		},
		fileLength: function (path) {
			var data = prefetched && prefetched.get(path);
			return data ? data.length : 0;
		},
		takeFileInto: function (path, view) {
			var data = prefetched && prefetched.get(path);
			if (data) view.set(data, 0);
			if (prefetched) prefetched.delete(path);
		},
		takeFile: function (path) {
			if (!prefetched) return new Uint8Array(0);
			var data = prefetched.get(path);
			prefetched.delete(path);
			return data || new Uint8Array(0);
		},
		assetManifest: function () {
			return fetch("web-assets.txt", { cache: "no-cache" }).then(function (r) {
				if (!r.ok) return "";
				return r.text();
			}).catch(function () { return ""; });
		}
	};
}
