namespace Rinten.Porting.SBox;

//Hammer displacements: subdivides the cage and applies the per-sample offset stream
//Block numbering is undocumented; the mesh picks the one whose shared edges agree, else unsculpted
internal static class SBoxSubdivision
{
	//How deep this is worth following. Four is a 9x9 grid a patch.
	private const int MostLevels = 4;

	//How near two patches have to be about a sample they share before the reading is believed. They
	//should be identical; this is room for the same arithmetic done in two orders, and nothing more.
	private const float Agreement = 0.001f;

	//One face of the cage: what it is made of, and how deep it goes.
	internal readonly record struct Cage( int[] Corners, int[] Edges, int Level )
	{
		//A cage whose half edges are not to hand, which reads its blocks in order.
		internal Cage( int[] corners, int level ) : this( corners, null, level ) { }
	}

	//What came of one mesh, and whether its shape was there to read.
	internal readonly record struct Result( List<int[]>[] Faces, bool Sculpted, string Trouble );

	//One way of reading the stream: which block a patch owns, and which way its grid runs.
	private readonly record struct Reading( string Name, int[] Block, bool Down );

	//Cuts the faces that ask for it and puts Hammer's own offsets on the samples. positions
	//grows with the points that are made; none of the ones already there is moved.
	internal static Result Apply( List<Vector3> positions, IReadOnlyList<Cage> faces, Vector3[] displacement = null )
	{
		var result = new List<int[]>[faces.Count];

		for ( var i = 0; i < faces.Count; i++ )
			result[i] = new List<int[]> { faces[i].Corners };

		var deepest = 0;
		var corners = 0;

		foreach ( var face in faces )
		{
			var level = Math.Clamp( face.Level, 0, MostLevels );
			if ( level == 0 ) continue;

			if ( level > deepest ) deepest = level;
			corners += face.Corners.Length;
		}

		if ( deepest == 0 ) return new Result( result, false, null );

		//One sample every 2^(level-1) + 1 along a patch's side. Level one is a patch with nothing inside it - its four corners and no more.
		var side = ( 1 << ( deepest - 1 ) ) + 1;
		var wanted = corners * side * side;

		var trouble = (string)null;

		if ( displacement is not null && displacement.Length != wanted )
		{
			trouble = $"its shape is {displacement.Length} offsets where {corners} patch(es) at level " +
				$"{deepest} want {wanted} - the surface was cut but not sculpted";

			displacement = null;
		}

		//The cut: one quad per corner, sharing the midpoint of every edge and the middle of every face.
		var midpoints = new Dictionary<(int, int), int>();
		var patches = new List<int[]>( corners );
		var owner = new List<int>( corners );
		var edges = new List<int>( corners );

		for ( var i = 0; i < faces.Count; i++ )
		{
			if ( Math.Clamp( faces[i].Level, 0, MostLevels ) == 0 ) continue;

			var made = new List<int[]>( faces[i].Corners.Length );
			Cut( positions, midpoints, faces[i].Corners, made );

			for ( var c = 0; c < made.Count; c++ )
			{
				patches.Add( made[c] );
				owner.Add( i );
				edges.Add( faces[i].Edges is { } walked && c < walked.Length ? walked[c] : edges.Count );
			}

			result[i] = new List<int[]>();
		}

		var block = (int[])null;
		var down = false;

		if ( displacement is not null )
		{
			if ( Read( positions, patches, edges, side, displacement ) is not { } reading )
			{
				trouble = "its shape does not read: no numbering of the blocks has two patches agreeing " +
					"about the samples they share, so the offsets are laid out some way this does not " +
					"know - the surface was cut but not sculpted";

				displacement = null;
			}
			else
			{
				block = reading.Block;
				down = reading.Down;
			}
		}

		//And the grid on each patch. Welded by where a sample is before it is moved: two patches meeting on an edge
		//work out the same points there and carry the same offsets, so one vertex serves both and there is no seam.
		var welded = new Dictionary<(long, long, long), int>();

		for ( var p = 0; p < patches.Count; p++ )
		{
			Grid( positions, welded, patches[p], side, displacement,
				block is null ? 0 : block[p] * side * side, down, result[owner[p]] );
		}

		return new Result( result, displacement is not null, trouble );
	}

	//Block numbering the stream uses, or null; half-edge order first, must agree exactly everywhere
	private static Reading? Read( List<Vector3> positions, List<int[]> patches, List<int> edges,
		int side, Vector3[] displacement )
	{
		var blocks = displacement.Length / ( side * side );

		var running = new int[patches.Count];
		for ( var p = 0; p < patches.Count; p++ ) running[p] = p;

		//A corner's block is where its half edge comes among the half edges that carry one. The mesh's boundary
		//half edges are not subdivided and hold no block, which is why this is a rank and not the index itself.
		var sorted = new List<int>( edges );
		sorted.Sort();

		var rank = new Dictionary<int, int>( sorted.Count );
		foreach ( var edge in sorted ) if ( !rank.ContainsKey( edge ) ) rank[edge] = rank.Count;

		var ranked = new int[patches.Count];
		for ( var p = 0; p < patches.Count; p++ ) ranked[p] = rank.GetValueOrDefault( edges[p], p );

		foreach ( var (name, candidate) in new[]
		{
			( "the corner's half edge", ranked ),
			( "the half edge itself", edges.ToArray() ),
			( "one after another", running ),
		} )
		{
			var fits = true;

			foreach ( var at in candidate )
				if ( at < 0 || at >= blocks ) { fits = false; break; }

			if ( !fits ) continue;

			for ( var down = 0; down < 2; down++ )
			{
				if ( Disagreement( positions, patches, side, displacement, candidate, down == 1 ) <= Agreement )
					return new Reading( name, candidate, down == 1 );
			}
		}

		return null;
	}

	//The most two patches disagree about a sample they both work out - which for a reading that is right is nothing at all.
	private static float Disagreement( List<Vector3> positions, List<int[]> patches, int side,
		Vector3[] displacement, int[] block, bool down )
	{
		var seen = new Dictionary<(long, long, long), Vector3>();
		var worst = 0.0f;

		for ( var p = 0; p < patches.Count; p++ )
		{
			if ( patches[p].Length < 4 ) continue;

			var q0 = positions[patches[p][0]];
			var q1 = positions[patches[p][1]];
			var q2 = positions[patches[p][2]];
			var q3 = positions[patches[p][3]];

			var at = block[p] * side * side;

			for ( var i = 0; i < side; i++ )
			{
				var v = (float)i / ( side - 1 );

				for ( var j = 0; j < side; j++ )
				{
					var u = (float)j / ( side - 1 );

					var place =
						( q0 * ( 1.0f - u ) * ( 1.0f - v ) ) + ( q1 * u * ( 1.0f - v ) ) +
						( q2 * u * v ) + ( q3 * ( 1.0f - u ) * v );

					var offset = at + ( down ? ( j * side ) + i : ( i * side ) + j );
					if ( offset < 0 || offset >= displacement.Length ) return float.MaxValue;

					var key = Key( place );

					if ( seen.TryGetValue( key, out var had ) )
					{
						var apart = ( had - displacement[offset] ).Length;
						if ( apart > worst ) worst = apart;
						continue;
					}

					seen[key] = displacement[offset];
				}
			}
		}

		return worst;
	}

	//One face into one quad per corner: the corner, the point half way along the edge leaving
	//it, the middle of the face, and the point half way along the edge arriving at it.
	private static void Cut( List<Vector3> positions, Dictionary<(int, int), int> midpoints,
		int[] corners, List<int[]> into )
	{
		if ( corners.Length < 3 )
		{
			into.Add( corners );
			return;
		}

		var centre = Vector3.Zero;
		foreach ( var corner in corners ) centre += positions[corner];

		positions.Add( centre / corners.Length );
		var middle = positions.Count - 1;

		for ( var i = 0; i < corners.Length; i++ )
		{
			var previous = corners[( i + corners.Length - 1 ) % corners.Length];
			var current = corners[i];
			var next = corners[( i + 1 ) % corners.Length];

			into.Add( new[]
			{
				current,
				Midpoint( positions, midpoints, current, next ),
				middle,
				Midpoint( positions, midpoints, previous, current ),
			} );
		}
	}

	//The grid one patch carries: a sample every step across it, moved by whatever Hammer put there, cut into quads.
	private static void Grid( List<Vector3> positions, Dictionary<(long, long, long), int> welded,
		int[] patch, int side, Vector3[] displacement, int at, bool down, List<int[]> into )
	{
		if ( patch.Length < 4 )
		{
			into.Add( patch );
			return;
		}

		var q0 = positions[patch[0]];
		var q1 = positions[patch[1]];
		var q2 = positions[patch[2]];
		var q3 = positions[patch[3]];

		var grid = new int[side * side];

		for ( var i = 0; i < side; i++ )
		{
			//i runs from the cage's corner toward the midpoint behind it, j toward the one in front.
			var v = (float)i / ( side - 1 );

			for ( var j = 0; j < side; j++ )
			{
				var u = (float)j / ( side - 1 );

				var place =
					( q0 * ( 1.0f - u ) * ( 1.0f - v ) ) +
					( q1 * u * ( 1.0f - v ) ) +
					( q2 * u * v ) +
					( q3 * ( 1.0f - u ) * v );

				var key = Key( place );

				if ( welded.TryGetValue( key, out var found ) )
				{
					grid[( i * side ) + j] = found;
					continue;
				}

				if ( displacement is not null )
				{
					var offset = at + ( down ? ( j * side ) + i : ( i * side ) + j );

					if ( offset >= 0 && offset < displacement.Length ) place += displacement[offset];
				}

				positions.Add( place );
				welded[key] = positions.Count - 1;
				grid[( i * side ) + j] = positions.Count - 1;
			}
		}

		for ( var i = 0; i < side - 1; i++ )
		{
			for ( var j = 0; j < side - 1; j++ )
			{
				into.Add( new[]
				{
					grid[( i * side ) + j],
					grid[( i * side ) + j + 1],
					grid[( ( i + 1 ) * side ) + j + 1],
					grid[( ( i + 1 ) * side ) + j],
				} );
			}
		}
	}

	//Where a sample is before it is moved, as something two patches work out identically. Tenths of a millimetre,
	//which is finer than any map is drawn to and coarse enough that the same arithmetic in two orders still meets.
	private static (long, long, long) Key( Vector3 place )
		=> ( (long)MathF.Round( place.x * 10000.0f ),
			(long)MathF.Round( place.y * 10000.0f ),
			(long)MathF.Round( place.z * 10000.0f ) );

	//The point half way along an edge, made once however many faces meet on it. Two faces given two points would be two faces with a crack between them.
	private static int Midpoint( List<Vector3> positions, Dictionary<(int, int), int> midpoints, int a, int b )
	{
		var key = a < b ? (a, b) : (b, a);

		if ( midpoints.TryGetValue( key, out var found ) ) return found;

		positions.Add( ( positions[a] + positions[b] ) / 2.0f );
		midpoints[key] = positions.Count - 1;

		return positions.Count - 1;
	}
}
