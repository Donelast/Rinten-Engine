using System.IO;

namespace Rinten;

//Turns a ModelDefinition.ModelClip into the compiled clip the engine samples. The counterpart to ModelCompiler,
//and the same shape: the keys come from an export the artist made, read by our own core and nothing else.
internal static class AnimationCompiler
{
	private static readonly Logger log = new( "Core" );

	/// <summary>What a compile did, or why it did nothing.</summary>
	public readonly record struct Result( bool Success, string Reason, int Frames )
	{
		public static Result Failed( string reason ) => new( false, reason, 0 );
		public static Result Ok( int frames ) => new( true, "", frames );
	}

	//Separate from ModelDefinition.ModelClip so a clip can be compiled from fields built up during a port too, ahead of there being a model definition to hold them.
	/// <summary>What a clip says, as the compiler needs it.</summary>
	public readonly record struct Fields( string Source, int Take, float Scale, bool Looping, bool Additive, string Name )
	{
		public string TakeName { get; init; }

		/// <summary>The clip a frame of which every frame of this is measured against.</summary>
		public string SubtractFrom { get; init; }

		/// <summary>Which frame of it, or below zero where this is not a difference.</summary>
		public int SubtractFrame { get; init; }

		/// <summary>Whether the difference is measured where the bone stands rather than under its parent.</summary>
		public bool SubtractModelSpace { get; init; }

		/// <summary>The slice of the export this clip is, or below zero for all of it.</summary>
		public int FirstFrame { get; init; } = -1;

		/// <summary>And where that slice ends.</summary>
		public int LastFrame { get; init; } = -1;

		/// <summary>A bone whose travel is lifted off the clip, or nothing.</summary>
		public string MotionBone { get; init; }

		/// <summary>Whether its travel across the ground comes off.</summary>
		public bool MotionAcross { get; init; }

		/// <summary>And its travel up and down.</summary>
		public bool MotionUp { get; init; }

		/// <summary>A clip this one's frame is lined up with, or nothing.</summary>
		public string AlignTo { get; init; }

		/// <summary>Which frame of this one, and which of the other.</summary>
		public int AlignFrame { get; init; }

		public int AlignToFrame { get; init; }

		/// <summary>The bone that is lined up, or nothing for the root.</summary>
		public string AlignBone { get; init; }

		/// <summary>Whether across the ground, and whether up and down.</summary>
		public bool AlignAcross { get; init; }

		public bool AlignUp { get; init; }

		/// <summary>How many frames at each end are brought together, or none.</summary>
		public int SmoothFromStart { get; init; }

		public int SmoothFromEnd { get; init; }

		/// <summary>A difference laid over every frame of this, by name, or nothing.</summary>
		public string LayerFrom { get; init; }

		/// <summary>How far the whole clip is turned about the model's upright.</summary>
		public float TurnDegrees { get; init; }

		/// <summary>The rate it plays at, or below zero for the export's own.</summary>
		public float FrameRate { get; init; } = -1.0f;

		/// <summary>The frame a loop goes back to, or the first.</summary>
		public int LoopStart { get; init; }

		/// <summary>What is marked along the clip. Kept out of the constructor because most clips have none and the six above are what every one has.</summary>
		public IReadOnlyList<ModelDefinition.ModelClip.Event> Events { get; init; }
	}

	/// <summary>Compile a clip from what a definition says.</summary>
	public static Result Compile( Fields fields, string sourceDirectory, string outputPath, ModelDefinition ops = null )
	{
		if ( !CoreNative.Available ) return Result.Failed( $"rintencore is not loaded: {CoreNative.BuildInfo}" );

		if ( string.IsNullOrWhiteSpace( fields.Source ) )
			return Result.Failed( "no source - a clip has nothing baked into it to fall back on" );

		var source = AssetSources.Resolve( fields.Source, sourceDirectory );
		if ( source is null )
			return Result.Failed( $"cannot find {fields.Source}" );

		var builder = new CoreAnimation.AnimClipBuilder();

		try
		{
			builder.Name = fields.Name ?? "";

			var scale = fields.Scale > 0.0f ? fields.Scale : 1.0f;

			var frames = Path.GetExtension( source ).ToLowerInvariant() switch
			{
				".fbx" => ImportFbx( builder, source, scale, fields ),
				".smd" => CoreSmdImport.ImportClip( builder, source, scale ),
				".gltf" or ".glb" => CoreGltfImport.ImportClip( builder, source, scale, fields.Take, fields.TakeName ),
				_ => -1,
			};

			if ( frames < 0 ) return Result.Failed( $"{fields.Source} is not an export this core can read" );
			if ( frames == 0 ) return Result.Failed( $"{fields.Source} has no animation in it" );

			//Set before the clip is cut, because the frame a loop goes back to is numbered from the start of
			//the file and the cut moves it - which Trim does, and cannot do to a value it has not been given.
			builder.LoopStart = fields.LoopStart;

			//Before the difference, because the frame a difference is measured against is counted from
			//the start of the clip rather than of the file - their own document numbers it that way.
			if ( fields.FirstFrame >= 0 || fields.LastFrame >= 0 )
			{
				builder.Trim( fields.FirstFrame, fields.LastFrame );
				frames = builder.FrameCount;
			}

			//Apply the model's turn first so the clip, its references and layers all share one space
			ApplyOps( builder, ops );

			Layered( builder, fields, sourceDirectory, ops );

			//Turned before it is lined up and before its travel comes off, because both of those are about where it goes and it is about to go somewhere else.
			if ( fields.TurnDegrees != 0.0f ) builder.TurnAbove( fields.TurnDegrees );

			//Line up before the difference, then hold still, matching the source document's processing order
			LineUp( builder, fields, sourceDirectory, ops );

			if ( fields.MotionAcross || fields.MotionUp )
				builder.HoldStill( fields.MotionBone, fields.MotionAcross, fields.MotionUp );

			//And the seam last, so it closes over whatever the rest of the processing left the ends at.
			if ( fields.SmoothFromStart > 0 || fields.SmoothFromEnd > 0 )
				builder.SmoothLoop( fields.SmoothFromStart, fields.SmoothFromEnd );

			Difference( builder, fields, sourceDirectory, ops );

			builder.Looping = fields.Looping;
			builder.Additive = fields.Additive;

			//Last, because everything above counts in frames and only this decides how long a frame is.
			if ( fields.FrameRate > 0.0f ) builder.SetFrameRate( fields.FrameRate );

			WriteEvents( builder, fields );

			Directory.CreateDirectory( Path.GetDirectoryName( outputPath ) );

			return builder.Save( outputPath )
				? Result.Ok( frames )
				: Result.Failed( $"could not write {outputPath}" );
		}
		catch ( Exception e )
		{
			log.Warning( $"compiling {fields.Name} failed: {e.Message}" );
			return Result.Failed( e.Message );
		}
		finally
		{
			builder.Dispose();
		}
	}

	//Picks the take by name, falling back to the numbered take since take names rarely match
	static int ImportFbx( CoreAnimation.AnimClipBuilder builder, string source, float scale, Fields fields )
	{
		if ( !string.IsNullOrWhiteSpace( fields.TakeName ) )
		{
			var named = CoreFbxImport.ImportClipNamed( builder, source, scale, fields.TakeName );

			if ( named > 0 ) return named;
		}

		return CoreFbxImport.ImportClip( builder, source, scale, fields.Take );
	}

	//Makes an additive clip a difference against its named reference frame (usually its own first frame)
	static void Difference( CoreAnimation.AnimClipBuilder builder, Fields fields,
		string sourceDirectory, ModelDefinition definition )
	{
		if ( fields.SubtractFrame < 0 ) return;

		var named = fields.SubtractFrom ?? "";

		//Against itself, which is what a blank name and its own name both mean.
		if ( string.IsNullOrWhiteSpace( named ) || string.Equals( named, fields.Name, StringComparison.OrdinalIgnoreCase ) )
		{
			builder.Subtract( builder, fields.SubtractFrame, fields.SubtractModelSpace );
			return;
		}

		var against = definition?.AllClips()?.FirstOrDefault( clip =>
			clip is not null && string.Equals( clip.SequenceName, named, StringComparison.OrdinalIgnoreCase ) );

		//A clip measured against one the model does not list. Its own first frame is the nearest thing to the
		//pose it was drawn from, and a difference measured from that is far closer than no difference at all.
		if ( against is null )
		{
			builder.Subtract( builder, fields.SubtractFrame, fields.SubtractModelSpace );
			return;
		}

		var reference = new CoreAnimation.AnimClipBuilder();

		try
		{
			var source = AssetSources.Resolve( against.File, sourceDirectory );
			if ( source is null ) { builder.Subtract( builder, fields.SubtractFrame, fields.SubtractModelSpace ); return; }

			var scale = against.Scale > 0.0f ? against.Scale : 1.0f;

			var fromFields = new Fields( against.File, against.Take, scale, against.Looping,
				against.Additive, against.SequenceName ) { TakeName = against.TakeName };

			var frames = Path.GetExtension( source ).ToLowerInvariant() switch
			{
				".fbx" => ImportFbx( reference, source, scale, fromFields ),
				".smd" => CoreSmdImport.ImportClip( reference, source, scale ),
				".gltf" or ".glb" => CoreGltfImport.ImportClip( reference, source, scale, against.Take, against.TakeName ),
				_ => -1,
			};

			//In the same space as what is being measured, or the difference carries the turn between the two spaces as if it were animation.
			if ( frames > 0 ) ApplyOps( reference, definition );

			builder.Subtract( frames > 0 ? reference : builder, fields.SubtractFrame, fields.SubtractModelSpace );
		}
		catch ( Exception e )
		{
			log.Warning( $"measuring {fields.Name} against {named} failed: {e.Message}" );
			builder.Subtract( builder, fields.SubtractFrame, fields.SubtractModelSpace );
		}
		finally
		{
			reference.Dispose();
		}
	}

	//Lays a named difference over every frame; it is built as declared, not read from its export
	static void Layered( CoreAnimation.AnimClipBuilder builder, Fields fields,
		string sourceDirectory, ModelDefinition definition )
	{
		var named = fields.LayerFrom ?? "";

		if ( string.IsNullOrWhiteSpace( named ) ) return;
		if ( string.Equals( named, fields.Name, StringComparison.OrdinalIgnoreCase ) ) return;

		var over = definition?.AllClips()?.FirstOrDefault( clip =>
			clip is not null && string.Equals( clip.SequenceName, named, StringComparison.OrdinalIgnoreCase ) );

		if ( over is null )
		{
			log.Warning( $"{fields.Name} lays {named} over itself and the model has no such clip" );
			return;
		}

		var laid = Built( over, sourceDirectory, definition, 0 );
		if ( laid is null ) return;

		try { builder.AddLayer( laid ); }
		finally { laid.Dispose(); }
	}

	//Builds a listed clip far enough to be a reference: imported, cut and differenced, two levels deep
	static CoreAnimation.AnimClipBuilder Built( ModelDefinition.ModelClip clip,
		string sourceDirectory, ModelDefinition definition, int depth )
	{
		if ( clip is null || depth > 2 ) return null;

		var source = AssetSources.Resolve( clip.File, sourceDirectory );
		if ( source is null ) return null;

		var made = new CoreAnimation.AnimClipBuilder();

		try
		{
			var scale = clip.Scale > 0.0f ? clip.Scale : 1.0f;

			var fields = new Fields( clip.File, clip.Take, scale, clip.Looping, clip.Additive, clip.SequenceName )
			{
				TakeName = clip.TakeName,
			};

			var frames = Path.GetExtension( source ).ToLowerInvariant() switch
			{
				".fbx" => ImportFbx( made, source, scale, fields ),
				".smd" => CoreSmdImport.ImportClip( made, source, scale ),
				".gltf" or ".glb" => CoreGltfImport.ImportClip( made, source, scale, clip.Take, clip.TakeName ),
				_ => -1,
			};

			if ( frames <= 0 ) { made.Dispose(); return null; }

			if ( clip.FirstFrame >= 0 || clip.LastFrame >= 0 ) made.Trim( clip.FirstFrame, clip.LastFrame );

			ApplyOps( made, definition );

			if ( clip.SubtractFrame >= 0 )
			{
				var against = string.IsNullOrWhiteSpace( clip.SubtractFrom ) ||
					string.Equals( clip.SubtractFrom, clip.SequenceName, StringComparison.OrdinalIgnoreCase )
					? made
					: Built( definition?.AllClips()?.FirstOrDefault( other =>
						other is not null && string.Equals( other.SequenceName, clip.SubtractFrom,
							StringComparison.OrdinalIgnoreCase ) ), sourceDirectory, definition, depth + 1 );

				if ( against is null ) against = made;

				made.Subtract( against, clip.SubtractFrame, clip.SubtractModelSpace );

				if ( !ReferenceEquals( against, made ) ) against.Dispose();
			}

			return made;
		}
		catch ( Exception e )
		{
			log.Warning( $"building {clip.SequenceName} to lay over another clip failed: {e.Message}" );
			made.Dispose();
			return null;
		}
	}

	//Slides a clip so its first frame matches another clip's, so cycles line up with the idle
	static void LineUp( CoreAnimation.AnimClipBuilder builder, Fields fields,
		string sourceDirectory, ModelDefinition definition )
	{
		if ( !fields.AlignAcross && !fields.AlignUp ) return;

		var named = fields.AlignTo ?? "";
		if ( string.IsNullOrWhiteSpace( named ) ) return;

		//Against itself lines nothing up.
		if ( string.Equals( named, fields.Name, StringComparison.OrdinalIgnoreCase ) ) return;

		var against = definition?.AllClips()?.FirstOrDefault( clip =>
			clip is not null && string.Equals( clip.SequenceName, named, StringComparison.OrdinalIgnoreCase ) );

		if ( against is null ) return;

		var reference = new CoreAnimation.AnimClipBuilder();

		try
		{
			var source = AssetSources.Resolve( against.File, sourceDirectory );
			if ( source is null ) return;

			var scale = against.Scale > 0.0f ? against.Scale : 1.0f;

			var fromFields = new Fields( against.File, against.Take, scale, against.Looping,
				against.Additive, against.SequenceName ) { TakeName = against.TakeName };

			var frames = Path.GetExtension( source ).ToLowerInvariant() switch
			{
				".fbx" => ImportFbx( reference, source, scale, fromFields ),
				".smd" => CoreSmdImport.ImportClip( reference, source, scale ),
				".gltf" or ".glb" => CoreGltfImport.ImportClip( reference, source, scale, against.Take, against.TakeName ),
				_ => -1,
			};

			if ( frames <= 0 ) return;

			//The clip it is lined up with is a slice of its own export as often as this one
			//is, and its frame is counted from the start of the clip rather than of the file.
			if ( against.FirstFrame >= 0 || against.LastFrame >= 0 )
				reference.Trim( against.FirstFrame, against.LastFrame );

			ApplyOps( reference, definition );

			builder.LineUpWith( reference, fields.AlignBone, fields.AlignFrame, fields.AlignToFrame,
				fields.AlignAcross, fields.AlignUp );
		}
		catch ( Exception e )
		{
			log.Warning( $"lining {fields.Name} up with {named} failed: {e.Message}" );
		}
		finally
		{
			reference.Dispose();
		}
	}

	static void ApplyOps( CoreAnimation.AnimClipBuilder builder, ModelDefinition definition )
	{
		if ( builder is null || definition is null ) return;

		var transform = definition.Transform;
		if ( transform is { Enabled: true } && !transform.IsIdentity )
		{
			builder.ApplyTransform( transform.Position, transform.Rotation.ToRotation(), transform.Scale );
		}

		if ( definition.Mirror is { Enabled: true } mirror )
			builder.Mirror( (int)mirror.Axis, mirror.RemapLeftRight );
	}

	/// <summary>Compile a take listed on a model, applying that model's TRS/mirror.</summary>
	public static Result CompileModelClip( ModelDefinition.ModelClip clip, ModelDefinition definition,
		string sourceDirectory, string outputPath )
	{
		if ( clip is null ) return Result.Failed( "no clip" );

		//Compiled and named as such: a character with no source art names its clips this way, and the file beside
		//it already is the only copy there is. Nothing to rebuild it from - which is success, not failure.
		if ( string.Equals( Path.GetExtension( clip.File ), ".anim_c", StringComparison.OrdinalIgnoreCase ) )
			return Result.Ok( 0 );

		var fields = new Fields( clip.File, clip.Take, clip.Scale, clip.Looping, clip.Additive, clip.SequenceName )
		{
			TakeName = clip.TakeName,
			Events = clip.Events,
			SubtractFrom = clip.SubtractFrom,
			SubtractFrame = clip.SubtractFrame,
			FirstFrame = clip.FirstFrame,
			LastFrame = clip.LastFrame,
			MotionBone = clip.MotionBone,
			MotionAcross = clip.MotionAcross,
			MotionUp = clip.MotionUp,
			AlignTo = clip.AlignTo,
			AlignFrame = clip.AlignFrame,
			AlignToFrame = clip.AlignToFrame,
			AlignBone = clip.AlignBone,
			AlignAcross = clip.AlignAcross,
			AlignUp = clip.AlignUp,
			SmoothFromStart = clip.SmoothFromStart,
			SmoothFromEnd = clip.SmoothFromEnd,
			LayerFrom = clip.LayerFrom,
			TurnDegrees = clip.TurnDegrees,
			FrameRate = clip.FrameRate,
			LoopStart = clip.LoopStart,
			SubtractModelSpace = clip.SubtractModelSpace,
		};

		return Compile( fields, sourceDirectory, outputPath, definition );
	}

	//Put what the definition marked onto the compiled clip. Frames become seconds here, because this
	//is the first place that knows the rate: an animator marks a frame, and the rate is the export's.
	private static void WriteEvents( CoreAnimation.AnimClipBuilder builder, Fields fields )
	{
		if ( fields.Events is null || fields.Events.Count == 0 ) return;

		var rate = builder.FrameRate;
		if ( rate <= 0.0f ) rate = 30.0f;

		foreach ( var marked in fields.Events )
		{
			if ( marked is null ) continue;

			var start = marked.StartFrame / rate;

			//A moment is written a frame long: what a graph asks of an event is how far through it is, and a moment answers that with nothing.
			var end = marked.EndFrame < marked.StartFrame
				? start + ( 1.0f / rate )
				: marked.EndFrame / rate;

			var index = builder.AddEvent( marked.Id ?? "", (int)marked.Kind, start, end, marked.Data );

			if ( index < 0 || marked.Curve is null || marked.Curve.Count == 0 ) continue;

			builder.SetEventCurve( index, System.Runtime.InteropServices.CollectionsMarshal.AsSpan( marked.Curve ) );
		}
	}
}
