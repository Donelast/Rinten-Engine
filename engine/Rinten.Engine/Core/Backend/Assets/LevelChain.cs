using System.Collections.Generic;
using System.Runtime.InteropServices;
using Rinten.Meshlets;

namespace Rinten;

//A model's LOD chain: each level simplifies the whole previous level, with solved new vertices
internal static unsafe class LevelChain
{
	//What a model's clusters come to: every mesh's clusters, the vertices they name (absolute, into the model's vertices) and their triangles.
	internal sealed class Build
	{
		public readonly List<ModelFormat.Cluster> Clusters = new();
		public readonly List<uint> Vertices = new();
		public readonly List<byte> Triangles = new();
		public uint Levels;

		/// <summary>The vertices the coarse levels made for themselves, to go after the model's own: a cluster names the first of these as the model's vertex count, the next as one more. And for each the model's vertex it came down from, whose bones and blend shapes it takes.</summary>
		public readonly List<Vertex> Extra = new();
		public readonly List<uint> ExtraFrom = new();
	}

	//Where a mesh's material cuts holes in it: the alpha of the picture it is cut out by and the value under which a texel is a hole.
	internal sealed class Cutout
	{
		private readonly byte[] alpha;
		private readonly int width, height;
		private readonly byte cutoff;

		public Cutout( byte[] alpha, int width, int height, byte cutoff )
		{
			this.alpha = alpha;
			this.width = width;
			this.height = height;
			this.cutoff = cutoff;
		}

		/// <summary>Whether the picture is cut away at a texture coordinate - the picture repeats, as the sampler's does.</summary>
		public bool Hole( float u, float v )
		{
			u -= MathF.Floor( u );
			v -= MathF.Floor( v );

			var x = Math.Min( (int)( u * width ), width - 1 );
			var y = Math.Min( (int)( v * height ), height - 1 );

			return alpha[( y * width ) + x] < cutoff;
		}
	}

	//A card: an isolated, mostly cut-out piece (leaf, frond) already at minimal triangles; gets thinned
	private const int CardTriangles = 8;
	private const float CardHoles = 0.1f;

	//Error of a card-thinned level as a share of the remaining cards' width
	private const float CardErrorShare = 0.15f;

	//The power of "cards there were over cards kept", by area, that the kept ones grow by. A half is what keeps the area.
	private const float CardFill = 0.5f;

	private const int MostVertices = 128;
	private const int MostTriangles = 124;
	private const float ConeWeight = 0.25f;
	private const float Infinite = 1e30f;

	//What the simplifier is told about a vertex besides where it is: the normal and both texture coordinates, seven floats a vertex.
	private const int AttributeFloats = 7;

	//Weights of normal and UV changes in collapse error, relative to mesh size; clamped by ErrorClamped
	private const float NormalWeight = 0.5f;
	private const float UvWeight = 0.5f;

	//Attributes whose discontinuities are seams to keep: both UV sets; normal breaks may collapse
	private const uint ProtectedAttributes = ( 1u << 3 ) | ( 1u << 4 ) | ( 1u << 5 ) | ( 1u << 6 );

	//One cluster while the chain is built: what the file gets, its vertices as the mesh's own (local numbers),
	//its triangles into those, and its corners as the mesh's own numbers for the simplifying above it.
	private sealed class Made
	{
		public ModelFormat.Cluster Out;

		/// <summary>The mesh it was cut from - a chain's level holds every mesh's clusters.</summary>
		public ushort Part;
		public uint[] Verts;
		public byte[] Tris;
		public uint[] Corners;
	}

	//Packs a list of indices to its own vertices, so a pass walks the few thousand a step touches rather than the whole model.
	private sealed class Compact
	{
		public uint[] Mark;
		public uint[] Local = System.Array.Empty<uint>();
		public readonly List<uint> ToGlobal = new();
		public readonly List<Vector3> Positions = new();
		public readonly List<float> Attributes = new();

		public Compact( int vertexCount )
		{
			Mark = new uint[vertexCount];
			System.Array.Fill( Mark, uint.MaxValue );
		}

		/// <summary><paramref name="also"/>, when given, is AttributeFloats a vertex at the same numbering as <paramref name="from"/>.</summary>
		public void Take( ReadOnlySpan<uint> what, ReadOnlySpan<Vector3> from, ReadOnlySpan<float> also = default )
		{
			//The numbering grows as the levels make vertices.
			if ( Mark.Length < from.Length )
			{
				var was = Mark.Length;
				System.Array.Resize( ref Mark, System.Math.Max( from.Length, was * 2 ) );
				System.Array.Fill( Mark, uint.MaxValue, was, Mark.Length - was );
			}

			if ( Local.Length < what.Length ) Local = new uint[what.Length];
			ToGlobal.Clear();
			Positions.Clear();
			Attributes.Clear();

			for ( var k = 0; k < what.Length; ++k )
			{
				var v = what[k];
				if ( Mark[v] == uint.MaxValue )
				{
					Mark[v] = (uint)ToGlobal.Count;
					ToGlobal.Add( v );
					Positions.Add( from[(int)v] );

					if ( !also.IsEmpty )
						for ( var a = 0; a < AttributeFloats; ++a )
							Attributes.Add( also[( (int)v * AttributeFloats ) + a] );
				}
				Local[k] = Mark[v];
			}

			foreach ( var v in ToGlobal ) Mark[v] = uint.MaxValue;
		}
	}

	//The cluster structs carry fixed buffers, which C# only reads off a local without pinning - so these
	//helpers take the struct by value and hand it back, rather than reaching into one that lives on the heap.

	private static ModelFormat.Cluster BoxOf( uint[] verts, ReadOnlySpan<Vector3> positions, ModelFormat.Cluster cluster )
	{
		cluster.boxMin[0] = cluster.boxMin[1] = cluster.boxMin[2] = 1e30f;
		cluster.boxMax[0] = cluster.boxMax[1] = cluster.boxMax[2] = -1e30f;

		foreach ( var v in verts )
		{
			ref readonly var p = ref positions[(int)v];

			for ( var k = 0; k < 3; ++k )
			{
				var c = p[k];
				if ( c < cluster.boxMin[k] ) cluster.boxMin[k] = c;
				if ( c > cluster.boxMax[k] ) cluster.boxMax[k] = c;
			}
		}

		return cluster;
	}

	private static ModelFormat.Cluster Sphere( ModelFormat.Cluster cluster, in MeshBounds.Cluster bounds )
	{
		cluster.centre[0] = bounds.Center.x;
		cluster.centre[1] = bounds.Center.y;
		cluster.centre[2] = bounds.Center.z;
		cluster.radius = bounds.Radius;
		cluster.coneAxis[0] = bounds.ConeAxis.x;
		cluster.coneAxis[1] = bounds.ConeAxis.y;
		cluster.coneAxis[2] = bounds.ConeAxis.z;
		cluster.coneCutoff = bounds.ConeCutoff;
		return cluster;
	}

	//Cuts a list of a mesh's triangles into clusters of one level and appends them to the list, each carrying the level's error.
	private static int Cut( ReadOnlySpan<uint> indices, ReadOnlySpan<Vector3> positions,
		Compact compact, float error, uint level, List<Made> made )
	{
		compact.Take( indices, positions );

		var limit = MeshClusterizer.Bound( indices.Length, MostVertices, MostTriangles );
		var words = MeshClusterizer.BoundWords( indices.Length );
		var runs = new MeshClusterizer.Meshlet[limit + 1];
		var runVertices = new uint[words + MostVertices];
		var runTriangles = new byte[words + ( MostTriangles * 3 )];
		var localPositions = CollectionsMarshal.AsSpan( compact.Positions );

		var got = MeshClusterizer.Build( runs, runVertices, runTriangles, compact.Local.AsSpan( 0, indices.Length ),
			localPositions, MostVertices, MostTriangles, ConeWeight );

		for ( var m = 0; m < got; ++m )
		{
			ref var run = ref runs[m];
			var localVertices = runVertices.AsSpan( (int)run.VertexOffset, (int)run.VertexCount );
			var localTriangles = runTriangles.AsSpan( (int)run.TriangleOffset, (int)run.TriangleCount * 3 );

			var bounds = MeshBounds.OfMeshlet( localVertices, localTriangles, localPositions );

			var one = new Made();
			var cluster = Sphere( default, in bounds );
			cluster.error = error;
			cluster.parentError = Infinite;
			cluster.partLevel = level << 16;

			one.Verts = new uint[run.VertexCount];
			for ( var v = 0; v < one.Verts.Length; ++v )
				one.Verts[v] = compact.ToGlobal[(int)localVertices[v]];

			one.Tris = localTriangles.ToArray();

			one.Corners = new uint[one.Tris.Length];
			for ( var t = 0; t < one.Corners.Length; ++t )
				one.Corners[t] = one.Verts[localTriangles[t]];

			one.Out = cluster;
			made.Add( one );
		}

		return got;
	}

	//A mesh as the levels above nought are made from: its positions and the attributes a collapse is judged by, in its own numbering,
	//the weights those are counted with, and its places welded by position. The lists grow with the vertices the levels make.
	private sealed class Source
	{
		public List<Vector3> Own;
		public List<float> Attributes;
		public float[] Weights;
		public List<uint> Remap;
		public List<Vector3> WeldedPositions;
		public int Welded;
		public int ExtraFirst;
	}

	private static Source Prepare( ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices, Build into )
	{
		var vertexCount = vertices.Length;

		//The mesh's positions and the attributes a collapse is judged by, in the mesh's own numbering
		//- and past the mesh's own count, the vertices the levels make, in the order they are made.
		var own = new List<Vector3>( vertexCount );
		var attributes = new List<float>( vertexCount * AttributeFloats );

		Span<float> uvLow = stackalloc float[2] { 1e30f, 1e30f };
		Span<float> uvHigh = stackalloc float[2] { -1e30f, -1e30f };
		Span<float> uv1Low = stackalloc float[2] { 1e30f, 1e30f };
		Span<float> uv1High = stackalloc float[2] { -1e30f, -1e30f };

		for ( var v = 0; v < vertexCount; ++v )
		{
			ref readonly var one = ref vertices[v];

			own.Add( one.Position );

			//Unit, whatever the file said. A model can arrive with normals that are not unit at all - teto's are ten thousand long - and
			//weighing those as though they were is an error in the hundreds on a model six units across, which no allowance ever accepts.
			var along = ( one.Normal.x * one.Normal.x ) + ( one.Normal.y * one.Normal.y ) + ( one.Normal.z * one.Normal.z );
			var unit = along > 1e-12f ? 1.0f / MathF.Sqrt( along ) : 0.0f;

			attributes.Add( one.Normal.x * unit );
			attributes.Add( one.Normal.y * unit );
			attributes.Add( one.Normal.z * unit );
			attributes.Add( one.TexCoord0.x );
			attributes.Add( one.TexCoord0.y );
			attributes.Add( one.TexCoord1.x );
			attributes.Add( one.TexCoord1.y );

			for ( var k = 0; k < 2; ++k )
			{
				if ( one.TexCoord0[k] < uvLow[k] ) uvLow[k] = one.TexCoord0[k];
				if ( one.TexCoord0[k] > uvHigh[k] ) uvHigh[k] = one.TexCoord0[k];
				if ( one.TexCoord1[k] < uv1Low[k] ) uv1Low[k] = one.TexCoord1[k];
				if ( one.TexCoord1[k] > uv1High[k] ) uv1High[k] = one.TexCoord1[k];
			}
		}

		//Each UV set's range, so weights do not scale with tiling; the lightmap set is weighed on its own
		var uvExtent = 0.0f;
		var uv1Extent = 0.0f;
		for ( var k = 0; k < 2; ++k )
		{
			uvExtent = MathF.Max( uvExtent, uvHigh[k] - uvLow[k] );
			uv1Extent = MathF.Max( uv1Extent, uv1High[k] - uv1Low[k] );
		}
		uvExtent = MathF.Max( uvExtent, 1.0f );
		uv1Extent = MathF.Max( uv1Extent, 1.0f );

		var normalWeight = NormalWeight;
		var uvWeight = UvWeight / uvExtent;
		var uv1Weight = UvWeight / uv1Extent;
		var weights = new float[AttributeFloats]
		{
			normalWeight, normalWeight, normalWeight,
			uvWeight, uvWeight,
			uv1Weight, uv1Weight,
		};

		//And the mesh welded by position, which is what the grouping is done on. Both grow with the vertices the
		//levels make: one that moved is a new place, one that only took new attributes stands where its source does.
		var firstRemap = new uint[vertexCount];
		var firstPositions = own.ToArray();
		var welded = MeshWeld.RemapPositions( firstRemap, indices, firstPositions );
		var firstWelded = new Vector3[welded];
		MeshWeld.Apply<Vector3>( firstWelded, firstPositions, firstRemap );

		var remap = new List<uint>( firstRemap );
		var weldedPositions = new List<Vector3>( firstWelded );

		//Where the vertices this mesh makes start in the list they are kept in.
		var extraFirst = into.Extra.Count;

		return new Source
		{
			Own = own,
			Attributes = attributes,
			Weights = weights,
			Remap = remap,
			WeldedPositions = weldedPositions,
			Welded = welded,
			ExtraFirst = extraFirst,
		};
	}

	//One mesh of a model's chain: its triangles in the model's numbering, and what the model says of it.
	internal sealed record ChainPart( ushort Part, uint[] Indices, Cutout Cutout, int GoneAt, List<OwnLevel> Own );

	/// <summary>Builds the LOD chain by simplifying the whole model per level and appends its clusters</summary>
	public static int BuildChain( ReadOnlySpan<Vertex> vertices, List<ChainPart> parts, Build into,
		int most = 0, float step = 0.5f, bool lockBorder = false )
	{
		var vertexCount = vertices.Length;
		if ( vertexCount == 0 || parts.Count == 0 ) return 0;

		var all = new List<uint>();
		foreach ( var one in parts ) all.AddRange( one.Indices );
		if ( all.Count < 3 ) return 0;

		var prepared = Prepare( vertices, CollectionsMarshal.AsSpan( all ), into );
		var own = prepared.Own;
		var attributes = prepared.Attributes;
		var remap = prepared.Remap;
		var weldedPositions = prepared.WeldedPositions;
		var extraFirst = prepared.ExtraFirst;
		var extraBase = (uint)vertexCount + (uint)extraFirst;
		var weights = prepared.Weights;

		//Which of the parts each vertex is in; a vertex a level makes is in its source's.
		var partOf = new List<int>( vertexCount );
		for ( var v = 0; v < vertexCount; ++v ) partOf.Add( -1 );
		for ( var p = 0; p < parts.Count; ++p )
			foreach ( var v in parts[p].Indices ) partOf[(int)v] = p;

		var made = new List<Made>();
		var compact = new Compact( vertexCount );

		var level = all.ToArray();
		var levelFirst = 0;
		var levelCount = CutLevel( level, 0.0f, 0, parts, partOf, own, compact, made );
		if ( levelCount == 0 ) return 0;

		var levels = 1u;
		var levelError = 0.0f;

		var anyCutout = parts.Exists( x => x.Cutout is not null );
		var pieces = anyCutout ? new Pieces() : null;
		var gathered = new List<uint>();
		var solid = new List<uint>();
		var plain = new List<uint>();
		var partCards = new List<Card>();
		var cards = new List<(Card Card, List<uint> From)>();
		var cardVertex = new Dictionary<uint, uint>();
		var movedTo = new Dictionary<(float, float, float), uint>();
		var changed = System.Array.Empty<byte>();
		var standsFor = System.Array.Empty<uint>();
		Span<float> carried = stackalloc float[AttributeFloats];

		while ( levels < MostChainLevels && ( most == 0 || levels < most ) &&
			( level.Length / 3 > MostTriangles || parts.Exists( x => x.Own?.Exists( o => o.Level >= (int)levels ) == true ) ) )
		{
			//A part gone from this level on is not in it.
			gathered.Clear();

			for ( var t = 0; t + 2 < level.Length; t += 3 )
			{
				var p = partOf[(int)level[t]];
				if ( p >= 0 && parts[p].GoneAt >= 0 && levels >= (uint)parts[p].GoneAt ) continue;

				gathered.Add( level[t] );
				gathered.Add( level[t + 1] );
				gathered.Add( level[t + 2] );
			}

			//The cards of each cut-out part out first; the rest is simplified together.
			solid.Clear();
			cards.Clear();

			var cardError = 0.0f;
			var cardCorners = 0;

			pieces?.Of( made, levelFirst, levelCount, remap, weldedPositions.Count );

			for ( var p = 0; p < parts.Count; ++p )
			{
				var mine = new List<uint>();

				for ( var t = 0; t + 2 < gathered.Count; t += 3 )
				{
					if ( partOf[(int)gathered[t]] != p ) continue;
					mine.Add( gathered[t] );
					mine.Add( gathered[t + 1] );
					mine.Add( gathered[t + 2] );
				}

				if ( mine.Count == 0 ) continue;

				partCards.Clear();
				if ( parts[p].Cutout is not null && pieces is not null )
					pieces.Split( mine, remap, own, attributes, parts[p].Cutout, plain, partCards );

				if ( partCards.Count == 0 )
				{
					solid.AddRange( mine );
					continue;
				}

				solid.AddRange( plain );
				cardCorners += Thin( partCards, out var thinned );
				cardError = MathF.Max( cardError, thinned );

				foreach ( var card in partCards ) cards.Add( ( card, mine ) );
			}

			var solidArray = CollectionsMarshal.AsSpan( solid );
			compact.Take( solidArray, CollectionsMarshal.AsSpan( own ), CollectionsMarshal.AsSpan( attributes ) );

			var simplified = new uint[solid.Count + cardCorners];
			var localCount = compact.ToGlobal.Count;
			if ( changed.Length < localCount ) changed = new byte[localCount];
			if ( standsFor.Length < localCount ) standsFor = new uint[localCount];

			compact.Local.AsSpan( 0, solidArray.Length ).CopyTo( simplified );

			var localPositions = CollectionsMarshal.AsSpan( compact.Positions );
			var localAttributes = CollectionsMarshal.AsSpan( compact.Attributes );

			var kept = solidArray.Length;
			var error = 0.0f;

			//The artist's own meshes for this level, where there are any: the simplifier still runs, for the error the level stands in with.
			var mineAt = new Dictionary<int, OwnLevel>();
			for ( var p = 0; p < parts.Count; ++p )
				if ( parts[p].Own?.Find( x => x.Level == (int)levels ) is { } found ) mineAt[p] = found;

			if ( solidArray.Length >= 6 )
			{
				var target = (int)( solidArray.Length * step ) / 3 * 3;

				var options = MeshSimplifier.Options.ErrorAbsolute |
					MeshSimplifier.Options.ErrorClamped | MeshSimplifier.Options.PreserveFolds | MeshSimplifier.Options.Permissive;

				//A small piece is dropped whole - a rivet, a pebble - unless the model has holes cut in it, where a small piece is a leaf.
				if ( !anyCutout ) options |= MeshSimplifier.Options.Prune;

				//A piece of something larger - a terrain chunk - keeps its edge where its neighbour meets it, at every level of either.
				if ( lockBorder ) options |= MeshSimplifier.Options.LockBorder;

				kept = MeshSimplifier.SimplifyWithUpdate( simplified.AsSpan( 0, solidArray.Length ),
					localPositions, localAttributes, AttributeFloats, weights,
					target, Infinite, options,
					changed.AsSpan( 0, localCount ), out error, ProtectedAttributes, default );
			}
			else
			{
				changed.AsSpan( 0, localCount ).Clear();
			}

			//A level that would not come down ends the chain.
			if ( mineAt.Count == 0 && ( kept + cardCorners == 0 || kept + cardCorners > ( gathered.Count * 3 ) / 4 ) ) break;

			for ( var l = 0; l < localCount; ++l ) standsFor[l] = uint.MaxValue;

			movedTo.Clear();

			for ( var k = 0; k < kept; ++k )
			{
				var local = (int)simplified[k];

				if ( standsFor[local] == uint.MaxValue )
				{
					var from = compact.ToGlobal[local];

					if ( changed[local] != 0 )
					{
						standsFor[local] = Add( vertices, vertexCount, 0, from, localPositions[local],
							localAttributes.Slice( local * AttributeFloats, AttributeFloats ),
							own, attributes, remap, weldedPositions, movedTo, into, extraFirst );
						partOf.Add( partOf[(int)from] );
					}
					else
					{
						standsFor[local] = from;
					}
				}

				simplified[k] = standsFor[local];
			}

			cardVertex.Clear();

			foreach ( var (card, from) in cards )
			{
				if ( !card.Kept ) continue;

				foreach ( var triangle in card.Triangles )
				{
					for ( var c = 0; c < 3; ++c )
					{
						var source = from[( triangle * 3 ) + c];

						if ( card.Growth == 1.0f )
						{
							simplified[kept++] = source;
							continue;
						}

						if ( !cardVertex.TryGetValue( source, out var grown ) )
						{
							var was = own[(int)source];

							CollectionsMarshal.AsSpan( attributes ).Slice( (int)source * AttributeFloats, AttributeFloats ).CopyTo( carried );

							grown = Add( vertices, vertexCount, 0, source, card.Middle + ( ( was - card.Middle ) * card.Growth ),
								carried, own, attributes, remap, weldedPositions, movedTo, into, extraFirst );
							partOf.Add( partOf[(int)source] );

							cardVertex[source] = grown;
						}

						simplified[kept++] = grown;
					}
				}
			}

			if ( cardError > error ) error = cardError;
			if ( error > levelError ) levelError = error;

			//The level below is told this one's error as its parent's.
			for ( var c = 0; c < levelCount; ++c )
			{
				var below = made[levelFirst + c].Out;
				below.parentError = levelError;
				made[levelFirst + c].Out = below;
			}

			var next = new List<uint>( kept );

			for ( var k = 0; k + 2 < kept; k += 3 )
			{
				//A part the artist made this level of is that mesh, not what the simplifier left of it.
				if ( mineAt.ContainsKey( partOf[(int)simplified[k]] ) ) continue;

				next.Add( simplified[k] );
				next.Add( simplified[k + 1] );
				next.Add( simplified[k + 2] );
			}

			foreach ( var (p, mine) in mineAt )
			{
				var first = own.Count;
				next.AddRange( Adopt( mine, own, attributes, remap, weldedPositions, into ) );
				for ( var v = first; v < own.Count; ++v ) partOf.Add( p );
			}

			level = next.ToArray();
			levelFirst = made.Count;
			levelCount = CutLevel( level, levelError, levels, parts, partOf, own, compact, made );

			if ( levelCount == 0 ) break;

			levels++;
		}

		//Out to the file's numbering: the model's own vertices, then the ones the levels made.
		var ownSpan = CollectionsMarshal.AsSpan( own );

		foreach ( var one in made )
		{
			var cluster = BoxOf( one.Verts, ownSpan, one.Out );

			cluster.vertexOffset = (uint)into.Vertices.Count;
			cluster.triangleOffset = (uint)( into.Triangles.Count / 3 );
			cluster.counts = (uint)one.Verts.Length | ( (uint)( one.Tris.Length / 3 ) << 16 );
			cluster.partLevel |= one.Part;

			foreach ( var v in one.Verts )
				into.Vertices.Add( v < (uint)vertexCount ? v : extraBase + ( v - (uint)vertexCount ) );

			into.Triangles.AddRange( one.Tris );
			into.Clusters.Add( cluster );
		}

		if ( levels > into.Levels ) into.Levels = levels;

		return made.Count;
	}

	//A level of a chain cut into clusters a part at a time, each told its part.
	private static int CutLevel( uint[] triangles, float error, uint number, List<ChainPart> parts, List<int> partOf,
		List<Vector3> own, Compact compact, List<Made> made )
	{
		var byPart = new List<uint>[parts.Count];

		for ( var t = 0; t + 2 < triangles.Length; t += 3 )
		{
			var p = partOf[(int)triangles[t]];
			if ( p < 0 ) continue;

			var list = byPart[p] ??= new List<uint>();
			list.Add( triangles[t] );
			list.Add( triangles[t + 1] );
			list.Add( triangles[t + 2] );
		}

		var cut = 0;

		for ( var p = 0; p < parts.Count; ++p )
		{
			if ( byPart[p] is null ) continue;

			var first = made.Count;
			cut += Cut( CollectionsMarshal.AsSpan( byPart[p] ), CollectionsMarshal.AsSpan( own ), compact, error, number, made );
			for ( var m = first; m < made.Count; ++m ) made[m].Part = parts[p].Part;
		}

		return cut;
	}

	//An artist's mesh brought into a chain's numbering: every vertex of it a new vertex of the
	//chain's, taking its bones and blend shapes from itself. Returns its triangles in that numbering.
	private static uint[] Adopt( OwnLevel mine, List<Vector3> own, List<float> attributes, List<uint> remap,
		List<Vector3> weldedPositions, Build into )
	{
		var first = (uint)own.Count;

		for ( var v = 0; v < mine.Vertices.Length; ++v )
		{
			var vertex = mine.Vertices[v];

			var along = vertex.Normal.Length;
			var normal = along > 1e-6f ? vertex.Normal / along : vertex.Normal;

			own.Add( vertex.Position );
			attributes.Add( normal.x );
			attributes.Add( normal.y );
			attributes.Add( normal.z );
			attributes.Add( vertex.TexCoord0.x );
			attributes.Add( vertex.TexCoord0.y );
			attributes.Add( vertex.TexCoord1.x );
			attributes.Add( vertex.TexCoord1.y );

			remap.Add( (uint)weldedPositions.Count );
			weldedPositions.Add( vertex.Position );

			into.Extra.Add( vertex );
			into.ExtraFrom.Add( mine.FirstVertex + (uint)v );
		}

		var level = new uint[mine.Indices.Length / 3 * 3];

		for ( var i = 0; i < level.Length; ++i )
			level[i] = first + Math.Min( mine.Indices[i], (uint)Math.Max( mine.Vertices.Length - 1, 0 ) );

		return level;
	}

	//The most levels a chain is built to: the cull describes eight a mesh and folds the rest into the last, and
	//the impostor, where there is one, is the eighth - folded in with a level of the meshes, both would be drawn.
	private const int MostChainLevels = 7;

	//One card of a mesh's level: its triangles as places in that list, how big it is and where.
	private sealed class Card
	{
		public readonly List<int> Triangles = new();
		public float Area;
		public Vector3 Middle;
		public float Radius;
		public uint Order;
		public bool Kept;
		public float Growth = 1.0f;
	}

	//Every other card of a mesh's level dropped, in an order that runs through its space so what is left is spread as evenly as what
	//was there, and the rest told how much to grow. Returns how many corners the kept ones have, and the error the thinning comes to.
	private static int Thin( List<Card> cards, out float error )
	{
		error = 0.0f;

		//Along a curve through their middles: neighbours in the order are neighbours in space, so taking every other one takes one of each pair.
		var low = new Vector3( float.MaxValue, float.MaxValue, float.MaxValue );
		var high = new Vector3( float.MinValue, float.MinValue, float.MinValue );

		foreach ( var card in cards )
		{
			low = Vector3.Min( low, card.Middle );
			high = Vector3.Max( high, card.Middle );
		}

		var span = high - low;

		foreach ( var card in cards )
		{
			var at = card.Middle - low;

			card.Order = Spread( span.x > 0.0f ? at.x / span.x : 0.0f )
				| ( Spread( span.y > 0.0f ? at.y / span.y : 0.0f ) << 1 )
				| ( Spread( span.z > 0.0f ? at.z / span.z : 0.0f ) << 2 );
		}

		cards.Sort( ( a, b ) => a.Order != b.Order ? a.Order.CompareTo( b.Order ) : a.Triangles[0].CompareTo( b.Triangles[0] ) );

		var all = 0.0f;
		var kept = 0.0f;
		var corners = 0;

		for ( var i = 0; i < cards.Count; ++i )
		{
			cards[i].Kept = ( i & 1 ) == 0;
			all += cards[i].Area;

			if ( cards[i].Kept )
			{
				kept += cards[i].Area;
				corners += cards[i].Triangles.Count * 3;
			}
		}

		//One card cannot be halved; it stays as it is, and nothing is in error.
		if ( cards.Count < 2 || !( kept > 0.0f ) ) return corners;

		var growth = MathF.Pow( all / kept, CardFill );
		var width = 0.0f;

		foreach ( var card in cards )
		{
			if ( !card.Kept ) continue;

			card.Growth = growth;
			width += card.Radius * 2.0f * growth * card.Area;
		}

		//The width of the cards left, weighed by their area: one wide card among a thousand leaves is not what the level looks like.
		error = CardErrorShare * width / kept;

		return corners;
	}

	//Ten bits of a number from nought to one, with two empty bits between each.
	private static uint Spread( float unit )
	{
		var x = (uint)Math.Clamp( (int)( unit * 1023.0f ), 0, 1023 );

		x = ( x | ( x << 16 ) ) & 0x030000FFu;
		x = ( x | ( x << 8 ) ) & 0x0300F00Fu;
		x = ( x | ( x << 4 ) ) & 0x030C30C3u;
		x = ( x | ( x << 2 ) ) & 0x09249249u;

		return x;
	}

	//The pieces of one level - its triangles joined where they share a place - and, a mesh at a time, which of them are cards.
	private sealed class Pieces
	{
		private int[] parent = System.Array.Empty<int>();
		private int[] triangles = System.Array.Empty<int>();
		private readonly Dictionary<int, Card> inGroup = new();
		private readonly Dictionary<int, float> holes = new();
		private readonly List<int> rootOf = new();

		private int Find( int a )
		{
			while ( parent[a] != a )
			{
				parent[a] = parent[parent[a]];
				a = parent[a];
			}

			return a;
		}

		/// <summary>Joins the level's triangles into pieces and counts each piece's triangles.</summary>
		public void Of( List<Made> made, int first, int count, List<uint> remap, int places )
		{
			if ( parent.Length < places )
			{
				parent = new int[places];
				triangles = new int[places];
			}

			for ( var i = 0; i < places; ++i ) parent[i] = i;
			System.Array.Clear( triangles, 0, places );

			for ( var c = 0; c < count; ++c )
			{
				var corners = made[first + c].Corners;

				for ( var t = 0; t + 2 < corners.Length; t += 3 )
				{
					var a = Find( (int)remap[(int)corners[t]] );
					var b = Find( (int)remap[(int)corners[t + 1]] );

					if ( a != b ) parent[b] = a;

					var d = Find( (int)remap[(int)corners[t + 2]] );

					if ( d != a ) parent[d] = a;
				}
			}

			for ( var c = 0; c < count; ++c )
			{
				var corners = made[first + c].Corners;

				for ( var t = 0; t + 2 < corners.Length; t += 3 )
					triangles[Find( (int)remap[(int)corners[t]] )]++;
			}
		}

		/// <summary>A mesh's triangles into its cards and the rest. A card is a piece that is whole within the mesh - not the half of one a neighbour holds the rest of - small, and cut through by its material.</summary>
		public void Split( List<uint> gathered, List<uint> remap, List<Vector3> own, List<float> attributes, Cutout cutout,
			List<uint> plain, List<Card> cards )
		{
			inGroup.Clear();
			holes.Clear();
			rootOf.Clear();
			plain.Clear();

			var count = gathered.Count / 3;

			for ( var t = 0; t < count; ++t )
			{
				var root = Find( (int)remap[(int)gathered[t * 3]] );
				rootOf.Add( root );

				if ( !inGroup.TryGetValue( root, out var card ) ) inGroup[root] = card = new Card();

				card.Triangles.Add( t );

				//Too large to be a card whatever else it is: not measured.
				if ( triangles[root] > CardTriangles ) continue;

				int i0 = (int)gathered[t * 3], i1 = (int)gathered[( t * 3 ) + 1], i2 = (int)gathered[( t * 3 ) + 2];
				Vector3 p0 = own[i0], p1 = own[i1], p2 = own[i2];

				var area = ( p1 - p0 ).Cross( p2 - p0 ).Length * 0.5f;

				card.Area += area;
				card.Middle += ( p0 + p1 + p2 ) * ( area / 3.0f );

				//How much of the triangle its material cuts away: seven places on it - the middle, and toward each corner and each edge.
				float u0 = attributes[( i0 * AttributeFloats ) + 3], v0 = attributes[( i0 * AttributeFloats ) + 4];
				float u1 = attributes[( i1 * AttributeFloats ) + 3], v1 = attributes[( i1 * AttributeFloats ) + 4];
				float u2 = attributes[( i2 * AttributeFloats ) + 3], v2 = attributes[( i2 * AttributeFloats ) + 4];

				var cut = 0;

				for ( var k = 0; k < 7; ++k )
				{
					float a = HoleTaps[k * 3], b = HoleTaps[( k * 3 ) + 1], c = HoleTaps[( k * 3 ) + 2];

					if ( cutout.Hole( ( u0 * a ) + ( u1 * b ) + ( u2 * c ), ( v0 * a ) + ( v1 * b ) + ( v2 * c ) ) ) cut++;
				}

				holes[root] = holes.GetValueOrDefault( root ) + ( area * cut / 7.0f );
			}

			//Which pieces are cards; the rest go back to the simplifier whole.
			foreach ( var ( root, card ) in inGroup )
			{
				var whole = card.Triangles.Count == triangles[root];
				var small = triangles[root] <= CardTriangles;

				if ( whole && small && card.Area > 0.0f && holes.GetValueOrDefault( root ) >= CardHoles * card.Area )
				{
					card.Middle /= card.Area;

					foreach ( var t in card.Triangles )
						for ( var c = 0; c < 3; ++c )
							card.Radius = MathF.Max( card.Radius, ( own[(int)gathered[( t * 3 ) + c]] - card.Middle ).Length );

					cards.Add( card );
				}
				else
				{
					card.Kept = false;
					card.Area = -1.0f;
				}
			}

			if ( cards.Count == 0 ) return;

			//In the mesh's own order, so the simplifier sees them as it would have.
			for ( var t = 0; t < count; ++t )
			{
				if ( inGroup[rootOf[t]].Area >= 0.0f ) continue;

				plain.Add( gathered[t * 3] );
				plain.Add( gathered[( t * 3 ) + 1] );
				plain.Add( gathered[( t * 3 ) + 2] );
			}
		}

		private static readonly float[] HoleTaps =
		{
			1.0f / 3.0f, 1.0f / 3.0f, 1.0f / 3.0f,
			0.6f, 0.2f, 0.2f,
			0.2f, 0.6f, 0.2f,
			0.2f, 0.2f, 0.6f,
			0.45f, 0.45f, 0.1f,
			0.1f, 0.45f, 0.45f,
			0.45f, 0.1f, 0.45f,
		};
	}

	//Adds a vertex created by a level's solve; non-solved data comes from its source vertex
	private static uint Add( ReadOnlySpan<Vertex> vertices, int vertexCount, uint firstVertex, uint source,
		Vector3 position, ReadOnlySpan<float> solved,
		List<Vector3> own, List<float> attributes, List<uint> remap, List<Vector3> weldedPositions,
		Dictionary<(float, float, float), uint> movedTo, Build into, int extraFirst )
	{
		var number = (uint)own.Count;
		var fromExtra = source >= (uint)vertexCount;

		var vertex = fromExtra ? into.Extra[extraFirst + (int)( source - (uint)vertexCount )] : vertices[(int)source];
		var origin = fromExtra ? into.ExtraFrom[extraFirst + (int)( source - (uint)vertexCount )] : firstVertex + source;

		//A normal is a direction: the mean of several is shorter than any of them.
		var normal = new Vector3( solved[0], solved[1], solved[2] );
		var length = normal.Length;
		normal = length > 1e-6f ? normal / length : vertex.Normal.Normal;

		//A place of its own where it moved; its source's where it only took new attributes - a seam's two halves still stand together for the grouping.
		var was = own[(int)source];

		if ( was.x == position.x && was.y == position.y && was.z == position.z )
		{
			remap.Add( remap[(int)source] );
		}
		else if ( movedTo.TryGetValue( ( position.x, position.y, position.z ), out var there ) )
		{
			//Another copy of the same vertex was here first - a hard edge moved with all its copies - and they are one place for the grouping.
			remap.Add( there );
		}
		else
		{
			movedTo[( position.x, position.y, position.z )] = (uint)weldedPositions.Count;
			remap.Add( (uint)weldedPositions.Count );
			weldedPositions.Add( position );
		}

		own.Add( position );

		attributes.Add( normal.x );
		attributes.Add( normal.y );
		attributes.Add( normal.z );
		for ( var a = 3; a < AttributeFloats; ++a ) attributes.Add( solved[a] );

		var tangent = new Vector3( vertex.Tangent.x, vertex.Tangent.y, vertex.Tangent.z );
		tangent -= normal * tangent.Dot( normal );
		var tangentLength = tangent.Length;

		if ( tangentLength > 1e-6f )
		{
			tangent /= tangentLength;
			vertex.Tangent = new Vector4( tangent.x, tangent.y, tangent.z, vertex.Tangent.w );
		}

		vertex.Position = position;
		vertex.Normal = normal;
		vertex.TexCoord0 = new Vector4( solved[3], solved[4], vertex.TexCoord0.z, vertex.TexCoord0.w );
		vertex.TexCoord1 = new Vector4( solved[5], solved[6], vertex.TexCoord1.z, vertex.TexCoord1.w );

		into.Extra.Add( vertex );
		into.ExtraFrom.Add( origin );

		return number;
	}

	/// <summary>Cuts every mesh into clusters: the LOD chain plus the impostor level on top</summary>
	public static void CutClusters( ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices,
		ReadOnlySpan<ModelFormat.Mesh> meshes, Build into, int most = 0, Cutout[] cutouts = null, ChainOptions chain = null )
	{
		chain ??= new ChainOptions( meshes.Length );

		var chainParts = new List<ChainPart>();
		var standing = new List<ChainPart>();

		for ( var i = 0; i < meshes.Length; ++i )
		{
			if ( !Whole( meshes[i], vertices, indices ) ) continue;

			//Another mesh's level is cut as that level, not as a mesh.
			if ( i < chain.LevelOf.Length && chain.LevelOf[i] >= 0 ) continue;

			ref readonly var mesh = ref meshes[i];
			var cutout = cutouts is not null && i < cutouts.Length ? cutouts[i] : null;

			//The artist's own levels of this one, by level.
			List<OwnLevel> ownLevels = null;

			for ( var k = 0; k < meshes.Length && k < chain.LevelOf.Length; ++k )
			{
				if ( chain.LevelOf[k] != i || !Whole( meshes[k], vertices, indices ) ) continue;

				ref readonly var other = ref meshes[k];

				( ownLevels ??= new() ).Add( new OwnLevel( chain.LevelAt[k],
					vertices.Slice( (int)other.firstVertex, (int)other.vertexCount ).ToArray(),
					indices.Slice( (int)other.firstIndex, (int)other.indexCount ).ToArray(), other.firstVertex ) );
			}

			//In the model's numbering: a chain is cut over the whole model at once.
			var model = new uint[mesh.indexCount];
			for ( var k = 0; k < model.Length; ++k ) model[k] = mesh.firstVertex + indices[(int)mesh.firstIndex + k];

			var part = new ChainPart( (ushort)i, model, cutout, i < chain.GoneAt.Length ? chain.GoneAt[i] : -1, ownLevels );

			if ( ( mesh.reserved & (uint)ModelFormat.MeshFlags.StandsIn ) != 0 ) standing.Add( part );
			else chainParts.Add( part );
		}

		if ( chainParts.Count > 0 ) BuildChain( vertices, chainParts, into, most, chain.Step, chain.LockBorder );
		if ( standing.Count > 0 ) BuildChain( vertices, standing, into, 1 );
	}

	private static bool Whole( in ModelFormat.Mesh mesh, ReadOnlySpan<Vertex> vertices, ReadOnlySpan<uint> indices )
		=> mesh.indexCount >= 3 && mesh.vertexCount > 0
			&& (long)mesh.firstVertex + mesh.vertexCount <= vertices.Length
			&& (long)mesh.firstIndex + mesh.indexCount <= indices.Length;

	//What a model says about its chains beyond the meshes themselves, one entry
	//a mesh - see ModelBuilder.SetChainStep, SetMeshLastLevel and SetMeshAsLevel.
	internal sealed class ChainOptions
	{
		public float Step = 0.5f;

		/// <summary>The model is a piece of something larger, whose open edges meet its neighbours - see ModelBuilder.SetLockBorder.</summary>
		public bool LockBorder;

		public readonly int[] GoneAt;
		public readonly int[] LevelOf;
		public readonly int[] LevelAt;

		public ChainOptions( int meshes )
		{
			GoneAt = new int[meshes];
			LevelOf = new int[meshes];
			LevelAt = new int[meshes];
			System.Array.Fill( GoneAt, -1 );
			System.Array.Fill( LevelOf, -1 );
		}
	}

	//A mesh of the model that stands as a level of another's chain: its own vertices and triangles, and where its vertices are in the model.
	internal sealed record OwnLevel( int Level, Vertex[] Vertices, uint[] Indices, uint FirstVertex );
}
