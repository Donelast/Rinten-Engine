//Transcodes KTX2/UASTC textures in workers to the best GPU format and uploads each mip level

(function () {
	"use strict";

	var workers = null;
	var load = [];
	var waiting = {};
	var next = 1;

	//By texture: nought while its levels are on their way, one once they are written, minus one where they would not transcode - see transcodeState.
	var state = {};

	function start() {
		//Half the machine's threads, at most four: transcoding is quick, and the page's own thread wants the rest.
		var count = Math.max(1, Math.min(4, (navigator.hardwareConcurrency || 2) >> 1));
		workers = [];

		for (var i = 0; i < count; i++) {
			var w = new Worker("basis_worker.js");
			w.onmessage = done;
			w.onerror = function (e) { console.error("the picture transcoder failed: " + e.message); };
			workers.push(w);
			load.push(0);
		}
	}

	function done(e) {
		var d = e.data;
		var job = waiting[d.id];
		if (!job) return;

		delete waiting[d.id];
		load[job.worker]--;

		if (d.error) {
			console.warn("a picture would not transcode: " + d.error);
			if (RintenHandles.get(job.texture) === job.object) state[job.texture] = -1;
			return;
		}

		if (RintenHandles.get(job.texture) !== job.object) return;

		state[job.texture] = 1;

		for (var i = 0; i < d.levels.length; i++) {
			var level = d.levels[i];
			var bytes = new Uint8Array(level.data);

			//A block format a whole block at a time, a row of the upload a row of blocks; pixels a pixel at a time.
			if (job.blockBytes) job.write(job.texture, i, 0, 0, level.blocksX * 4, level.blocksY * 4, level.blocksX * job.blockBytes, bytes);
			else job.write(job.texture, i, 0, 0, level.width, level.height, level.width * 4, bytes);
		}
	}

	//The blocks are copied here, before the call returns: what the runtime hands
	//over is a view of its own memory, good only while the call is on the stack.
	function transcodeInto(texture, bytes, target, blockBytes, write) {
		if (!workers) start();

		var copy = RintenHandles.bytesOf(bytes).slice();

		var pick = 0;
		for (var i = 1; i < workers.length; i++) {
			if (load[i] < load[pick]) pick = i;
		}

		var id = next++;
		state[texture] = 0;
		waiting[id] = { texture: texture, object: RintenHandles.get(texture), blockBytes: blockBytes, write: write, worker: pick };
		load[pick]++;

		workers[pick].postMessage({ id: id, bytes: copy.buffer, target: target }, [copy.buffer]);
	}

	//Transcode state: 1 done, -1 failed, 0 pending; final answers are forgotten as handles get reused
	function transcodeState(texture) {
		var s = state[texture];
		if (s === undefined) return 1;
		if (s !== 0) delete state[texture];
		return s;
	}

	globalThis.RintenBasis = { transcodeInto: transcodeInto, transcodeState: transcodeState };
})();
