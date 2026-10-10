// One of basis.js's workers: KTX2s of UASTC blocks in, every level turned into
// the format asked for and handed back. The transcoder is Binomial's own build
// (basis_transcoder.js and .wasm, Apache 2.0).

importScripts("basis_transcoder.js");

var ready = BASIS({ locateFile: function (file) { return file; } }).then(function (module) {
	module.initializeBasis();
	return module;
});

onmessage = function (e) {
	var job = e.data;

	ready.then(function (module) {
		var answer = { id: job.id, levels: [], error: null };
		var file = new module.KTX2File(new Uint8Array(job.bytes));

		try {
			if (!file.isValid()) throw "not a KTX2 the transcoder reads";
			if (!file.startTranscoding()) throw "the transcoder would not start on it";

			var levels = file.getLevels();

			for (var level = 0; level < levels; level++) {
				var info = file.getImageLevelInfo(level, 0, 0);
				var size = file.getImageTranscodedSizeInBytes(level, 0, 0, job.target);
				var out = new Uint8Array(size);

				if (!file.transcodeImage(out, level, 0, 0, job.target, 0, -1, -1)) throw "level " + level + " would not transcode";

				answer.levels.push({
					data: out.buffer,
					width: info.origWidth,
					height: info.origHeight,
					blocksX: info.numBlocksX,
					blocksY: info.numBlocksY
				});
			}
		} catch (x) {
			answer.error = String(x);
			answer.levels = [];
		} finally {
			file.close();
			file.delete();
		}

		postMessage(answer, answer.levels.map(function (l) { return l.data; }));
	});
};
