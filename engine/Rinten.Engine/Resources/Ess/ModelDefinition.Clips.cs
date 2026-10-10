namespace Rinten;

partial class ModelDefinition
{
	/// <summary>One take compiled into this model, named as the export names it.</summary>
	public class ModelClip
	{
		/// <summary>The export this is built from - or, for a character with no source art, the compiled clip itself. See <see cref="AnimationCompiler.CompileModelClip"/> for what an <c>.anim_c</c> here means: the only copy there is, left alone rather than rebuilt.</summary>
		[Property, FilePath( "fbx,gltf,glb,smd,anim_c" )]
		public string File { get; set; } = "";

		//Export take names differ (often "Take 001"), so the model's clip name is kept separately
		/// <summary>What this clip is called on the model, where that is not what the export calls it.</summary>
		[Property, Hide]
		public string Name { get; set; } = "";

		/// <summary>Which take inside the export, by the export's own name for it. Blank takes the one <see cref="Take"/> numbers.</summary>
		[Property, Hide]
		public string TakeName { get; set; } = "";

		/// <summary>Which animation inside the file, for one holding more than one. Ignored when <see cref="TakeName"/> is set.</summary>
		[Property, Hide]
		public int Take { get; set; }

		//Subtracted at compile time so additive layers get a delta, not a full pose
		/// <summary>The clip this one is measured against, where it is a difference rather than a pose. Blank for the ordinary case and for a clip measured against a frame of itself.</summary>
		[Property, Hide]
		public string SubtractFrom { get; set; } = "";

		/// <summary>Which frame of <see cref="SubtractFrom"/> every frame of this clip is measured against. Below zero for a clip that is not a difference.</summary>
		[Property, Hide]
		public int SubtractFrame { get; set; } = -1;

		//Their format says which: AnimSubtract against the bone's own parent, AnimSubtractModelSpace against where it stands. The layer that plays each expects the one it was measured in - a parent-space difference laid on in model space turns a character as a whole.
		/// <summary>Whether the difference is measured where each bone stands rather than under the bone above it.</summary>
		[Property, Hide]
		public bool SubtractModelSpace { get; set; }

		//Their documents cut several clips out of one export: the citizen's eight pelvis lean poses are one frame each of the same file. Without the slice they are eight copies of the whole animation.
		/// <summary>The first frame of the export this clip is made of, or below zero for its start.</summary>
		[Property, Hide]
		public int FirstFrame { get; set; } = -1;

		/// <summary>The last frame of it, or below zero for its end.</summary>
		[Property, Hide]
		public int LastFrame { get; set; } = -1;

		//A walk cycle is authored walking forwards, and something has to decide whether the character is carried by the animation or by the game. Where the game carries it, the travel has to come off the clip or the mesh walks out of the character it belongs to.
		/// <summary>A bone whose travel is lifted off the clip, leaving it playing on the spot - or nothing, which is a clip that keeps whatever it was authored with.</summary>
		[Property, Hide]
		public string MotionBone { get; set; } = "";

		/// <summary>Whether that bone's travel across the ground comes off.</summary>
		[Property, Hide]
		public bool MotionAcross { get; set; }

		/// <summary>And its travel up and down.</summary>
		[Property, Hide]
		public bool MotionUp { get; set; }

		//An animator draws a walk cycle wherever their rig happened to stand, and lining its first frame up with the idle is what puts the walk under the character rather than half a metre in front of it.
		/// <summary>A clip this one's first frame is lined up with, so that two clips played one after the other put the character in the same place.</summary>
		[Property, Hide]
		public string AlignTo { get; set; } = "";

		/// <summary>Which frame of this clip is lined up.</summary>
		[Property, Hide]
		public int AlignFrame { get; set; }

		/// <summary>And which frame of the other one it is lined up with.</summary>
		[Property, Hide]
		public int AlignToFrame { get; set; }

		/// <summary>The bone that is lined up, or nothing for the root.</summary>
		[Property, Hide]
		public string AlignBone { get; set; } = "";

		/// <summary>Whether it is lined up across the ground.</summary>
		[Property, Hide]
		public bool AlignAcross { get; set; }

		/// <summary>And up and down.</summary>
		[Property, Hide]
		public bool AlignUp { get; set; }

		//A cycle cut out of a longer take does not end where it began, and played round it jumps at the seam.
		/// <summary>How many frames at the start, and at the end, are brought together so the clip loops without a jolt. Zero leaves the seam as it is.</summary>
		[Property, Hide]
		public int SmoothFromStart { get; set; }

		/// <summary>And how many at the end.</summary>
		[Property, Hide]
		public int SmoothFromEnd { get; set; }

		//How an animation drawn for one body is worn by another: the difference between the two bodies at rest, added on top. Their human citizen wears a hundred and eighty-seven of the cartoony citizen's animations this way.
		/// <summary>A difference laid over every frame of this clip, by name, or nothing.</summary>
		[Property, Hide]
		public string LayerFrom { get; set; } = "";

		//A walk drawn going one way, played going another: their citizen's four diagonal walks are the cardinal ones turned forty-five degrees, and there is no second animation behind them.
		/// <summary>How far the whole clip is turned about the model's upright, in degrees.</summary>
		[Property, Hide]
		public float TurnDegrees { get; set; }

		//An animation is drawn at whatever rate the animator worked in and played at whatever rate the character needs - a jump exported at thirty and played at thirty-six, a grid of nine aim poses played at three so each holds for a third of a second.
		/// <summary>How many frames a second this plays at, or below zero to keep the rate the export was made at.</summary>
		[Property, Hide]
		public float FrameRate { get; set; } = -1.0f;

		//An animation with a run-up that plays once and a cycle that plays for ever after: a breathing layer that settles over its first second and repeats what follows.
		/// <summary>The frame a loop goes back to, which is not always the first.</summary>
		[Property, Hide]
		public int LoopStart { get; set; }

		[Property]
		public float Scale { get; set; } = 1.0f;

		[Property]
		public bool Looping { get; set; } = true;

		[Property]
		public bool Additive { get; set; }

		[Property, Hide]
		public bool Enabled { get; set; } = true;

		//A name rather than a place in a tree: a node keeps its folder when the list around it is reordered, and a folder nobody is in stops existing on its own.
		/// <summary>What folder the outliner sorts this into, or nothing for the top.</summary>
		[Property, Hide]
		public string Folder { get; set; } = "";

		[Property, Hide]
		public List<Event> Events { get; set; } = new();

		public string SequenceName
		{
			get
			{
				//What the model calls it, then what the export calls it, then the file itself - each one more of a guess than the last.
				var raw = Name;

				if ( string.IsNullOrWhiteSpace( raw ) ) raw = TakeName;

				if ( string.IsNullOrWhiteSpace( raw ) )
					raw = System.IO.Path.GetFileNameWithoutExtension( File ) ?? "";
				return SafeClipName( raw );
			}
		}

		/// <summary>FBX take names can contain |; the compiled .anim cannot.</summary>
		public static string SafeClipName( string name )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) return "";

			foreach ( var c in System.IO.Path.GetInvalidFileNameChars() )
				name = name.Replace( c, '_' );

			return name.Replace( '|', '_' );
		}

		/// <summary>Markers along a clip (footsteps, sounds, transition guards); FBX can't carry these</summary>
		public class Event
		{
			/// <summary>What it is called - what a graph asks for by name.</summary>
			[Property]
			public string Id { get; set; } = "";

			[Property]
			public EventKind Kind { get; set; } = EventKind.Generic;

			/// <summary>In frames of the export, because that is where an animator put it. Turned into a time when the clip is compiled, using the rate the export was authored at.</summary>
			[Property]
			public float StartFrame { get; set; }

			/// <summary>Where it ends. Below the start means a moment rather than a span, and a moment is written one frame long - what a graph asks of an event is how far through it is, and a moment cannot answer that.</summary>
			[Property]
			public float EndFrame { get; set; } = -1.0f;

			/// <summary>Which foot, which transition rule, or where in the clip's sync order this sits - by kind.</summary>
			public int Data { get; set; }

			/// <summary>A curve event's values, spread evenly across its span.</summary>
			public List<float> Curve { get; set; } = new();
		}

		/// <summary>What an event is about.</summary>
		public enum EventKind
		{
			/// <summary>A name at a moment - a sound, a hit, anything the game listens for.</summary>
			Generic = 0,

			/// <summary>A footstep. Which foot is in <see cref="Event.Data"/>.</summary>
			Foot = 1,

			/// <summary>A mark that says "this is the same moment" across clips - the point of one walk that lines up with a point of another.</summary>
			Sync = 2,

			/// <summary>A number drawn along the clip rather than a moment in it.</summary>
			Curve = 3,

			/// <summary>Whether a transition away from here is allowed.</summary>
			Transition = 4,

			//Six rather than five: these numbers are cast straight across to GraphContext.SampledKind, and five there is the graph's own kind, raised by a state machine rather than read off a clip.
			/// <summary>A name that is true for as long as the playhead is inside it - "reloading", "can_be_hit" - which the game is told about as it starts and stops, and a graph can ask about like any other mark.</summary>
			Tag = 6,
		}
	}

	/// <summary>Compile one take to <paramref name="outputPath"/> so the preview and graph can load it.</summary>
	public bool TryCompileClip( ModelClip clip, string sourceDirectory, string outputPath )
	{
		if ( clip is null || string.IsNullOrWhiteSpace( outputPath ) )
			return false;

		var result = AnimationCompiler.CompileModelClip( clip, this, sourceDirectory, outputPath );
		AnimationClip.Forget( outputPath );
		return result.Success;
	}

	/// <summary>The takes this model declares - the one place its clips are listed.</summary>
	public IReadOnlyList<ModelClip> AllClips() => Clips;
}
