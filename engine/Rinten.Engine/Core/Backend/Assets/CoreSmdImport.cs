using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Rinten;

//Reading Source SMD into the model builder.
internal static unsafe class CoreSmdImport
{
	private const float Degrees = 180.0f / MathF.PI;

	//SMD is Source's format and is written in Source's axes. Positions and normals come over by the basis; the three
	//angles do not, because pitch, yaw and roll mean the same thing in both and only the axes they turn about have moved.
	private static ForeignBasis Axes => ForeignBasis.Quake;

	private sealed class Bone
	{
		public string Name = "bone";
		public int Parent = -1;
		public Vector3 Position;
		public Rotation Rotation = Rotation.Identity;
	}

	private sealed class Group
	{
		public string Material = "";
		public readonly List<Vertex> Vertices = new();
		public readonly List<uint> Indices = new();
		
		/// <summary>Bones as this file numbers them, four per vertex. Turned into the builder's numbering once the skeleton has been merged into it.</summary>
		public readonly List<int> BoneIndices = new();
		public readonly List<byte> BoneWeights = new();
	}

	/// <summary>Reads the file into the builder. Negative means it could not be read; zero means it held no triangles (an animation SMD).</summary>
	public static int Import( CoreAssets.ModelBuilder builder, string path, float scale )
		=> Import( builder, path, scale, null, addSkeleton: true );

	/// <summary>The same, with material remaps, whether to add the skeleton, and what a surface nothing names wears.</summary>
	public static int Import( CoreAssets.ModelBuilder builder, string path, float scale,
		IReadOnlyDictionary<string, string> remaps, bool addSkeleton, string fallback = null )
	{
		if ( builder is null || string.IsNullOrWhiteSpace( path ) ) return -1;

		string[] lines;

		try
		{
			ImportReads.File( path );
			lines = File.ReadAllLines( path );
		}
		catch ( IOException ) { return -1; }
		catch ( UnauthorizedAccessException ) { return -1; }

		return ImportLines( builder, lines, scale, remaps, addSkeleton, fallback );
	}

	static int ImportLines( CoreAssets.ModelBuilder builder, string[] lines, float scale,
		IReadOnlyDictionary<string, string> remaps, bool addSkeleton, string fallback = null )
	{
		var unitScale = scale > 0.0f ? scale : 1.0f;
		var bones = new List<Bone>();
		var groups = new Dictionary<string, Group>( StringComparer.OrdinalIgnoreCase );
		Group current = null;
		var section = "";

		for ( var at = 0; at < lines.Length; at++ )
		{
			var line = lines[at].Trim();
			if ( line.Length == 0 || line[0] == ';' || line[0] == '#' ) continue;

			var lower = line.ToLowerInvariant();

			if ( lower is "nodes" or "skeleton" or "triangles" )
			{
				section = lower;
				continue;
			}

			if ( lower == "end" )
			{
				section = "";
				continue;
			}

			if ( section == "nodes" )
			{
				if ( !TryReadNode( line, out var bone ) ) continue;

				while ( bones.Count <= bone.Index ) bones.Add( new Bone() );

				bones[bone.Index] = bone.Bone;
				continue;
			}

			if ( section == "skeleton" )
			{
				if ( lower.StartsWith( "time ", StringComparison.Ordinal ) ) continue;

				TryReadPose( line, bones, unitScale );
				continue;
			}

			if ( section != "triangles" ) continue;

			if ( !LooksLikeVertex( line ) )
			{
				current = groups.TryGetValue( line, out var known ) ? known : groups[line] = new Group { Material = line };
				continue;
			}

			if ( current is null ) continue;

			if ( !TryReadVertex( line, unitScale, current ) ) continue;
		}

		if ( groups.Count == 0 ) return 0;

		Square( groups );
		Square( bones );

		//No skinning at draw time: bake the rest pose so vertices are in model space.
		if ( !addSkeleton && bones.Count > 0 ) ApplyRestPose( groups, bones );

		//The file's bones, merged into whatever the builder already holds, and which of its bones each of ours turned out to be.
		var map = addSkeleton && bones.Count > 0 ? AddSkeleton( builder, bones ) : null;

		var skinned = map is not null;
		var meshes = 0;

		foreach ( var group in groups.Values )
		{
			if ( group.Indices.Count == 0 ) continue;

			if ( skinned ) PadSkin( group );

			int firstVertex;

			var vertexSpan = CollectionsMarshal.AsSpan( group.Vertices );

			fixed ( Vertex* data = vertexSpan )
			{
				firstVertex = builder.AddVertices( data, group.Vertices.Count );
			}

			if ( skinned )
			{
				var boneSpan = Remapped( group.BoneIndices, map );
				var weightSpan = CollectionsMarshal.AsSpan( group.BoneWeights );

				fixed ( byte* b = boneSpan )
				fixed ( byte* w = weightSpan )
				{
					builder.AddSkin( b, w, group.Vertices.Count );
				}
			}

			int firstIndex;

			var indexSpan = CollectionsMarshal.AsSpan( group.Indices );

			fixed ( uint* data = indexSpan )
			{
				firstIndex = builder.AddIndices( data, group.Indices.Count );
			}

			var material = MaterialOf( group.Material, remaps, fallback );

			builder.AddMesh( string.IsNullOrEmpty( group.Material ) ? "mesh" : group.Material, material, firstIndex, group.Indices.Count, firstVertex, group.Vertices.Count );

			++meshes;
		}

		return meshes;
	}

	/// <summary>Reads the skeleton times into the clip builder. Negative means it could not be read; zero means it held no poses.</summary>
	public static int ImportClip( CoreAnimation.AnimClipBuilder builder, string path, float scale )
	{
		if ( builder is null || string.IsNullOrWhiteSpace( path ) ) return -1;

		string[] lines;

		try
		{
			ImportReads.File( path );
			lines = File.ReadAllLines( path );
		}
		catch ( IOException ) { return -1; }
		catch ( UnauthorizedAccessException ) { return -1; }

		var unitScale = scale > 0.0f ? scale : 1.0f;
		var bones = new List<Bone>();
		var frames = new List<BonePose[]>();
		BonePose[] current = null;
		var section = "";

		for ( var at = 0; at < lines.Length; at++ )
		{
			var line = lines[at].Trim();
			if ( line.Length == 0 || line[0] == ';' || line[0] == '#' ) continue;

			var lower = line.ToLowerInvariant();

			if ( lower is "nodes" or "skeleton" or "triangles" )
			{
				section = lower;
				continue;
			}

			if ( lower == "end" )
			{
				section = "";
				continue;
			}

			if ( section == "nodes" )
			{
				if ( !TryReadNode( line, out var bone ) ) continue;

				while ( bones.Count <= bone.Index ) bones.Add( new Bone() );

				bones[bone.Index] = bone.Bone;
				continue;
			}

			if ( section != "skeleton" ) continue;

			if ( lower.StartsWith( "time ", StringComparison.Ordinal ) )
			{
				if ( !int.TryParse( line.AsSpan( 5 ).Trim(), NumberStyles.Integer,
					CultureInfo.InvariantCulture, out var time ) || time < 0 )
					continue;

				while ( frames.Count <= time )
				{
					frames.Add( frames.Count == 0
						? RestPose( bones.Count )
						: (BonePose[])frames[^1].Clone() );
				}

				current = frames[time];
				continue;
			}

			if ( current is null ) continue;

			TryReadPose( line, current, unitScale );
		}

		if ( bones.Count == 0 || frames.Count == 0 ) return 0;

		Square( bones, frames );

		var order = new List<int>( bones.Count );

		for ( var depth = 0; ; ++depth )
		{
			var added = 0;

			for ( var i = 0; i < bones.Count; i++ )
			{
				if ( DepthOf( bones, i ) != depth ) continue;

				order.Add( i );
				++added;
			}

			if ( added == 0 ) break;
		}

		var remap = new int[bones.Count];
		Array.Fill( remap, -1 );

		for ( var b = 0; b < order.Count; b++ )
		{
			var i = order[b];
			var parent = bones[i].Parent;
			var parentIndex = parent >= 0 && parent < bones.Count ? remap[parent] : -1;

			if ( builder.AddBone( bones[i].Name ?? $"bone{i}", parentIndex ) < 0 )
				return -1;

			remap[i] = b;
		}

		const float rate = 30.0f;

		builder.SetFrameRate( rate );

		var frameCount = frames.Count;
		var track = new float[frameCount * CoreAnimation.AnimClipBuilder.FloatsPerFrame];

		for ( var i = 0; i < bones.Count; i++ )
		{
			var dest = remap[i];
			if ( dest < 0 ) continue;

			for ( var frame = 0; frame < frameCount; frame++ )
			{
				var pose = i < frames[frame].Length ? frames[frame][i] : BonePose.Rest;
				var into = frame * CoreAnimation.AnimClipBuilder.FloatsPerFrame;

				track[into + 0] = pose.Position.x;
				track[into + 1] = pose.Position.y;
				track[into + 2] = pose.Position.z;

				track[into + 3] = pose.Rotation.x;
				track[into + 4] = pose.Rotation.y;
				track[into + 5] = pose.Rotation.z;
				track[into + 6] = pose.Rotation.w;

				track[into + 7] = 1.0f;
				track[into + 8] = 1.0f;
				track[into + 9] = 1.0f;
			}

			if ( !builder.SetFrames( dest, track, frameCount ) ) return -1;
		}

		return frameCount;
	}

	private readonly struct BonePose
	{
		public Vector3 Position { get; init; }
		public Rotation Rotation { get; init; }

		public static BonePose Rest => new() { Rotation = Rotation.Identity };
	}

	private static BonePose[] RestPose( int count )
	{
		var poses = new BonePose[count];
		for ( var i = 0; i < count; i++ ) poses[i] = BonePose.Rest;
		return poses;
	}

	private static int DepthOf( List<Bone> bones, int index )
	{
		var depth = 0;

		for ( var walk = bones[index].Parent; walk >= 0 && walk < bones.Count && depth <= bones.Count;
			walk = bones[walk].Parent )
			++depth;

		return depth;
	}

	private static string MaterialOf( string name, IReadOnlyDictionary<string, string> remaps, string fallback = null )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return "";

		if ( remaps is not null )
		{
			if ( remaps.TryGetValue( name, out var mapped ) ) return mapped;

			var withExt = name.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase )
				? name
				: name + ".vmat";

			if ( remaps.TryGetValue( withExt, out mapped ) ) return mapped;
			if ( remaps.TryGetValue( Path.GetFileNameWithoutExtension( name ), out mapped ) ) return mapped;
		}

		//The one material the model wears throughout, where it says it has one. Without this the surface name goes through as it
		//stands and the model is drawn in the error material, which is a surface called lowpoly_metal asked for as if it were a file.
		if ( !string.IsNullOrWhiteSpace( fallback ) ) return fallback;

		if ( name.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase )
			|| name.EndsWith( ".mat", StringComparison.OrdinalIgnoreCase ) )
			return Path.ChangeExtension( name.Replace( '\\', '/' ), ".mat" );

		return name.Replace( '\\', '/' );
	}

	private readonly record struct ReadBone( int Index, Bone Bone );

	//Bone name without rig prefix, lowercased, to match the names existing content uses
	private static string BoneName( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) ) return name;

		var dot = name.LastIndexOf( '.' );
		if ( dot >= 0 && dot < name.Length - 1 ) name = name[( dot + 1 )..];

		return name.ToLowerInvariant();
	}

	private static bool TryReadNode( string line, out ReadBone result )
	{
		result = default;

		var tokens = Split( line );
		if ( tokens.Count < 3 ) return false;
		if ( !int.TryParse( tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index ) ) return false;
		if ( !int.TryParse( tokens[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parent ) ) return false;

		var name = BoneName( tokens[1].Trim( '"' ) );
		if ( string.IsNullOrEmpty( name ) ) name = $"bone{index}";

		result = new ReadBone( index, new Bone { Name = name, Parent = parent < 0 ? -1 : parent } );
		return index >= 0;
	}

	private static void TryReadPose( string line, List<Bone> bones, float scale )
	{
		if ( !TryReadPose( line, out var index, out var pose, scale ) ) return;
		if ( index < 0 || index >= bones.Count ) return;

		bones[index].Position = pose.Position;
		bones[index].Rotation = pose.Rotation;
	}

	private static void TryReadPose( string line, BonePose[] poses, float scale )
	{
		if ( !TryReadPose( line, out var index, out var pose, scale ) ) return;
		if ( index < 0 || index >= poses.Length ) return;

		poses[index] = pose;
	}

	private static bool TryReadPose( string line, out int index, out BonePose pose, float scale )
	{
		index = -1;
		pose = BonePose.Rest;

		var tokens = Split( line );
		if ( tokens.Count < 7 ) return false;
		if ( !int.TryParse( tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out index ) ) return false;

		if ( !TryFloat( tokens[1], out var x ) || !TryFloat( tokens[2], out var y ) || !TryFloat( tokens[3], out var z ) )
			return false;

		if ( !TryFloat( tokens[4], out var rx ) || !TryFloat( tokens[5], out var ry ) || !TryFloat( tokens[6], out var rz ) )
			return false;

		//SMD angles are about X, Y, Z; map each to its axis for Rotation.From, not in file order
		pose = new BonePose
		{
			Position = Axes.Vector( new Vector3( x, y, z ) ) * scale,
			Rotation = Rotation.From( ry * Degrees, rz * Degrees, rx * Degrees ),
		};

		return true;
	}

	private static bool TryReadVertex( string line, float scale, Group group )
	{
		var tokens = Split( line );
		if ( tokens.Count < 9 ) return false;

		if ( !TryFloat( tokens[1], out var x ) || !TryFloat( tokens[2], out var y ) || !TryFloat( tokens[3], out var z ) )
			return false;

		if ( !TryFloat( tokens[4], out var nx ) || !TryFloat( tokens[5], out var ny ) || !TryFloat( tokens[6], out var nz ) )
			return false;

		if ( !TryFloat( tokens[7], out var u ) || !TryFloat( tokens[8], out var v ) )
			return false;

		var vertex = new Vertex
		{
			Position = Axes.Vector( new Vector3( x, y, z ) ) * scale,
			Normal = Axes.Vector( new Vector3( nx, ny, nz ) ),

			//SMD V counts up from the bottom; flip it like the other importers do
			TexCoord0 = new Vector4( u, 1.0f - v, 0.0f, 0.0f ),
			Color = Color32.White,
		};

		var index = (uint)group.Vertices.Count;
		group.Vertices.Add( vertex );

		//The file's own numbering, kept whole - it is mapped onto the builder's once the skeleton has been merged
		//in, and a rig with more than a hundred and twenty-eight bones used to be clamped to nonsense here.
		int[] bones = { 0, 0, 0, 0 };
		byte[] weights = { 255, 0, 0, 0 };

		if ( tokens.Count >= 10
			&& int.TryParse( tokens[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out var links )
			&& links > 0 )
		{
			var pairs = new List<(int Bone, float Weight)>();

			for ( var i = 0; i < links && 10 + i * 2 + 1 < tokens.Count; i++ )
			{
				if ( !int.TryParse( tokens[10 + i * 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bone ) )
					continue;

				if ( !TryFloat( tokens[10 + i * 2 + 1], out var weight ) ) continue;

				pairs.Add( (bone < 0 ? 0 : bone, weight) );
			}

			pairs.Sort( ( a, b ) => b.Weight.CompareTo( a.Weight ) );

			var total = 0f;
			var count = Math.Min( 4, pairs.Count );

			for ( var i = 0; i < count; i++ ) total += pairs[i].Weight;

			if ( total <= 0.0f )
			{
				weights[0] = 255;
			}
			else
			{
				var sum = 0;

				for ( var i = 0; i < count; i++ )
				{
					bones[i] = pairs[i].Bone;
					weights[i] = (byte)Math.Clamp( pairs[i].Weight / total * 255.0f, 0, 255 );
					sum += weights[i];
				}

				if ( sum != 255 ) weights[0] = (byte)Math.Clamp( weights[0] + ( 255 - sum ), 0, 255 );
			}
		}
		else if ( int.TryParse( tokens[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parent )
			&& parent > 0 )
		{
			bones[0] = parent;
		}

		group.BoneIndices.AddRange( bones );
		group.BoneWeights.AddRange( weights );
		group.Indices.Add( index );

		return true;
	}

	private static void PadSkin( Group group )
	{
		var vertices = group.Vertices.Count;

		if ( group.BoneIndices.Count == vertices * 4 ) return;

		while ( group.BoneIndices.Count < vertices * 4 )
		{
			group.BoneIndices.Add( 0 );
			group.BoneWeights.Add( group.BoneWeights.Count % 4 == 0 ? (byte)255 : (byte)0 );
		}
	}


	//studiomdl's default quarter turn about up, applied to geometry, skeleton and clips alike
	private static readonly Rotation Squared = Rotation.From( 0.0f, 90.0f, 0.0f );

	//Turns a file's geometry onto the engine's axes.
	private static void Square( Dictionary<string, Group> groups )
	{
		foreach ( var group in groups.Values )
		{
			for ( var i = 0; i < group.Vertices.Count; i++ )
			{
				var vertex = group.Vertices[i];

				vertex.Position = Squared * vertex.Position;
				vertex.Normal = Squared * vertex.Normal;

				group.Vertices[i] = vertex;
			}
		}
	}

	//Turns a file's skeleton onto them, by its roots - everything below a root is written relative to its parent and comes along on its own.
	private static void Square( List<Bone> bones )
	{
		for ( var i = 0; i < bones.Count; i++ )
		{
			if ( bones[i].Parent >= 0 ) continue;

			bones[i].Position = Squared * bones[i].Position;
			bones[i].Rotation = Squared * bones[i].Rotation;
		}
	}

	//The same, for one frame of a clip.
	private static void Square( List<Bone> bones, List<BonePose[]> frames )
	{
		for ( var i = 0; i < bones.Count; i++ )
		{
			if ( bones[i].Parent >= 0 ) continue;

			foreach ( var frame in frames )
			{
				if ( i >= frame.Length ) continue;

				frame[i] = new BonePose
				{
					Position = Squared * frame[i].Position,
					Rotation = Squared * frame[i].Rotation,
				};
			}
		}
	}

	private static Transform[] RestPoseWorld( List<Bone> bones )
	{
		var rest = new Transform[bones.Count];

		for ( var i = 0; i < bones.Count; i++ )
		{
			var local = new Transform( bones[i].Position, bones[i].Rotation );

			rest[i] = bones[i].Parent >= 0 && bones[i].Parent < i
				? rest[bones[i].Parent].ToWorld( local )
				: local;
		}

		return rest;
	}

	private static void ApplyRestPose( Dictionary<string, Group> groups, List<Bone> bones )
	{
		var rest = RestPoseWorld( bones );
		if ( rest.Length == 0 ) return;

		foreach ( var group in groups.Values )
		{
			for ( var i = 0; i < group.Vertices.Count; i++ )
			{
				var bone = 0;
				if ( i * 4 < group.BoneIndices.Count ) bone = group.BoneIndices[i * 4];
				if ( (uint)bone >= (uint)rest.Length ) bone = 0;

				var vertex = group.Vertices[i];
				vertex.Position = rest[bone].PointToWorld( vertex.Position );
				vertex.Normal = rest[bone].NormalToWorld( vertex.Normal );
				group.Vertices[i] = vertex;
			}
		}
	}

	//Adds the file's skeleton, reusing same-named builder bones; returns the file-to-builder bone map
	private static int[] AddSkeleton( CoreAssets.ModelBuilder builder, List<Bone> bones )
	{
		var rest = RestPoseWorld( bones );
		var map = new int[bones.Count];

		for ( var i = 0; i < bones.Count; i++ )
		{
			var name = bones[i].Name ?? $"bone{i}";

			var held = builder.FindBone( name );
			if ( held >= 0 )
			{
				map[i] = held;
				continue;
			}

			var world =
				System.Numerics.Matrix4x4.CreateFromQuaternion( new System.Numerics.Quaternion(
					rest[i].Rotation.x, rest[i].Rotation.y, rest[i].Rotation.z, rest[i].Rotation.w ) ) *
				System.Numerics.Matrix4x4.CreateTranslation(
					rest[i].Position.x, rest[i].Position.y, rest[i].Position.z );

			if ( !System.Numerics.Matrix4x4.Invert( world, out var inverse ) )
				inverse = System.Numerics.Matrix4x4.Identity;

			var matrix = stackalloc float[16]
			{
				inverse.M11, inverse.M12, inverse.M13, inverse.M14,
				inverse.M21, inverse.M22, inverse.M23, inverse.M24,
				inverse.M31, inverse.M32, inverse.M33, inverse.M34,
				inverse.M41, inverse.M42, inverse.M43, inverse.M44,
			};

			var parent = bones[i].Parent;
			if ( parent >= i || parent < -1 ) parent = -1;

			map[i] = CoreModelBones.Add( builder, name,
				parent >= 0 ? map[parent] : -1, matrix );
		}

		return map;
	}

	//The file's bone numbers as the builder's, one byte each - which is what the skin is stored as.
	private static byte[] Remapped( List<int> indices, int[] map )
	{
		var bytes = new byte[indices.Count];

		for ( var i = 0; i < indices.Count; i++ )
		{
			var bone = indices[i];
			var at = bone >= 0 && bone < map.Length ? map[bone] : 0;

			//Past what a byte holds is a rig with more than two hundred and fifty-six bones, which the
			//format cannot address. The root is wrong but it is somewhere, where the wrap would be anywhere.
			bytes[i] = (byte)Math.Clamp( at, 0, 255 );
		}

		return bytes;
	}

	//A vertex line is nine numbers; checking only the first char misread surfaces like "12gauge"
	private static bool LooksLikeVertex( string line )
	{
		var tokens = Split( line );

		if ( tokens.Count < 9 ) return false;

		for ( var i = 0; i < 9; i++ )
		{
			if ( !TryFloat( tokens[i], out _ ) ) return false;
		}

		return true;
	}

	private static List<string> Split( string line )
	{
		var tokens = new List<string>();
		var cursor = 0;

		while ( cursor < line.Length )
		{
			while ( cursor < line.Length && char.IsWhiteSpace( line[cursor] ) ) cursor++;
			if ( cursor >= line.Length ) break;

			if ( line[cursor] == '"' )
			{
				var start = ++cursor;
				while ( cursor < line.Length && line[cursor] != '"' ) cursor++;
				tokens.Add( line[start..cursor] );
				if ( cursor < line.Length ) cursor++;
				continue;
			}

			var from = cursor;
			while ( cursor < line.Length && !char.IsWhiteSpace( line[cursor] ) ) cursor++;
			tokens.Add( line[from..cursor] );
		}

		return tokens;
	}

	private static bool TryFloat( string text, out float value )
		=> float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out value );
}
