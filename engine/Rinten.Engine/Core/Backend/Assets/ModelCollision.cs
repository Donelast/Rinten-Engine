using System;
using System.Collections.Generic;

namespace Rinten;

//Builds collision from a ModelDefinition's shapes, in bone or model space; callers apply the TRS
internal static unsafe class ModelCollision
{
	private static readonly Logger log = new( "Core" );

	//Max hull corners: Box3D's 255 half-edge limit fails silently past 44 corners
	private const int MostCorners = 44;

	//How many corners to keep. Their documents say nothing far more often than they name a number, and
	//nothing has to mean a hull rather than the whole mesh - a weapon's low-poly copy is five thousand points.
	private static int CornersFor( int asked ) => asked > 0 && asked < MostCorners ? asked : MostCorners;

	/// <summary>Adds every shape <paramref name="definition"/> names to a builder that already holds the model's geometry.</summary>
	public static int Write( CoreAssets.ModelBuilder builder, ModelDefinition definition, string sourceDirectory )
	{
		if ( builder is null || definition?.Hulls is not { Count: > 0 } ) return 0;

		var added = 0;
		var bodies = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );

		//Only a single-body model gets the definition's mass; ragdoll limbs use their shapes' own mass
		var limbs = definition.Hulls
			.Where( hull => hull is { Enabled: true } and not ModelDefinition.RenderMesh )
			.Select( hull => hull.Bone ?? "" )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.Count();

		var mass = limbs > 1 ? 0.0f : definition.Mass;

		if ( limbs > 1 && definition.Mass > 0.0f )
		{
			log.Warning( $"a ragdoll of {limbs} bodies cannot each weigh the {definition.Mass}kg the model asks for - " +
				"every limb weighs what its own shape says instead" );
		}

		var hosts = Hosts( builder, definition );

		foreach ( var hull in definition.Hulls )
		{
			if ( hull is null || !hull.Enabled ) continue;

			var surface = SurfaceName( string.IsNullOrWhiteSpace( hull.Surface ) ? definition.Surface : hull.Surface );

			if ( hull is ModelDefinition.RenderMesh )
			{
				added += FromRenderMesh( builder, definition, surface );
				continue;
			}

			//Which body this shape belongs on, and how to get there from the bone it was drawn against - see Hosts.
			var bone = hull.Bone ?? "";
			var host = hosts.TryGetValue( bone, out var found ) ? found : bone;
			var into = string.Equals( host, bone, StringComparison.OrdinalIgnoreCase )
				? (Transform?)null
				: Between( builder, bone, host );

			var body = BodyFor( builder, bodies, host, hull, mass );

			if ( body < 0 ) continue;

			added += hull switch
			{
				ModelDefinition.SphereHull sphere => AddSphere( builder, body, surface, sphere, into ),
				ModelDefinition.CapsuleHull capsule => AddCapsule( builder, body, surface, capsule, into ),
				ModelDefinition.BoxHull box => AddBox( builder, body, surface, box, into ),
				ModelDefinition.RenderHull { HasFile: true } file => FromFile( builder, definition, file, surface, sourceDirectory, body ),
				ModelDefinition.RenderHull render => FromRender( builder, definition, render, surface, body ),
				_ => 0,
			};
		}

		return added;
	}

	//A bone's rest transform, where its bone-space shapes' body goes
	private static Transform RestOf( CoreAssets.ModelBuilder builder, ModelDefinition.Hull hull )
	{
		var bone = hull?.Bone ?? "";

		if ( bone.Length == 0 ) return new Transform( Vector3.Zero, Rotation.Identity );

		return CoreModelBones.TryRest( builder, bone, out var rest )
			? rest
			: new Transform( Vector3.Zero, Rotation.Identity );
	}

	//Maps shapes on bones without joints to the nearest jointed ancestor's body (ragdolls only)
	private static Dictionary<string, string> Hosts( CoreAssets.ModelBuilder builder, ModelDefinition definition )
	{
		var hosts = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		if ( definition?.Joints is not { Count: > 0 } joints ) return hosts;

		var jointed = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var joint in joints )
		{
			if ( joint is null || !joint.Enabled ) continue;

			if ( !string.IsNullOrWhiteSpace( joint.BoneA ) ) jointed.Add( joint.BoneA );
			if ( !string.IsNullOrWhiteSpace( joint.BoneB ) ) jointed.Add( joint.BoneB );
		}

		if ( jointed.Count == 0 ) return hosts;

		//A joint names a body, so the bone it names is only a body if a shape hangs off it. Their citizen's neck is jointed and has no shape.
		var hulled = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var hull in definition.Hulls )
		{
			if ( hull is { Enabled: true } and not ModelDefinition.RenderMesh && !string.IsNullOrWhiteSpace( hull.Bone ) )
				hulled.Add( hull.Bone );
		}

		foreach ( var hull in definition.Hulls )
		{
			if ( hull is not { Enabled: true } or ModelDefinition.RenderMesh ) continue;

			var bone = hull.Bone ?? "";

			if ( bone.Length == 0 || jointed.Contains( bone ) ) continue;

			//Up the skeleton to the first bone that is a body. Nothing found leaves the shape where it
			//is: a shape hung off a bone above every joint in the model is the model's own, not a limb's.
			for ( var at = builder.BoneParent( builder.FindBone( bone ) ); at >= 0; at = builder.BoneParent( at ) )
			{
				var named = builder.BoneName( at );

				if ( !jointed.Contains( named ) || !hulled.Contains( named ) ) continue;

				hosts[bone] = named;
				log.Info( $"{definition.ResourcePath}: {Named( hull )} hangs off {bone}, which no joint holds - " +
					$"it is collision on {named} instead" );

				break;
			}
		}

		return hosts;
	}

	//The step from one bone's own space into another's, both at rest. Read off the builder rather than through CoreModelBones: that only
	//knows the bones something added through it, which is the extra ones a definition declares and not the skeleton an fbx brought in.
	private static Transform Between( CoreAssets.ModelBuilder builder, string bone, string host )
	{
		var from = RestOfBone( builder, builder.FindBone( bone ) );
		var to = RestOfBone( builder, builder.FindBone( host ) );

		return to.ToLocal( from );
	}

	//Where a bone of the builder rests, in the model's own space.
	private static Transform RestOfBone( CoreAssets.ModelBuilder builder, int bone )
	{
		if ( bone < 0 ) return new Transform( Vector3.Zero, Rotation.Identity );

		var written = builder.BoneInverseBind( bone );
		if ( written.Length < 16 ) return new Transform( Vector3.Zero, Rotation.Identity );

		var inverse = new System.Numerics.Matrix4x4(
			written[0], written[1], written[2], written[3],
			written[4], written[5], written[6], written[7],
			written[8], written[9], written[10], written[11],
			written[12], written[13], written[14], written[15] );

		if ( !System.Numerics.Matrix4x4.Invert( inverse, out var pose ) )
			return new Transform( Vector3.Zero, Rotation.Identity );

		if ( !System.Numerics.Matrix4x4.Decompose( pose, out _, out var turn, out var offset ) )
			return new Transform( Vector3.Zero, Rotation.Identity );

		return new Transform(
			new Vector3( offset.X, offset.Y, offset.Z ),
			new Rotation( turn.X, turn.Y, turn.Z, turn.W ) );
	}

	//One body per bone so a ragdoll is one piece per named limb.
	private static int BodyFor( CoreAssets.ModelBuilder builder, Dictionary<string, int> bodies, string host,
		ModelDefinition.Hull hull, float mass )
	{
		var key = host ?? "";

		if ( bodies.TryGetValue( key, out var existing ) )
			return existing;

		var rest = RestOf( builder, hull );

		var transform = stackalloc float[7]
		{
			rest.Position.x, rest.Position.y, rest.Position.z,
			rest.Rotation.x, rest.Rotation.y, rest.Rotation.z, rest.Rotation.w,
		};

		var body = builder.AddBody( Named( hull ), key, transform, mass, 0.0f, 0.0f, 1.0f );

		if ( body >= 0 )
			bodies[key] = body;

		return body;
	}

	private static int AddSphere( CoreAssets.ModelBuilder builder, int body, string surface,
		ModelDefinition.SphereHull hull, Transform? into )
	{
		if ( hull.Radius <= 0.0f ) return 0;

		var at = Moved( into, hull.Center );

		var center = stackalloc float[3] { at.x, at.y, at.z };
		builder.AddSphere( body, surface, center, hull.Radius );
		return 1;
	}

	private static int AddCapsule( CoreAssets.ModelBuilder builder, int body, string surface,
		ModelDefinition.CapsuleHull hull, Transform? into )
	{
		if ( hull.Radius <= 0.0f ) return 0;

		var from = Moved( into, hull.PointA );
		var to = Moved( into, hull.PointB );

		var a = stackalloc float[3] { from.x, from.y, from.z };
		var b = stackalloc float[3] { to.x, to.y, to.z };
		builder.AddCapsule( body, surface, a, b, hull.Radius );
		return 1;
	}

	private static int AddBox( CoreAssets.ModelBuilder builder, int body, string surface,
		ModelDefinition.BoxHull hull, Transform? into )
	{
		var corners = hull.Corners();

		if ( into is { } move )
		{
			for ( var i = 0; i < corners.Length; i++ ) corners[i] = move.PointToWorld( corners[i] );
		}

		return AddPoints( builder, body, surface, corners );
	}

	private static Vector3 Moved( Transform? into, Vector3 point )
		=> into is { } move ? move.PointToWorld( point ) : point;

	//A hull around what the model draws.
	private static int FromRender( CoreAssets.ModelBuilder builder, ModelDefinition definition, ModelDefinition.RenderHull hull, string surface, int body )
	{
		var corners = CornersFor( hull.MaxVertices );

		if ( hull.PerElement )
		{
			var added = 0;

			var rest = RestOf( builder, hull );

			foreach ( var element in ElementsOf( builder ) )
				added += AddPoints( builder, body, surface, Into( rest, Reduce( element, corners ) ) );

			if ( added > 0 ) return added;

			log.Warning( $"{definition.ResourcePath}: {Named( hull )} has no meshes to hull one by one" );
		}

		var points = PointsOf( builder );
		if ( points is not { Length: >= 4 } )
		{
			log.Warning( $"{definition.ResourcePath}: {Named( hull )} has no geometry to hull" );
			return 0;
		}

		return AddPoints( builder, body, surface, Into( RestOf( builder, hull ), Reduce( points, corners ) ) );
	}

	//Model-space points in the space of the body they are going on. A hull worked out from what the model draws is in the model's
	//own space, and the body it goes on is now at its bone - so the points have to come back the other way or the shape moves twice.
	private static Vector3[] Into( Transform rest, Vector3[] points )
	{
		if ( points is null || rest.Position.IsNearlyZero( 0.0001f ) && rest.Rotation == Rotation.Identity )
			return points;

		var moved = new Vector3[points.Length];

		for ( var i = 0; i < points.Length; i++ )
			moved[i] = rest.PointToLocal( points[i] );

		return moved;
	}

	//The model's points, one array per mesh it is made of. A model is built once and asked about: each
	//mesh owns a run of the vertex buffer, so the runs are what tells one part of a shelf from another.
	private static List<Vector3[]> ElementsOf( CoreAssets.ModelBuilder builder )
	{
		var elements = new List<Vector3[]>();
		var model = builder.Build();

		if ( model is null ) return elements;

		try
		{
			var count = model.VertexCount;
			var meshes = model.MeshCount;

			if ( count <= 0 || meshes <= 0 ) return elements;

			var vertices = new Vertex[count];

			fixed ( Vertex* v = vertices )
			{
				if ( model.CopyVertices( v, count ) <= 0 ) return elements;
			}

			var range = stackalloc int[4];

			for ( var mesh = 0; mesh < meshes; mesh++ )
			{
				model.MeshRange( mesh, range );

				var first = range[2];
				var length = range[3];

				if ( first < 0 || length < 4 || first + length > count ) continue;

				var points = new Vector3[length];

				for ( var i = 0; i < length; i++ )
					points[i] = vertices[first + i].Position;

				elements.Add( points );
			}

			return elements;
		}
		finally
		{
			model.Dispose();
		}
	}

	//A hull around a mesh file of its own.
	private static int FromFile( CoreAssets.ModelBuilder builder, ModelDefinition definition, ModelDefinition.RenderHull hull,
		string surface, string sourceDirectory, int body )
	{
		if ( string.IsNullOrWhiteSpace( hull.File ) )
		{
			log.Warning( $"{definition.ResourcePath}: {Named( hull )} names no file" );
			return 0;
		}

		var path = AssetSources.Resolve( hull.File, sourceDirectory );
		if ( path is null )
		{
			log.Warning( $"{definition.ResourcePath}: {Named( hull )} cannot find {hull.File}" );
			return 0;
		}

		var pieces = CoreMeshImport.Pieces( path, hull.Scale > 0.0f ? hull.Scale : 1.0f );
		if ( pieces is not { Count: > 0 } )
		{
			log.Warning( $"{definition.ResourcePath}: {hull.File} has nothing to make a hull out of" );
			return 0;
		}

		var added = 0;
		var thin = new List<string>();

		foreach ( var piece in pieces )
		{
			if ( piece.Points is not { Length: >= 4 } )
			{
				thin.Add( Named( piece, hull ) );
				continue;
			}

			added += AddPiece( builder, definition, hull, surface, body, Named( piece, hull ), piece.Points );
		}

		if ( added > 0 )
		{
			if ( thin.Count > 0 )
			{
				log.Warning( $"{definition.ResourcePath}: {hull.File} has nothing solid in " +
					$"{string.Join( ", ", thin )} - a hull needs four corners not all in one plane" );
			}

			return added;
		}

		//Not one piece stands on its own, so the file is one shape rather than several - which is what it was read as before any of them were told apart.
		var whole = pieces.SelectMany( piece => piece.Points ).ToArray();

		if ( whole.Length < 4 )
		{
			log.Warning( $"{definition.ResourcePath}: {hull.File} has nothing to make a hull out of" );
			return 0;
		}

		log.Warning( $"{definition.ResourcePath}: no piece of {hull.File} has four corners of its own - " +
			"it is hulled as one shape" );

		return AddPiece( builder, definition, hull, surface, body, Named( hull ), whole );
	}

	//One piece of a hull file, kept as it was drawn where the solver can take it.
	private static int AddPiece( CoreAssets.ModelBuilder builder, ModelDefinition definition, ModelDefinition.RenderHull hull,
		string surface, int body, string name, Vector3[] points )
	{
		var corners = CornersFor( hull.MaxVertices );
		var kept = Reduce( points, corners );

		//The file is the shape, so losing any of it is worth saying out loud.
		if ( kept.Length < points.Length && hull.MaxVertices <= 0 )
		{
			log.Warning( $"{definition.ResourcePath}: {name} in {hull.File} has {points.Length} corners " +
				$"and the solver builds at most {corners} - it collides as a rougher shape than it was " +
				"drawn; split it or simplify it" );
		}

		return AddPoints( builder, body, surface, Into( RestOf( builder, hull ), kept ) );
	}

	//What a piece of a hull file is called, or something to call it by.
	private static string Named( CoreMeshImport.Piece piece, ModelDefinition.RenderHull hull )
		=> string.IsNullOrWhiteSpace( piece.Name ) ? Named( hull ) : piece.Name;

	//The model's own triangles, exactly.
	private static int FromRenderMesh( CoreAssets.ModelBuilder builder, ModelDefinition definition, string surface )
		=> builder.CollisionFromMesh( 1, surface, definition.Mass );

	private static int AddPoints( CoreAssets.ModelBuilder builder, int body, string surface, Vector3[] points )
	{
		if ( points is not { Length: >= 4 } ) return 0;

		var flat = new float[points.Length * 3];

		for ( var i = 0; i < points.Length; i++ )
		{
			flat[i * 3] = points[i].x;
			flat[i * 3 + 1] = points[i].y;
			flat[i * 3 + 2] = points[i].z;
		}

		fixed ( float* p = flat )
			builder.AddHull( body, surface, p, points.Length );

		return 1;
	}

	//Surface file name without path or extension, which Surface.FindByName expects
	private static string SurfaceName( string surface )
		=> string.IsNullOrWhiteSpace( surface ) ? "" : System.IO.Path.GetFileNameWithoutExtension( surface );

	//What a shape is called, or something to call it by.
	private static string Named( ModelDefinition.Hull hull )
		=> string.IsNullOrWhiteSpace( hull.Name ) ? hull.Title : hull.Name;

	//Every point the builder holds, by building what it has so far. The builder does not hand its vertices
	//back - it is a write-only pile until it is saved - so this saves it into memory and reads that.
	private static Vector3[] PointsOf( CoreAssets.ModelBuilder builder )
	{
		var model = builder.Build();
		if ( model is null ) return null;

		try
		{
			var count = model.VertexCount;
			if ( count <= 0 ) return null;

			var vertices = new Vertex[count];

			fixed ( Vertex* v = vertices )
			{
				if ( model.CopyVertices( v, count ) <= 0 ) return null;
			}

			var points = new Vector3[count];

			for ( var i = 0; i < count; i++ )
				points[i] = vertices[i].Position;

			return points;
		}
		finally
		{
			model.Dispose();
		}
	}

	//Reduces a hull to the extreme points along max evenly spread directions, deterministically
	internal static Vector3[] Reduce( Vector3[] points, int max )
	{
		//Fewer than four points is not a hull at all, and Box3D quietly turns a smaller cap into a tetrahedron.
		if ( max <= 0 || points is null || points.Length <= max || points.Length < 4 ) return points;
		if ( max < 4 ) max = 4;

		var kept = new HashSet<int>();

		//Golden-angle spiral: even coverage for any count, with no table.
		const float golden = 2.39996323f;

		for ( var i = 0; i < max; i++ )
		{
			var z = 1.0f - ( 2.0f * i + 1.0f ) / max;
			var radius = MathF.Sqrt( MathF.Max( 0.0f, 1.0f - z * z ) );
			var angle = golden * i;

			var direction = new Vector3( radius * MathF.Cos( angle ), radius * MathF.Sin( angle ), z );

			var best = 0;
			var bestDot = float.NegativeInfinity;

			for ( var p = 0; p < points.Length; p++ )
			{
				var dot = Vector3.Dot( points[p], direction );
				if ( dot <= bestDot ) continue;

				bestDot = dot;
				best = p;
			}

			kept.Add( best );
		}

		var reduced = new Vector3[kept.Count];
		var at = 0;

		foreach ( var index in kept )
			reduced[at++] = points[index];

		return reduced;
	}
}
