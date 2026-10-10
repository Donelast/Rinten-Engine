using System.Buffers.Binary;
using System.Collections.Generic;

namespace Rinten.Porting.SBox;

//Rebuilds whole clips from compressed channel segments, starting from the rest pose
internal static partial class CoreImportedAnimations
{
	//A bone of the skeleton the clips are written against.
	internal readonly record struct Bone( string Name, int Parent, Vector3 Position, Rotation Rotation );

	//One clip, every bone, every frame.
	internal sealed class Clip
	{
		public string Name;

		/// <summary>What it was authored at.</summary>
		public float FrameRate = 30.0f;

		public int FrameCount;
		public int BoneCount;

		public bool Looping;

		/// <summary>Additive clip, inferred from the poses (most bone positions zero) since the file flag is unset</summary>
		public bool Delta;

		/// <summary>Where each bone stands, relative to the bone above it, as <c>frame * BoneCount + bone</c>.</summary>
		public Vector3[] Positions = System.Array.Empty<Vector3>();
		public Rotation[] Rotations = System.Array.Empty<Rotation>();

		/// <summary>Which bones the clip actually says anything about, per channel.</summary>
		public bool[] Moves = System.Array.Empty<bool>();
		public bool[] Turns = System.Array.Empty<bool>();

		/// <summary>Recorded root travel over the clip in metres (model axes); used to place clips in blends</summary>
		public Vector3 Movement;

		/// <summary>Seconds from the first frame to the last.</summary>
		public float Duration => FrameRate > 0.0f && FrameCount > 1
			? ( FrameCount - 1 ) / FrameRate
			: 0.0f;

		/// <summary>How fast it travels, in metres a second.</summary>
		public float Speed => Duration > 0.0f ? Movement.Length / Duration : 0.0f;

		/// <summary>The same, as forward and to the right - which is how a game says where it is going.</summary>
		public Vector2 GroundVelocity => Duration > 0.0f
			? new Vector2( Movement.x, -Movement.y ) / Duration
			: Vector2.Zero;
	}

	//One bone's worth of one channel: what the values in a segment are for.
	internal sealed class Channel
	{
		/// <summary>Position, Angle, Scale - or something we do not carry.</summary>
		public string Variable = "";

		/// <summary>Which bone an element of this channel belongs to.</summary>
		public readonly Dictionary<int, int> Bones = new();
	}

	internal readonly record struct Segment( int Channel, byte[] Container );

	//One entry of a mask: a bone, and the weight from it downwards.
	internal readonly record struct MaskBone( string Bone, float Weight );

	//A named set of per-bone weights - their weight list, our bone mask.
	internal sealed class Mask
	{
		public string Name = "";

		/// <summary>Where the weight changes, parents before children. Everything else is at nothing.</summary>
		public IReadOnlyList<MaskBone> Bones = System.Array.Empty<MaskBone>();
	}

	//Everything a model's animation blocks hold, read once. Sampling a clip out of it is Animations.Read.
	internal sealed class Animations
	{
		/// <summary>The skeleton the clips move, in their order.</summary>
		public IReadOnlyList<Bone> Skeleton { get; init; } = System.Array.Empty<Bone>();

		/// <summary>What each clip is called, in the order they are stored.</summary>
		public IReadOnlyList<string> Clips { get; init; } = System.Array.Empty<string>();

		/// <summary>Every clip name the sequences refer to each other by.</summary>
		public IReadOnlyList<string> Names { get; init; } = System.Array.Empty<string>();

		/// <summary>What a game can ask to play.</summary>
		public IReadOnlyList<Sequence> Sequences { get; init; } = System.Array.Empty<Sequence>();

		/// <summary>The named bone masks its graph blends through.</summary>
		public IReadOnlyList<Mask> Masks { get; init; } = System.Array.Empty<Mask>();

		internal IReadOnlyList<CoreImportedKeyValues3> Descriptions { get; init; }
			= System.Array.Empty<CoreImportedKeyValues3>();

		internal IReadOnlyList<string> Decoders { get; init; } = System.Array.Empty<string>();
		internal IReadOnlyList<Segment> Segments { get; init; } = System.Array.Empty<Segment>();
		internal IReadOnlyList<Channel> Channels { get; init; } = System.Array.Empty<Channel>();

		/// <summary>The clip at a position, rebuilt frame by frame.</summary>
		public Clip Read( int at ) => ReadClip( this, at );

		//A model carries clips nothing asks for - halves of a blend that was replaced, poses kept for an artist to look at. They cost as much to carry as the ones a game plays and answer to no name.
		/// <summary>Which clips a sequence actually plays, so the rest can be left where they are.</summary>
		public HashSet<int> Played()
		{
			var of = new Dictionary<string, int>( System.StringComparer.OrdinalIgnoreCase );

			for ( var i = 0; i < Clips.Count; i++ ) of.TryAdd( Clips[i], i );

			var played = new HashSet<int>();

			foreach ( var sequence in Sequences )
			{
				foreach ( var clip in sequence.Clips )
				{
					if ( clip < 0 || clip >= Names.Count ) continue;

					if ( of.TryGetValue( Names[clip], out var at ) ) played.Add( at );
				}
			}

			return played;
		}

		/// <summary>Clip indices layered over a clip, resolved through sequence names first, then clip names</summary>
		public IReadOnlyList<int> LayersOf( int clip )
		{
			var found = new List<int>();

			if ( clip < 0 || clip >= Clips.Count ) return found;

			foreach ( var sequence in Sequences )
			{
				if ( sequence.Layers.Length == 0 ) continue;

				var plays = false;

				foreach ( var at in sequence.Clips )
				{
					if ( at < 0 || at >= Names.Count ) continue;

					if ( string.Equals( Names[at], Clips[clip], System.StringComparison.OrdinalIgnoreCase ) )
					{
						plays = true;
						break;
					}
				}

				if ( !plays ) continue;

				foreach ( var layer in sequence.Layers )
				{
					var over = Animation( layer );

					if ( over >= 0 && over != clip && !found.Contains( over ) ) found.Add( over );
				}
			}

			return found;
		}

		//The clip a name in the sequence list ends up at, whether the name is a sequence's or a clip's own. -1 where it names neither.
		private int Animation( int name )
		{
			if ( name < 0 || name >= Names.Count ) return -1;

			for ( var i = 0; i < Clips.Count; i++ )
			{
				if ( string.Equals( Clips[i], Names[name], System.StringComparison.OrdinalIgnoreCase ) ) return i;
			}

			foreach ( var sequence in Sequences )
			{
				if ( !string.Equals( sequence.Name, Names[name], System.StringComparison.OrdinalIgnoreCase ) ) continue;

				foreach ( var at in sequence.Clips )
				{
					if ( at < 0 || at >= Names.Count ) continue;

					for ( var i = 0; i < Clips.Count; i++ )
					{
						if ( string.Equals( Clips[i], Names[at], System.StringComparison.OrdinalIgnoreCase ) ) return i;
					}
				}
			}

			return -1;
		}

		/// <summary>What a sequence is called, for the clips it plays.</summary>
		public IReadOnlyList<string> SequencesOf( int clip )
		{
			var named = new List<string>();

			if ( clip < 0 || clip >= Clips.Count ) return named;

			foreach ( var sequence in Sequences )
			{
				foreach ( var at in sequence.Clips )
				{
					if ( at < 0 || at >= Names.Count ) continue;

					if ( string.Equals( Names[at], Clips[clip], System.StringComparison.OrdinalIgnoreCase ) )
					{
						named.Add( sequence.Name );
						break;
					}
				}
			}

			return named;
		}

		/// <summary>The clip of a name, or null where there is none.</summary>
		public Clip Read( string name )
		{
			for ( var i = 0; i < Clips.Count; i++ )
			{
				if ( string.Equals( Clips[i], name, System.StringComparison.OrdinalIgnoreCase ) )
					return Read( i );
			}

			return null;
		}
	}

	/// <summary>Read what a compiled model carries, or null where it carries nothing.</summary>
	public static Animations Open( byte[] bytes, System.Func<string, byte[]> find = null )
	{
		var resource = CoreImportedResource.Open( bytes );
		if ( resource is null ) return null;

		var anim = Block( resource, "ANIM" );
		var group = Block( resource, "AGRP" );
		var sequences = Block( resource, "ASEQ" );

		if ( find is not null && ( anim is null || group is null || sequences is null ) )
		{
			Elsewhere( resource, find, ref anim, ref group, ref sequences );
		}

		if ( anim is null && group is null && sequences is null ) return null;

		var skeleton = Skeleton( group );
		var channels = Channels( group, skeleton );

		var descriptions = anim?.Array( "m_animArray" ) ?? System.Array.Empty<CoreImportedKeyValues3>();

		var names = new List<string>( descriptions.Count );
		foreach ( var description in descriptions ) names.Add( description.String( "m_name" ) ?? "" );

		var decoders = new List<string>();
		foreach ( var decoder in anim?.Array( "m_decoderArray" ) ?? System.Array.Empty<CoreImportedKeyValues3>() )
			decoders.Add( decoder.String( "m_szName" ) ?? "" );

		var segments = new List<Segment>();
		foreach ( var segment in anim?.Array( "m_segmentArray" ) ?? System.Array.Empty<CoreImportedKeyValues3>() )
			segments.Add( new Segment( segment.Int( "m_nLocalChannel", -1 ), segment.Bytes( "m_container" ) ) );

		return new Animations
		{
			Skeleton = skeleton,
			Clips = names,
			Names = Names( sequences ) ?? new List<string>(),
			Sequences = Sequences( sequences ) ?? new List<Sequence>(),
			Masks = Masks( sequences ) ?? new List<Mask>(),
			Descriptions = descriptions,
			Decoders = decoders,
			Segments = segments,
			Channels = channels,
		};
	}

	//The parsed contents of a block, or null where there is none.
	private static CoreImportedKeyValues3 Block( CoreImportedResource resource, string name )
		=> resource?.Find( name ) is { } block ? CoreImportedKeyValues3.Read( resource.Read( block ) ) : null;

	//Follows a model's animation group to its side files for key, clips and sequences
	private static void Elsewhere( CoreImportedResource model, System.Func<string, byte[]> find,
		ref CoreImportedKeyValues3 anim, ref CoreImportedKeyValues3 group, ref CoreImportedKeyValues3 sequences )
	{
		var data = Block( model, "DATA" );

		foreach ( var named in Named( data, "m_refAnimGroups" ) )
		{
			if ( Open( find, named ) is not { } opened ) continue;

			group ??= Block( opened, "DATA" );

			//The group names its clips, and the sequences beside them - the second of those by reference
			//rather than by a field, which is why what it names is read rather than what it says.
			foreach ( var clips in Named( group, "m_localHAnimArray" ) )
			{
				if ( anim is not null ) break;

				if ( Open( find, clips ) is { } file ) anim = Block( file, "DATA" );
			}

			foreach ( var reference in opened.References() )
			{
				if ( sequences is not null ) break;
				if ( !reference.EndsWith( ".vseq", System.StringComparison.OrdinalIgnoreCase ) ) continue;

				if ( Open( find, reference ) is { } file ) sequences = Block( file, "DATA" );
			}
		}

		foreach ( var named in Named( data, "m_refSequenceGroups" ) )
		{
			if ( sequences is not null ) break;

			if ( Open( find, named ) is { } file ) sequences = Block( file, "DATA" );
		}
	}

	//The paths an array of names holds, skipping the empty ones.
	private static IEnumerable<string> Named( CoreImportedKeyValues3 node, string key )
	{
		foreach ( var found in node?.Array( key ) ?? System.Array.Empty<CoreImportedKeyValues3>() )
		{
			var path = found.AsString();
			if ( !string.IsNullOrWhiteSpace( path ) ) yield return path;
		}
	}

	private static CoreImportedResource Open( System.Func<string, byte[]> find, string path )
	{
		try
		{
			return find( path ) is { Length: > 0 } bytes ? CoreImportedResource.Open( bytes ) : null;
		}
		catch ( System.Exception e ) when ( e is System.IO.IOException or System.UnauthorizedAccessException )
		{
			return null;
		}
	}

	//The skeleton out of the decode key, which is the one the clips are written against and not quite the model's - it carries the helpers too.
	private static List<Bone> Skeleton( CoreImportedKeyValues3 group )
	{
		var bones = new List<Bone>();
		if ( group?["m_decodeKey"] is not { } key ) return bones;

		foreach ( var bone in key.Array( "m_boneArray" ) )
		{
			var position = bone["m_pos"];
			var rotation = bone["m_quat"];

			bones.Add( new Bone(
				bone.String( "m_name" ) ?? "",
				bone.Int( "m_parent", -1 ),
				Axes.Vector( new Vector3(
					position?[0]?.AsFloat() ?? 0.0f,
					position?[1]?.AsFloat() ?? 0.0f,
					position?[2]?.AsFloat() ?? 0.0f ) ) * PortUnits.SBoxUnit,
				Axes.Quat( new Rotation(
					rotation?[0]?.AsFloat() ?? 0.0f,
					rotation?[1]?.AsFloat() ?? 0.0f,
					rotation?[2]?.AsFloat() ?? 0.0f,
					rotation?[3]?.AsFloat() ?? 1.0f ) ) ) );
		}

		return bones;
	}

	//Maps each channel's elements to bones by name, since element numbers differ per channel
	private static List<Channel> Channels( CoreImportedKeyValues3 group, List<Bone> skeleton )
	{
		var channels = new List<Channel>();
		if ( group?["m_decodeKey"] is not { } key ) return channels;

		var of = new Dictionary<string, int>( System.StringComparer.OrdinalIgnoreCase );

		for ( var i = 0; i < skeleton.Count; i++ ) of.TryAdd( skeleton[i].Name, i );

		foreach ( var found in key.Array( "m_dataChannelArray" ) )
		{
			var channel = new Channel { Variable = found.String( "m_szVariableName" ) ?? "" };

			var elements = found.Array( "m_nElementIndexArray" );
			var names = found.Array( "m_szElementNameArray" );

			for ( var i = 0; i < elements.Count; i++ )
			{
				var element = elements[i].AsInt( -1 );
				if ( element < 0 ) continue;

				var name = i < names.Count ? names[i].AsString() : null;

				if ( name is not null && of.TryGetValue( name, out var bone ) )
				{
					channel.Bones[element] = bone;
					continue;
				}

				//No name to go on: the channel's own order is the skeleton's.
				if ( i < skeleton.Count ) channel.Bones[element] = i;
			}

			channels.Add( channel );
		}

		return channels;
	}

	//Rebuild a clip: the rest pose everywhere, then every segment laid over it.
	private static Clip ReadClip( Animations animations, int at )
	{
		if ( at < 0 || at >= animations.Descriptions.Count ) return null;

		var description = animations.Descriptions[at];
		var data = description["m_pData"];

		var bones = animations.Skeleton.Count;
		var frames = data?.Int( "m_nFrames" ) ?? 0;

		if ( bones == 0 || frames <= 0 ) return null;

		var flags = description["m_flags"];

		var clip = new Clip
		{
			Name = description.String( "m_name" ) ?? "",
			FrameRate = description.Float( "fps", 30.0f ),
			FrameCount = frames,
			BoneCount = bones,
			Looping = flags?.Bool( "m_bLooping" ) ?? false,
			Delta = flags?.Bool( "m_bDelta" ) ?? false,
			Positions = new Vector3[frames * bones],
			Rotations = new Rotation[frames * bones],
			Moves = new bool[bones],
			Turns = new bool[bones],
		};

		//Everything the clip does not touch is where the skeleton rests, which is most of it: a clip that moves an arm says nothing about the legs.
		for ( var frame = 0; frame < frames; frame++ )
		{
			for ( var bone = 0; bone < bones; bone++ )
			{
				clip.Positions[frame * bones + bone] = animations.Skeleton[bone].Position;
				clip.Rotations[frame * bones + bone] = animations.Skeleton[bone].Rotation;
			}
		}

		foreach ( var block in data.Array( "m_frameblockArray" ) )
		{
			var start = block.Int( "m_nStartFrame" );
			var end = block.Int( "m_nEndFrame" );

			foreach ( var index in block.Array( "m_segmentIndexArray" ) )
			{
				var segment = index.AsInt( -1 );
				if ( segment < 0 || segment >= animations.Segments.Count ) continue;

				Apply( animations, animations.Segments[segment], start, end, clip );
			}
		}

		clip.Delta = ( flags?.Bool( "m_bDelta" ) ?? false ) || IsDifference( clip );
		clip.Movement = Movement( description );

		return clip;
	}

	/// <summary>Applies an additive layer: positions add, rotations compose with the layer second</summary>
	public static void Add( Clip clip, Clip layer )
	{
		if ( clip is null || layer is null ) return;
		if ( layer.FrameCount <= 0 || layer.BoneCount <= 0 ) return;

		var bones = System.Math.Min( clip.BoneCount, layer.BoneCount );

		for ( var frame = 0; frame < clip.FrameCount; frame++ )
		{
			//Their layers are one pose held for the length of whatever they are laid over, so a clip longer than the layer holds its last frame.
			var over = System.Math.Min( frame, layer.FrameCount - 1 );

			for ( var bone = 0; bone < bones; bone++ )
			{
				//A bone the layer says nothing about is a bone it leaves alone. What is stored there is where the skeleton rests, so
				//adding it would add a whole limb's length, and copying it would straighten out whatever the clip underneath was doing.
				var moves = bone >= layer.Moves.Length || layer.Moves[bone];
				var turns = bone >= layer.Turns.Length || layer.Turns[bone];

				if ( !moves && !turns ) continue;

				var at = ( frame * clip.BoneCount ) + bone;
				var from = ( over * layer.BoneCount ) + bone;

				//A difference is added and a pose replaces. Nearly all of theirs are differences - the conversion a borrowed animation needs to fit the skeleton
				//borrowing it - but a handful are a pose laid over an idle, and adding one of those to the pose underneath bends every joint it names twice.
				if ( layer.Delta )
				{
					if ( moves ) clip.Positions[at] += layer.Positions[from];
					if ( turns ) clip.Rotations[at] = clip.Rotations[at] * layer.Rotations[from];

					continue;
				}

				if ( moves ) clip.Positions[at] = layer.Positions[from];
				if ( turns ) clip.Rotations[at] = layer.Rotations[from];
			}
		}
	}

	//Root motion from compiled movement blocks (Source axes), summed to the end position
	private static ForeignBasis Axes => ForeignBasis.Quake;

	private static Vector3 Movement( CoreImportedKeyValues3 description )
	{
		var moved = Vector3.Zero;

		foreach ( var block in description.Array( "m_movementArray" ) )
		{
			var position = block["position"];
			if ( position is null ) continue;

			moved += new Vector3(
				position[0]?.AsFloat() ?? 0.0f,
				position[1]?.AsFloat() ?? 0.0f,
				position[2]?.AsFloat() ?? 0.0f );
		}

		return moved * PortUnits.SBoxUnit;
	}

	//Whether what was decoded is a set of differences rather than poses. See Clip.Delta for why this is measured rather
	//than read. A bone at the origin has no length, so a skeleton of them is not a skeleton - it is what to add to one.
	private static bool IsDifference( Clip clip )
	{
		if ( clip.BoneCount == 0 ) return false;

		var nothing = 0;

		for ( var bone = 0; bone < clip.BoneCount; bone++ )
		{
			if ( clip.Positions[bone].Length > 0.0005f ) continue;
			if ( MathF.Abs( MathF.Abs( clip.Rotations[bone].w ) - 1.0f ) > 0.001f ) continue;

			nothing++;
		}

		//Half sits in the middle of a gap with nothing in it. Across this character's four hundred and seventy four clips
		//the differences run from three fifths of their bones at nothing to all of them, and the poses stop at a tenth.
		return nothing * 2 > clip.BoneCount;
	}

	//How a segment's values are laid out and how wide each one is.
	private readonly record struct Shape( int Stride, bool Static, bool Delta, int Kind )
	{
		/// <summary>Three floats.</summary>
		public const int Full = 0;

		/// <summary>Three halves.</summary>
		public const int Half = 1;

		/// <summary>Six bytes of quaternion.</summary>
		public const int Packed = 2;

		/// <summary>Four floats.</summary>
		public const int Whole = 3;

		public static readonly Shape None = new( 0, false, false, -1 );
	}

	private static Shape ShapeOf( string decoder ) => decoder switch
	{
		"CCompressedStaticFullVector3" => new Shape( 12, true, false, Shape.Full ),
		"CCompressedStaticVector3" => new Shape( 6, true, false, Shape.Half ),
		"CCompressedFullVector3" => new Shape( 12, false, false, Shape.Full ),
		"CCompressedAnimVector3" => new Shape( 6, false, false, Shape.Half ),
		"CCompressedDeltaVector3" => new Shape( 6, false, true, Shape.Half ),

		"CCompressedStaticQuaternion" => new Shape( 6, true, false, Shape.Packed ),
		"CCompressedAnimQuaternion" => new Shape( 6, false, false, Shape.Packed ),
		"CCompressedFullQuaternion" => new Shape( 16, false, false, Shape.Whole ),

		//Floats, colours, booleans - the channels that drive faces and the numbers a graph reads. Nothing this carries yet.
		_ => Shape.None,
	};

	//Lay one segment's values over the frames it covers.
	private static void Apply( Animations animations, Segment segment, int start, int end, Clip clip )
	{
		var container = segment.Container;
		if ( container is null || container.Length < 8 ) return;

		if ( segment.Channel < 0 || segment.Channel >= animations.Channels.Count ) return;

		var channel = animations.Channels[segment.Channel];

		var position = channel.Variable is "Position";
		var rotation = channel.Variable is "Angle" or "Rotation";

		if ( !position && !rotation ) return;

		var decoder = BinaryPrimitives.ReadUInt16LittleEndian( container );
		if ( decoder >= animations.Decoders.Count ) return;

		var shape = ShapeOf( animations.Decoders[decoder] );
		if ( shape.Kind < 0 ) return;

		//A rotation channel written as a vector, or the other way about, is a file we have read wrong rather than one to make sense of.
		if ( rotation != ( shape.Kind is Shape.Packed or Shape.Whole ) ) return;

		var count = BinaryPrimitives.ReadUInt16LittleEndian( container.AsSpan( 4 ) );
		if ( count <= 0 ) return;

		var elements = 8;
		var values = elements + count * 2;

		if ( values > container.Length ) return;

		var bones = clip.BoneCount;

		//A delta segment keeps a full value per element in front of the frames, and what follows is what to add to it.
		var bases = shape.Delta ? values : 0;
		if ( shape.Delta ) values += count * 12;

		for ( var frame = System.Math.Max( start, 0 ); frame <= end && frame < clip.FrameCount; frame++ )
		{
			var local = frame - start;

			for ( var element = 0; element < count; element++ )
			{
				var id = BinaryPrimitives.ReadUInt16LittleEndian( container.AsSpan( elements + element * 2 ) );
				if ( !channel.Bones.TryGetValue( id, out var bone ) || bone >= bones ) continue;

				var offset = shape.Static
					? values + element * shape.Stride
					: values + ( local * count + element ) * shape.Stride;

				if ( offset + shape.Stride > container.Length ) continue;

				var from = container.AsSpan( offset, shape.Stride );
				var into = frame * bones + bone;

				if ( rotation )
				{
					clip.Turns[bone] = true;
					clip.Rotations[into] = Axes.Quat( shape.Kind == Shape.Whole ? Whole( from ) : Packed( from ) );

					continue;
				}

				clip.Moves[bone] = true;

				var moved = shape.Kind == Shape.Full ? Full( from ) : Half( from );

				if ( shape.Delta )
					moved += Full( container.AsSpan( bases + element * 12, 12 ) );

				clip.Positions[into] = Axes.Vector( moved ) * PortUnits.SBoxUnit;
			}
		}
	}

	private static Vector3 Full( ReadOnlySpan<byte> from ) => new(
		BinaryPrimitives.ReadSingleLittleEndian( from ),
		BinaryPrimitives.ReadSingleLittleEndian( from[4..] ),
		BinaryPrimitives.ReadSingleLittleEndian( from[8..] ) );

	private static Vector3 Half( ReadOnlySpan<byte> from ) => new(
		(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( from ) ),
		(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( from[2..] ) ),
		(float)BitConverter.UInt16BitsToHalf( BinaryPrimitives.ReadUInt16LittleEndian( from[4..] ) ) );

	private static Rotation Whole( ReadOnlySpan<byte> from ) => new(
		BinaryPrimitives.ReadSingleLittleEndian( from ),
		BinaryPrimitives.ReadSingleLittleEndian( from[4..] ),
		BinaryPrimitives.ReadSingleLittleEndian( from[8..] ),
		BinaryPrimitives.ReadSingleLittleEndian( from[12..] ) );

	//A rotation in six bytes. Three of the four components at fifteen bits each, and the fourth worked back out of them - a unit rotation
	//only has three numbers in it. Which one was left out is two bits, its sign is a third, and they are the top bits of the three pairs.
	private static Rotation Packed( ReadOnlySpan<byte> from )
	{
		//Fourteen bits of magnitude, and the bit above says whether to take it as it is or a whole range lower - between them a signed fifteen.
		const float scale = 0.70710678f / 16384.0f;

		var a = Component( from[0], from[1], scale );
		var b = Component( from[2], from[3], scale );
		var c = Component( from[4], from[5], scale );

		var left = 1.0f - ( a * a ) - ( b * b ) - ( c * c );

		//Rare bad data: xyz already exceed unit length, so renormalize instead of passing garbage
		if ( left <= 0.0f )
		{
			var length = MathF.Sqrt( ( a * a ) + ( b * b ) + ( c * c ) );

			if ( length <= 0.0f ) return Rotation.Identity;

			a /= length;
			b /= length;
			c /= length;
			left = 0.0f;
		}

		var d = MathF.Sqrt( left );

		if ( ( from[5] & 128 ) != 0 ) d = -d;

		//Which one was left out, as the two top bits say.
		return ( from[1] & 128, from[3] & 128 ) switch
		{
			(0, 0) => new Rotation( a, b, c, d ),
			(0, _) => new Rotation( d, a, b, c ),
			(_, 0) => new Rotation( c, d, a, b ),
			_ => new Rotation( b, c, d, a ),
		};
	}

	private static float Component( byte low, byte high, float scale )
	{
		var value = low | ( ( high & 63 ) << 8 );

		return ( high & 64 ) != 0 ? value * scale : ( value - 16384 ) * scale;
	}
}
