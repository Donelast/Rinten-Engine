// The headset, as a page reaches it: WebXR.
//
// The engine's half of this is CoreWebXr; this is the page's. What lives here
// is everything only the browser can do - asking whether there is a headset,
// opening the session when the player clicks, reading the poses out of each
// animation frame, and calling the engine's frame from inside it.
//
// Three things about a session decide the shape of all of it:
//
//   A session is only opened during a gesture. So a wish made outside one -
//   a game calling VRInput.Enter from its own menu, a second after the click
//   that chose it - is remembered and taken on the next click or key, which is
//   the bargain full screen already makes in native.js.
//
//   The poses, and the layer that may be drawn into, are good only while the
//   session's animation-frame callback runs. Not one microtask later. So while
//   there is a session the page drives the engine rather than the other way
//   round: the callback calls the frame the engine handed us, the whole tick
//   happens inside it, and the launcher's own loop stands aside. See
//   CoreWebXr.Frame and Program.Main.
//
//   What we are given to draw into is one framebuffer with a viewport an eye,
//   not two pictures. So an eye is drawn into a view of the engine's, at that
//   viewport's size, and blitted into the viewport at the end - see xrTarget in
//   webgl2.js, which hands out the handle a blit into an eye names.
//
// WebGL2 only. WebGPU's binding to WebXR is not in any headset's browser worth
// shipping to yet, so webgpu.js says it cannot and index.html picks WebGL2 for
// a game whose manifest says it is a VR game.
//
// Bound by index.html as globalThis.RintenXr before .NET starts.

function RintenXrBind() {
	"use strict";

	// -1 until the browser has answered, which it does through a promise.
	var supported = -1;

	var session = null;
	var space = null;
	var layerReady = false;

	// This animation frame's: the frame itself, the pose read from it, and the
	// views that pose holds. All three are dropped the moment the callback ends,
	// because that is when the browser stops answering for them.
	var pose = null;

	var wanted = false;
	var frameBody = null;

	// Whether anything in the engine has asked for a finger lately - see
	// writeJoints. True until the engine says otherwise, so a game that asks on
	// its first frame is answered on its first frame.
	var fingersWanted = true;
	var message = "";
	var ending = false;

	// What the session managed before it stopped. A headset that opens and shuts
	// again says nothing a player can read - the console is not reachable with one
	// on - so this is kept and handed back through message().
	var framesSeen = 0;
	var posesSeen = 0;
	var lastFail = "";

	function renderer() {
		return globalThis.RintenRender;
	}

	// Whether the renderer the page chose can draw to a headset at all.
	function rendererCan() {
		var r = renderer();
		return !!(r && r.xrBind && r.xrCan && r.xrCan());
	}

	if (navigator.xr && navigator.xr.isSessionSupported) {
		navigator.xr.isSessionSupported("immersive-vr").then(function (yes) {
			supported = yes ? 1 : 0;
			if (!yes) message = "this browser has no immersive VR";
		}).catch(function (e) {
			// An iframe without xr-spatial-tracking answers by throwing, and that
			// is the one failure a player can do something about: itch.io and the
			// like embed a game in one, and the game opens in its own tab.
			supported = 0;
			message = "the page this game is embedded in does not allow VR (" +
				String((e && e.message) || e) + ") - open it in its own tab";
		});
	} else {
		supported = 0;
		message = "this browser has no WebXR";
	}

	function fail(text) {
		message = text;
		lastFail = text;
		if (globalThis.console) console.warn("WebXR: " + text);
	}

	// --- the session ----------------------------------------------------------

	function open() {
		if (session || supported !== 1) return;

		if (!rendererCan()) {
			fail("this page is drawing with WebGPU, which cannot go in a headset - add ?webgl to the address");
			return;
		}

		var asked;

		try {
			// Called straight out of the gesture, before any await: a browser
			// checks for the activation at the call and not at the await.
			// "layers" only where the renderer will draw into a projection layer -
			// see xrLayersWanted in webgl2.js. A session that has them refuses a
			// baseLayer ("can't use baseLayer"), which is how asking for them once
			// turned every entry on a Quest into an InvalidStateError; the renderer
			// now puts whatever it draws through into the layers list instead. The
			// projection layer is what lets the engine draw both eyes into the
			// headset's own texture, with its foveation, rather than copying them in.
			var features = ["local-floor", "bounded-floor", "hand-tracking"];
			var r = globalThis.RintenRender;
			if (r && r.xrLayersWanted && r.xrLayersWanted()) features.push("layers");

			asked = navigator.xr.requestSession("immersive-vr", { optionalFeatures: features });
		} catch (e) {
			fail(String((e && e.message) || e));
			return;
		}

		asked.then(begin).catch(function (e) {
			var text = String((e && e.message) || e);

			// No activation: the wish stands and the next click takes it.
			// Anything else - no permission, no device - is a no, and asking
			// again on every click would be a warning on every click.
			wanted = /gesture|activation|user/i.test(text);

			fail(text);
		});
	}

	function begin(opened) {
		session = opened;
		wanted = false;
		ending = false;
		message = "";
		lastFail = "";

		session.addEventListener("end", function () {
			session = null;
			space = null;
			pose = null;
			layerReady = false;
			var r = renderer();
			if (r && r.xrDrop) r.xrDrop();

			// The whole point of the counters: "it went black and came back" and
			// "it drew nothing" and "the layer never came" look identical from
			// inside a headset, and this is the one line that tells them apart.
			// The reason first, the counters after. A line on a screen is read
			// from the left and cut off on the right, and the reason is the part
			// worth reading - putting the counters in front of it hid it.
			message = (lastFail ? lastFail : "no error was reported") +
				" | closed after " + framesSeen + " frames, " + posesSeen + " with a pose";

			framesSeen = 0;
			posesSeen = 0;
			changed();
		});

		renderer().xrBind(session).then(function (ok) {
			if (!ok) {
				// The renderer's own account, which is the only one worth reading.
				// It is asked for by name: what the renderer calls fail() goes to
				// its GL error counter and never reaches here, so a generic line
				// was all this could ever show.
				var r = renderer();
				var said = r && r.xrError ? r.xrError() : "";

				fail(said || "the renderer would not make the headset's layer");
				leave();
				return;
			}

			// Which way the eyes reach the headset, said once - the difference between
			// the two is a full-screen copy per eye and whether foveation thins the
			// shading at all, so a number read off a headset means nothing without it.
			var r2 = renderer();
			if (globalThis.console) console.log("WebXR: " + (r2 && r2.xrLayered && r2.xrLayered()
				? "drawing into the projection layer - no copy, foveation on the shading"
				: "drawing through an XRWebGLLayer - each eye copied in"));

			layerReady = true;
			changed();
			return reference();
		}).then(function () {
			if (session) session.requestAnimationFrame(frame);
		}).catch(function (e) {
			fail(String((e && e.message) || e));
			leave();
		});
	}

	// The floor where the headset knows where it is, and the headset's own
	// starting place where it does not - the same choice CoreXr makes between
	// stage and local.
	function reference() {
		return session.requestReferenceSpace("local-floor").catch(function () {
			return session.requestReferenceSpace("local");
		}).then(function (made) {
			space = made;
		});
	}

	// The page's own way in, shown again once a session ends - see index.html.
	function changed() {
		if (typeof globalThis.rintenVrChanged === "function") globalThis.rintenVrChanged();
	}

	function leave() {
		if (!session || ending) return;
		ending = true;
		try { session.end(); } catch (e) {}
	}

	// --- the frame ------------------------------------------------------------

	function frame(time, xrFrame) {
		if (!session) return;

		session.requestAnimationFrame(frame);

		framesSeen++;

		pose = space ? xrFrame.getViewerPose(space) : null;
		views = ordered(pose);
		inputs = xrFrame;

		if (views) posesSeen++;

		var r = renderer();
		if (r && r.xrViews) r.xrViews(views);

		if (frameBody) {
			try {
				frameBody();
			} catch (e) {
				if (globalThis.console) console.error(e);
			}
		}

		pose = null;
		views = null;
		inputs = null;
	}

	// The frame being drawn, for the input sources' poses - the same object as
	// the one the callback was handed, kept only for as long as it runs.
	var inputs = null;

	// This frame's two views, left first. A pose lists them in whatever order
	// it likes - the spec says nothing about which comes first - and everything
	// downstream of here, the engine included, counts eye nought as the left.
	var views = null;

	function ordered(from) {
		if (!from || !from.views || from.views.length < 2) return null;

		var left = null;
		var right = null;

		for (var i = 0; i < from.views.length; i++) {
			if (from.views[i].eye === "right") right = right || from.views[i];
			else left = left || from.views[i];
		}

		return left && right ? [left, right] : [from.views[0], from.views[1]];
	}

	// --- what the engine reads once a frame -----------------------------------
	//
	// One array, filled in one call. The offsets are CoreWebXr.Frame's; change
	// one and change the other.
	//
	// Filled into an array of our own and copied in at the end, rather than
	// written through what the engine hands us. A Span<double> arrives here as
	// .NET's MemoryView, which is a window onto the wasm heap with set() and
	// slice() and nothing else - assigning to into[0] puts a property on a
	// JavaScript object and writes no memory at all, which is a headset that
	// tracks nothing and says nothing about why.

	var ACTIVE = 0;
	var REFRESH = 1;
	var HEADFLAGS = 2;
	var HEADPOSE = 3;
	var EYEAT = 10;
	var EYEWORDS = 11;
	var SIZEAT = 32;
	var HANDAT = 36;
	var HANDWORDS = 21;
	var JOINTAT = 78;
	var JOINTS = 26;

	// The order OpenXR names a hand's joints in, which is the order the engine
	// reads them in. WebXR has no palm: the wrist is asked for in its place and
	// the palm is then made the way OpenXR defines it - see palmOf.
	var JointNames = [
		"wrist",
		"wrist",
		"thumb-metacarpal", "thumb-phalanx-proximal", "thumb-phalanx-distal", "thumb-tip",
		"index-finger-metacarpal", "index-finger-phalanx-proximal", "index-finger-phalanx-intermediate", "index-finger-phalanx-distal", "index-finger-tip",
		"middle-finger-metacarpal", "middle-finger-phalanx-proximal", "middle-finger-phalanx-intermediate", "middle-finger-phalanx-distal", "middle-finger-tip",
		"ring-finger-metacarpal", "ring-finger-phalanx-proximal", "ring-finger-phalanx-intermediate", "ring-finger-phalanx-distal", "ring-finger-tip",
		"pinky-finger-metacarpal", "pinky-finger-phalanx-proximal", "pinky-finger-phalanx-intermediate", "pinky-finger-phalanx-distal", "pinky-finger-tip"
	];

	// A profile's number is its place in XrControllerModels.WebProfiles, which
	// names the model a hand is drawn with and what the info command calls it -
	// the two lists must say the same names in the same order. A source's list
	// is most particular first, so the first this knows is the nearest.
	var ProfileCodes = {
		"meta-quest-touch-plus": 1, "meta-quest-touch-pro": 2,
		"oculus-touch-v3": 3, "oculus-touch-v2": 4, "oculus-touch": 5,
		"valve-index": 6,
		"htc-vive": 7, "htc-vive-cosmos": 8, "htc-vive-focus-3": 9,
		"microsoft-mixed-reality": 10, "samsung-odyssey": 11, "hp-mixed-reality": 12,
		"pico-4": 13, "pico-neo3": 14,
		"generic-trigger": 15, "generic-trigger-squeeze": 16,
		"generic-trigger-squeeze-thumbstick": 17, "generic-trigger-touchpad": 18,
		"generic-hand-select": 19, "generic-hand": 20
	};

	function writePose(into, at, transform) {
		var p = transform.position;
		var q = transform.orientation;
		into[at] = p.x; into[at + 1] = p.y; into[at + 2] = p.z;
		into[at + 3] = q.x; into[at + 4] = q.y; into[at + 5] = q.z; into[at + 6] = q.w;
	}

	// The four angles of an eye's field, out of its projection. A headset's are
	// not the same either side, which is exactly what an off-centre projection
	// says: the near plane's edges over the near distance are the tangents.
	function writeField(into, at, m) {
		into[at] = Math.atan((m[8] - 1) / m[0]);
		into[at + 1] = Math.atan((m[8] + 1) / m[0]);
		into[at + 2] = Math.atan((m[9] + 1) / m[5]);
		into[at + 3] = Math.atan((m[9] - 1) / m[5]);
	}

	function writeHand(into, at, source) {
		into[at] = 0;
		into[at + 15] = 0;
		into[at + 16] = 0;
		into[at + 17] = 0;
		into[at + 18] = 0;
		into[at + 19] = 0;
		into[at + 20] = 0;

		if (!source || !inputs) return;

		var flags = 0;

		if (source.gripSpace) {
			var grip = inputs.getPose(source.gripSpace, space);
			if (grip) { writePose(into, at + 1, grip.transform); flags |= 1; }
		}

		if (source.targetRaySpace) {
			var aim = inputs.getPose(source.targetRaySpace, space);
			if (aim) { writePose(into, at + 8, aim.transform); flags |= 2; }
		}

		var pad = source.gamepad;

		if (pad) {
			flags |= 4;

			var buttons = pad.buttons || [];
			var axes = pad.axes || [];

			into[at + 15] = buttons[0] ? buttons[0].value : 0;
			into[at + 16] = buttons[1] ? buttons[1].value : 0;

			// xr-standard puts the stick on the second pair; a controller with a
			// touchpad alone has only the first.
			into[at + 17] = axes.length > 2 ? axes[2] : (axes[0] || 0);
			into[at + 18] = axes.length > 3 ? axes[3] : (axes[1] || 0);

			var down = 0;
			if (buttons[3] && buttons[3].pressed) down |= 1;
			if (buttons[4] && buttons[4].pressed) down |= 2;
			if (buttons[5] && buttons[5].pressed) down |= 4;
			into[at + 19] = down;
		}

		var profiles = source.profiles || [];
		for (var i = 0; i < profiles.length; i++) {
			var code = ProfileCodes[profiles[i]];
			if (code) { into[at + 20] = code; break; }
		}

		if (source.hand) flags |= 8;

		into[at] = flags;
	}

	// The joint spaces of a hand, looked up once and kept: XRHand.get is a map
	// read and the names never change, so asking twenty-six times a frame for
	// the same twenty-six objects is work with nowhere to go.
	var handSpaces = new WeakMap();

	function spacesOf(hand) {
		var found = handSpaces.get(hand);
		if (found) return found;

		found = [];

		for (var i = 0; i < JOINTS; i++) {
			var one = hand.get(JointNames[i]);
			if (!one) return null;
			found.push(one);
		}

		handSpaces.set(hand, found);
		return found;
	}

	// Room for one hand's matrices, filled in one call - see below.
	var jointMatrices = new Float32Array(JOINTS * 16);

	// A rigid 4x4, column major, as a place and a turn. The quaternion is taken
	// from whichever diagonal term is largest, which is the form that does not
	// lose its precision near a half turn.
	function writeMatrix(into, at, m, from) {
		into[at] = m[from + 12];
		into[at + 1] = m[from + 13];
		into[at + 2] = m[from + 14];

		var m00 = m[from], m01 = m[from + 4], m02 = m[from + 8];
		var m10 = m[from + 1], m11 = m[from + 5], m12 = m[from + 9];
		var m20 = m[from + 2], m21 = m[from + 6], m22 = m[from + 10];

		var trace = m00 + m11 + m22;
		var x, y, z, w, s;

		if (trace > 0) {
			s = Math.sqrt(trace + 1) * 2;
			w = 0.25 * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s;
		} else if (m00 > m11 && m00 > m22) {
			s = Math.sqrt(1 + m00 - m11 - m22) * 2;
			w = (m21 - m12) / s; x = 0.25 * s; y = (m01 + m10) / s; z = (m02 + m20) / s;
		} else if (m11 > m22) {
			s = Math.sqrt(1 + m11 - m00 - m22) * 2;
			w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25 * s; z = (m12 + m21) / s;
		} else {
			s = Math.sqrt(1 + m22 - m00 - m11) * 2;
			w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25 * s;
		}

		into[at + 3] = x; into[at + 4] = y; into[at + 5] = z; into[at + 6] = w;
	}

	function writeJoints(into, hand, source) {
		var at = JOINTAT + hand * JOINTS * 7;

		if (!source || !source.hand || !inputs) return;

		// Nobody in the engine is reading fingers, so nothing here follows them.
		// A headset answers for a joint by walking its own tracking state, and
		// twenty-six of those a hand a frame is the most expensive thing a game
		// that never looks at a finger can be made to pay for. The engine says
		// when it wants them - see CoreWebXr.Tune and VRController.
		if (!fingersWanted) return;

		var spaces = spacesOf(source.hand);
		if (!spaces) return;

		// One call for the whole hand where the browser has it. fillPoses writes
		// every joint's matrix into one array; twenty-six getJointPose calls
		// cross into the browser twenty-six times for the same answer. It says
		// false where it could not answer for all of them, and then the array is
		// not to be read - so that falls through to asking one at a time.
		if (inputs.fillPoses && inputs.fillPoses(spaces, space, jointMatrices)) {
			for (var i = 0; i < JOINTS; i++) writeMatrix(into, at + i * 7, jointMatrices, i * 16);
			palmOf(into, at);
			return;
		}

		if (!inputs.getJointPose) return;

		for (var j = 0; j < JOINTS; j++) {
			var found = inputs.getJointPose(spaces[j], space);
			if (found) writePose(into, at + j * 7, found.transform);
		}

		palmOf(into, at);
	}

	// Where the middle finger's metacarpal and its first knuckle are in the list.
	var MIDDLE_METACARPAL = 11;
	var MIDDLE_PROXIMAL = 12;

	// The palm, as OpenXR has it: the middle of the middle finger's metacarpal,
	// turned the way that bone is. WebXR names the bone's two ends and not its
	// middle, so it is the point halfway between them. The wrist stood here
	// before, and it is a hand's length from the fingertips - so a closed fist
	// measured from it, which is how a game tells a fist, read as barely closed,
	// and whatever it held slipped out the moment the tracking wavered.
	function palmOf(into, at) {
		var a = at + MIDDLE_METACARPAL * 7;
		var b = at + MIDDLE_PROXIMAL * 7;

		into[at] = (into[a] + into[b]) * 0.5;
		into[at + 1] = (into[a + 1] + into[b + 1]) * 0.5;
		into[at + 2] = (into[a + 2] + into[b + 2]) * 0.5;
		into[at + 3] = into[a + 3];
		into[at + 4] = into[a + 4];
		into[at + 5] = into[a + 5];
		into[at + 6] = into[a + 6];
	}

	var words = new Float64Array(JOINTAT + 2 * JOINTS * 7);

	function poll(view) {
		if (!view || view.length < words.length) return 0;

		var into = words;
		into[ACTIVE] = 0;

		if (!session || !space || !pose || !views || !layerReady) return done(view);

		var r = renderer();

		for (var eye = 0; eye < 2; eye++) {
			var at = EYEAT + eye * EYEWORDS;
			writePose(into, at, views[eye].transform);
			writeField(into, at + 7, views[eye].projectionMatrix);

			into[SIZEAT + eye * 2] = r.xrSize(eye, 0);
			into[SIZEAT + eye * 2 + 1] = r.xrSize(eye, 1);

			if (into[SIZEAT + eye * 2] <= 0 || into[SIZEAT + eye * 2 + 1] <= 0) return done(view);
		}

		writePose(into, HEADPOSE, pose.transform);
		into[HEADFLAGS] = pose.emulatedPosition ? 2 : 3;

		// A session says what it is running at where the browser tells it; a
		// Quest's does not, and nought means "as fast as it asks for frames".
		into[REFRESH] = session.frameRate || 0;

		var left = null;
		var right = null;
		var sources = session.inputSources || [];

		// One source a side, and the same one frame after frame. A headset that
		// follows hands and controllers at once - a Quest does - lists both for a
		// side while the controller lies on the table, in whatever order it likes;
		// taking the first flicked the engine between the hand and the controller
		// frame to frame, and everything held or drawn at that hand jumped with it.
		// The hand wins: a controller in a hand hides that hand from the cameras,
		// so a side that has both is a hand that has put its controller down.
		for (var i = 0; i < sources.length; i++) {
			var source = sources[i];
			if (source.handedness === "left" && better(source, left)) left = source;
			else if (source.handedness === "right" && better(source, right)) right = source;
		}

		writeHand(into, HANDAT, left);
		writeHand(into, HANDAT + HANDWORDS, right);
		writeJoints(into, 0, left);
		writeJoints(into, 1, right);

		into[ACTIVE] = 1;
		return done(view);
	}

	// Whether a source is the one to read for its side over the one already taken.
	function better(source, taken) {
		return !taken || (!!source.hand && !taken.hand);
	}

	// What was read, copied into the engine's own memory - see the note above.
	function done(view) {
		view.set(words);
		return 1;
	}

	function buzz(hand, seconds, amplitude) {
		if (!session) return;

		var sources = session.inputSources || [];
		var want = hand === 0 ? "left" : "right";

		for (var i = 0; i < sources.length; i++) {
			if (sources[i].handedness !== want) continue;

			var pad = sources[i].gamepad;
			var actuators = pad && (pad.hapticActuators || []);
			if (!actuators || !actuators.length || !actuators[0].pulse) continue;

			try { actuators[0].pulse(amplitude, Math.max(seconds, 0.001) * 1000); } catch (e) {}
		}
	}

	// A wish made outside a gesture, taken on the next one.
	function settle() {
		if (wanted && !session) open();
	}

	window.addEventListener("click", settle, true);
	window.addEventListener("keydown", settle, true);

	return {
		// Nought is no, one is yes, and less than nought is "ask again" - which
		// is the answer until both the browser has come back from
		// isSessionSupported and the page has settled on a renderer. Saying no
		// early would be believed for the rest of the run; see CoreWebXr.Begin.
		supported: function () {
			var r = renderer();
			if (!r || !r.xrCan) return -1;
			if (!rendererCan()) return 0;
			return supported;
		},
		active: function () { return session && layerReady ? 1 : 0; },
		message: function () {
			if (!rendererCan() && renderer() && renderer().xrCan)
				return "this page is drawing with WebGPU, which cannot go in a headset - add ?webgl to the address";

			// A layer that had to fall back still works, and is worth saying: it
			// is the difference between a frame at our settings and one at the
			// browser's.
			var r = renderer();
			var fell = r && r.xrFallback ? r.xrFallback() : "";

			if (fell && !message) return fell;
			if (fell) return message + " | " + fell;

			return message;
		},
		enter: function () { wanted = true; open(); },
		leave: function () { wanted = false; leave(); },
		poll: poll,
		buzz: buzz,
		eyeTarget: function (eye) {
			var r = renderer();
			return r && r.xrTarget ? r.xrTarget(eye) : 0;
		},
		setFrame: function (fn) { frameBody = fn; },

		// Said by the engine when what it reads changes - see CoreWebXr.Tune.
		fingers: function (on) { fingersWanted = !!on; },

		// Whether this build asks for a headset at all - the manifest said so, or
		// ?vr on the address did. Worked out by index.html before the context was
		// made, because a context a headset draws from has to say so then.
		wanted: function () { return globalThis.RintenWantsXr ? 1 : 0; },

		// Whether the page should draw the way in for a game that has none of
		// its own - see index.html.
		offer: function (state) {
			if (typeof globalThis.rintenOfferVr === "function") globalThis.rintenOfferVr(!!state);
		}
	};
}

// --- a headset that is not there, for testing the path without one ----------
if (new URLSearchParams(location.search).has("fakexr")) {
	RintenXrBind = function () {
		var body = null;
		var targets = [0, 0];
		var ticks = 0;
		var polls = 0;

		function tick() {
			requestAnimationFrame(tick);
			if (!body) return;
			if (ticks === 0) console.log("fakexr: first body call");
			ticks++;
			if (ticks % 200 === 0) console.log("fakexr: ticks " + ticks + " polls " + polls);
			body();
		}

		requestAnimationFrame(tick);

		return {
			supported: function () { return 1; },
			active: function () { return 1; },
			message: function () { return "a headset that is not there"; },
			enter: function () {},
			leave: function () {},
			wanted: function () { return 1; },
			offer: function () {},
			buzz: function () {},
			setFrame: function (fn) { console.log("fakexr: setFrame"); body = fn; },
			// The eye's number and one, the way the real one names an eye - see
			// BrowserSwapchain.ViewCopyTo. The copy itself finds no viewport and
			// does nothing, which is the point: what this stands in for is the
			// ticking and the polling, not the picture.
			eyeTarget: function (eye) {
				if (!targets[eye]) {
					console.log("fakexr: eyeTarget " + eye);
					targets[eye] = eye + 1;
				}
				return targets[eye];
			},
			poll: function (view) {
				var into = new Float64Array(442);
				var c = document.getElementById("canvas");
				var w = Math.max(2, Math.floor(c.width / 2));
				var h = Math.max(2, Math.floor(c.height / 2));

				if (polls === 0) console.log("fakexr: first poll len=" + view.length + " eye=" + w + "x" + h);
				polls++;

				into[0] = 1; into[1] = 72; into[2] = 3;
				into[3] = 0; into[4] = 1.6; into[5] = 0;
				into[6] = 0; into[7] = 0; into[8] = 0; into[9] = 1;

				for (var e = 0; e < 2; e++) {
					var at = 10 + e * 11;
					into[at] = e === 0 ? -0.032 : 0.032;
					into[at + 1] = 1.6;
					into[at + 2] = 0;
					into[at + 3] = 0; into[at + 4] = 0; into[at + 5] = 0; into[at + 6] = 1;
					into[at + 7] = -0.8; into[at + 8] = 0.8; into[at + 9] = 0.7; into[at + 10] = -0.7;
					into[32 + e * 2] = w;
					into[32 + e * 2 + 1] = h;
				}

				view.set(into);
				return 1;
			}
		};
	};
}
