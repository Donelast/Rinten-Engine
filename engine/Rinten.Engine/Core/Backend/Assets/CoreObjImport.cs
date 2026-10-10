using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace Rinten;

//Managed OBJ import, one vertex per distinct index triple; no units, so the caller's scale applies
internal static unsafe class CoreObjImport
{
	//One corner of a face, as the file writes it: an index into each of the three lists, any of which may be missing.
	private readonly record struct Corner( int Position, int TexCoord, int Normal );

	//Everything between two usemtl or o lines is one mesh, because that is the only division the format has that a renderer cares about.
	private sealed class Group
	{
		public string Name = "mesh";
		public string Material = "";
		public readonly List<uint> Indices = new();
	}

	//A vertex out of the file. The format's basis is the engine's, so this only carries the scale.
	private static Vector3 Read( float x, float y, float z, float scale )
		=> new( x * scale, y * scale, z * scale );

	/// <summary>Reads the file into the builder and says how many meshes came out. Negative means it could not be read at all; zero means it held nothing.</summary>
	public static int Import( CoreAssets.ModelBuilder builder, string path, float scale )
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

		var positions = new List<Vector3>();
		var normals = new List<Vector3>();
		var texCoords = new List<Vector2>();

		var vertices = new List<Vertex>();
		var seen = new Dictionary<Corner, uint>();

		var groups = new List<Group> { new() };

		//What the material library beside the file says each material is, by the name usemtl calls it.
		var surfaces = new Dictionary<string, ModelFormat.Surface>( StringComparer.Ordinal );

		//A face's corners, reused: a polygon is fanned into triangles and most faces are three or four corners long.
		var corners = new List<Corner>();

		uint VertexFor( in Corner corner )
		{
			if ( seen.TryGetValue( corner, out var known ) ) return known;

			var vertex = new Vertex { Color = Color32.White };

			if ( corner.Position >= 0 && corner.Position < positions.Count )
				vertex.Position = positions[corner.Position];

			if ( corner.Normal >= 0 && corner.Normal < normals.Count )
				vertex.Normal = normals[corner.Normal];

			if ( corner.TexCoord >= 0 && corner.TexCoord < texCoords.Count )
			{
				var uv = texCoords[corner.TexCoord];
				vertex.TexCoord0 = new Vector4( uv.x, uv.y, 0.0f, 0.0f );
			}

			//No tangent in the format. Left at zero rather than guessed here: the builder
			//works one out from the triangles, which is the only place that can see them.
			var index = (uint)vertices.Count;

			vertices.Add( vertex );
			seen.Add( corner, index );

			return index;
		}

		for ( var at = 0; at < lines.Length; ++at )
		{
			var line = lines[at].TrimEnd( '\r' );

			//A line can be continued with a backslash, and long faces sometimes are.
			while ( line.EndsWith( '\\' ) && at + 1 < lines.Length )
			{
				line = line[..^1] + lines[++at].TrimEnd( '\r' );
			}

			if ( line.Length == 0 || line[0] == '#' ) continue;

			if ( line.StartsWith( "v ", StringComparison.Ordinal ) )
			{
				Span<float> xyz = stackalloc float[3];

				if ( ReadFloats( line.AsSpan( 2 ), xyz ) == 3 )
					positions.Add( Read( xyz[0], xyz[1], xyz[2], unitScale ) );

				continue;
			}

			if ( line.StartsWith( "vn ", StringComparison.Ordinal ) )
			{
				Span<float> xyz = stackalloc float[3];

				//A direction, so it is not scaled.
				if ( ReadFloats( line.AsSpan( 3 ), xyz ) == 3 )
					normals.Add( Read( xyz[0], xyz[1], xyz[2], 1.0f ) );

				continue;
			}

			if ( line.StartsWith( "vt ", StringComparison.Ordinal ) )
			{
				Span<float> uv = stackalloc float[2];

				if ( ReadFloats( line.AsSpan( 3 ), uv ) >= 1 )
				{
					//OBJ counts V up from the bottom, every renderer here counts it down from the top.
					texCoords.Add( new Vector2( uv[0], 1.0f - uv[1] ) );
				}

				continue;
			}

			if ( line.StartsWith( "mtllib ", StringComparison.Ordinal ) )
			{
				ReadLibrary( Path.Combine( Path.GetDirectoryName( path ) ?? "", line[7..].Trim() ), surfaces );
				continue;
			}

			var material = line.StartsWith( "usemtl ", StringComparison.Ordinal );

			if ( material || line.StartsWith( "o ", StringComparison.Ordinal ) ||
				line.StartsWith( "g ", StringComparison.Ordinal ) )
			{
				var value = line[( material ? 7 : 2 )..].Trim();

				//A group that has drawn nothing yet is renamed rather than replaced: files
				//commonly say `o name` and `usemtl name` in a row, and that is one mesh, not two.
				var current = groups[^1];

				if ( current.Indices.Count == 0 )
				{
					if ( material ) current.Material = value;
					else current.Name = value;

					continue;
				}

				groups.Add( new Group
				{
					Name = material ? current.Name : value,
					Material = material ? value : current.Material,
				} );

				continue;
			}

			if ( line.StartsWith( "f ", StringComparison.Ordinal ) )
			{
				corners.Clear();

				var rest = line.AsSpan( 2 );
				var cursor = 0;

				while ( true )
				{
					var word = NextToken( rest, ref cursor );
					if ( word.IsEmpty ) break;

					corners.Add( ReadCorner( word, positions.Count, texCoords.Count, normals.Count ) );
				}

				if ( corners.Count < 3 ) continue;

				//Fanned from the first corner. Right for a triangle or a quad, which is all anything exports, and not wrong for anything convex.
				var group = groups[^1];

				for ( var i = 1; i + 1 < corners.Count; ++i )
				{
					group.Indices.Add( VertexFor( corners[0] ) );
					group.Indices.Add( VertexFor( corners[i] ) );
					group.Indices.Add( VertexFor( corners[i + 1] ) );
				}
			}
		}

		if ( vertices.Count == 0 ) return 0;

		FillMissingNormals( vertices, groups );

		int firstVertex;

		var vertexSpan = CollectionsMarshal.AsSpan( vertices );

		fixed ( Vertex* data = vertexSpan )
		{
			firstVertex = builder.AddVertices( data, vertices.Count );
		}

		var meshes = 0;

		foreach ( var group in groups )
		{
			if ( group.Indices.Count == 0 ) continue;

			int firstIndex;

			var indexSpan = CollectionsMarshal.AsSpan( group.Indices );

			fixed ( uint* data = indexSpan )
			{
				firstIndex = builder.AddIndices( data, group.Indices.Count );
			}

			var added = builder.AddMesh( string.IsNullOrEmpty( group.Name ) ? "mesh" : group.Name, group.Material, firstIndex, group.Indices.Count, firstVertex, vertices.Count );

			if ( surfaces.TryGetValue( group.Material ?? "", out var surface ) )
				builder.SetMeshSurface( added, surface );

			++meshes;
		}

		return meshes;
	}

	//A file with no normals at all is common enough to be worth answering: every triangle's own normal, added to each of its corners.
	private static void FillMissingNormals( List<Vertex> vertices, List<Group> groups )
	{
		var span = CollectionsMarshal.AsSpan( vertices );

		foreach ( ref readonly var vertex in span )
		{
			if ( vertex.Normal != Vector3.Zero ) return;
		}

		foreach ( var group in groups )
		{
			for ( var i = 0; i + 2 < group.Indices.Count; i += 3 )
			{
				ref var a = ref span[(int)group.Indices[i]];
				ref var b = ref span[(int)group.Indices[i + 1]];
				ref var c = ref span[(int)group.Indices[i + 2]];

				var normal = Vector3.Cross( b.Position - a.Position, c.Position - a.Position );

				a.Normal += normal;
				b.Normal += normal;
				c.Normal += normal;
			}
		}

		foreach ( ref var vertex in span )
		{
			var length = vertex.Normal.Length;

			vertex.Normal = length <= 0.0f
				? new Vector3( 0.0f, 0.0f, 1.0f )
				: vertex.Normal / length;
		}
	}

	//The next run of non-whitespace, or empty at the end. Any whitespace separates - files mix spaces and tabs freely.
	private static ReadOnlySpan<char> NextToken( ReadOnlySpan<char> at, ref int cursor )
	{
		while ( cursor < at.Length && char.IsWhiteSpace( at[cursor] ) ) ++cursor;

		var start = cursor;

		while ( cursor < at.Length && !char.IsWhiteSpace( at[cursor] ) ) ++cursor;

		return at[start..cursor];
	}

	//Reads an OBJ's .mtl into surface numbers; skips mapped values, roughness from Pr or Ns
	private static void ReadLibrary( string path, Dictionary<string, ModelFormat.Surface> into )
	{
		string[] lines;

		try
		{
			if ( !File.Exists( path ) ) return;

			ImportReads.File( path );
			lines = File.ReadAllLines( path );
		}
		catch ( IOException ) { return; }
		catch ( UnauthorizedAccessException ) { return; }

		string name = null;

		var surface = default( ModelFormat.Surface );
		var colour = new float[] { 1.0f, 1.0f, 1.0f };
		var emission = new float[3];
		var alpha = 1.0f;
		float? roughness = null, shininess = null, metalness = null;
		bool colourSaid = false, colourMap = false, roughnessMap = false, metalnessMap = false, emissionMap = false;

		void Close()
		{
			if ( name is null ) return;

			if ( colourSaid && !colourMap ) surface.SetColour( colour[0], colour[1], colour[2], alpha );

			if ( !roughnessMap )
			{
				if ( roughness is { } rough ) surface.SetRoughness( rough );
				else if ( shininess is { } ns ) surface.SetRoughness( 1.0f - MathF.Sqrt( Math.Clamp( ns / 1000.0f, 0.0f, 1.0f ) ) );
			}

			if ( !metalnessMap && metalness is { } metal ) surface.SetMetalness( metal );
			if ( !emissionMap ) surface.SetEmission( emission[0], emission[1], emission[2], 1.0f );

			into[name] = surface;
		}

		Span<float> read = stackalloc float[3];

		foreach ( var raw in lines )
		{
			var line = raw.Trim();

			if ( line.Length == 0 || line[0] == '#' ) continue;

			var space = line.IndexOfAny( new[] { ' ', '\t' } );
			if ( space < 0 ) continue;

			var word = line[..space];
			var rest = line.AsSpan( space + 1 );

			if ( word == "newmtl" )
			{
				Close();

				name = rest.Trim().ToString();
				surface = default;
				colour = new[] { 1.0f, 1.0f, 1.0f };
				emission = new float[3];
				alpha = 1.0f;
				roughness = shininess = metalness = null;
				colourSaid = colourMap = roughnessMap = metalnessMap = emissionMap = false;

				continue;
			}

			switch ( word )
			{
				case "Kd" when ReadFloats( rest, read ) == 3:
					read.CopyTo( colour );
					colourSaid = true;
					break;

				case "Ke" when ReadFloats( rest, read ) == 3:
					read.CopyTo( emission );
					break;

				case "d" when ReadFloats( rest, read[..1] ) == 1:
					alpha = read[0];
					break;

				//The same thing said the other way up, by the exporters that say it at all.
				case "Tr" when ReadFloats( rest, read[..1] ) == 1:
					alpha = 1.0f - read[0];
					break;

				case "Ns" when ReadFloats( rest, read[..1] ) == 1:
					shininess = read[0];
					break;

				case "Pr" when ReadFloats( rest, read[..1] ) == 1:
					roughness = read[0];
					break;

				case "Pm" when ReadFloats( rest, read[..1] ) == 1:
					metalness = read[0];
					break;

				case "map_Kd": colourMap = true; break;
				case "map_Pr": roughnessMap = true; break;
				case "map_Pm": metalnessMap = true; break;
				case "map_Ke": emissionMap = true; break;
			}
		}

		Close();
	}

	private static int ReadFloats( ReadOnlySpan<char> at, Span<float> into )
	{
		var found = 0;
		var cursor = 0;

		while ( found < into.Length )
		{
			var word = NextToken( at, ref cursor );
			if ( word.IsEmpty ) break;

			if ( !float.TryParse( word, NumberStyles.Float, CultureInfo.InvariantCulture, out var value ) )
				break;

			into[found++] = value;
		}

		return found;
	}

	//An index as written: one-based, and negative counting back from the end.
	private static int Resolve( long written, int have )
	{
		if ( written > 0 ) return (int)written - 1;
		if ( written < 0 ) return have + (int)written;

		return -1;
	}

	//Reads "12", "12/3", "12//4" or "12/3/4".
	private static Corner ReadCorner( ReadOnlySpan<char> word, int positions, int texCoords, int normals )
	{
		var position = -1;
		var texCoord = -1;
		var normal = -1;

		var first = word;
		var slash = word.IndexOf( '/' );

		if ( slash >= 0 )
		{
			first = word[..slash];
			var rest = word[( slash + 1 )..];

			var second = rest;
			var next = rest.IndexOf( '/' );

			if ( next >= 0 )
			{
				second = rest[..next];

				var third = rest[( next + 1 )..];

				if ( long.TryParse( third, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n ) )
					normal = Resolve( n, normals );
			}

			if ( long.TryParse( second, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t ) )
				texCoord = Resolve( t, texCoords );
		}

		if ( long.TryParse( first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p ) )
			position = Resolve( p, positions );

		return new Corner( position, texCoord, normal );
	}
}
