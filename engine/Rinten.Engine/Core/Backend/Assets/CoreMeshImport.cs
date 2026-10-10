using System.IO;

namespace Rinten;

//Source art into our own model format. One place, because two things want it: the porter, bringing a whole project across, and the editor's
//own compiler, rebuilding one model because somebody saved its mesh. What they do with the result differs; how the result is made does not.
internal static unsafe class CoreMeshImport
{
	//What our compiled models start with: 'ESSM', little end first.
	private static readonly byte[] Magic = { 0x45, 0x53, 0x53, 0x4D };

	/// <summary>The formats our own importers read.</summary>
	public static bool CanRead( string source )
	{
		var extension = Path.GetExtension( source )?.ToLowerInvariant();

		return extension is ".fbx" or ".gltf" or ".glb" or ".obj" or ".smd";
	}

	/// <summary>Where a part of a model actually goes, once the bone it hangs off is known.</summary>
	public static (Vector3 Position, Angles Rotation) Placement( CoreAssets.ModelBuilder builder, string parent,
		Vector3 position, Angles rotation )
	{
		if ( string.IsNullOrWhiteSpace( parent ) ) return (position, rotation);
		if ( !CoreModelBones.TryRest( builder, parent, out var rest ) ) return (position, rotation);

		var placed = rest.ToWorld( new Transform( position, rotation.ToRotation() ) );

		return (placed.Position, placed.Rotation.Angles());
	}

	/// <summary>Imports art of any supported kind into a builder, returning mesh count; negative on read failure</summary>
	public static int Import( CoreAssets.ModelBuilder builder, string source, float scale )
		=> Import( builder, source, scale, default, default );

	/// <summary>The same, with the part put somewhere other than where it was exported.</summary>
	public static int Import( CoreAssets.ModelBuilder builder, string source, float scale, Vector3 position, Angles rotation )
		=> Import( builder, source, scale, position, rotation, null );

	/// <summary>The same, told which bone the part hangs off.</summary>
	public static int Import( CoreAssets.ModelBuilder builder, string source, float scale, Vector3 position,
		Angles rotation, string parent )
	{
		if ( builder is null || string.IsNullOrWhiteSpace( source ) ) return -1;

		var placed = !position.IsNearlyZero( 0.0001f ) || !rotation.AsVector3().IsNearlyZero( 0.0001f );

		if ( placed ) SetPlacement( builder, position, rotation );

		//Everything before this part is weighted already or belongs to bone nothing; only what this part adds should follow its bone.
		builder.FillSkin();

		//No bone named is no bone: what the part does not weight itself stays still - see ModelBuilder.BindStillVertices.
		var rigid = string.IsNullOrWhiteSpace( parent ) ? CoreAssets.ModelBuilder.NoBone : builder.FindBone( parent );

		builder.SetRigidBone( rigid );

		try
		{
			return Read( builder, source, scale );
		}
		finally
		{
			//Whatever this part left unweighted is its own, and is written before the bone goes back - or the part after it would be given this one's bone.
			builder.FillSkin();
			builder.SetRigidBone( CoreAssets.ModelBuilder.NoBone );

			//Cleared whatever happened, or the next part of the model would be put wherever this one was.
			if ( placed ) builder.SetPlacement( null, null );
		}
	}

	//Where the next thing added to a builder goes, for readers that are not this one - an smd, say, whose importer knows nothing about a model being
	//assembled out of pieces. Put back with Place( builder, Vector3.Zero, default ) when the piece is in, or everything after it is put in the same place.
	internal static unsafe void Place( CoreAssets.ModelBuilder builder, Vector3 position, Angles rotation )
		=> SetPlacement( builder, position, rotation );

	private static void SetPlacement( CoreAssets.ModelBuilder builder, Vector3 position, Angles rotation )
	{
		var turn = rotation.ToRotation();

		var quaternion = stackalloc float[4] { turn.x, turn.y, turn.z, turn.w };
		var offset = stackalloc float[3] { position.x, position.y, position.z };

		builder.SetPlacement( quaternion, offset );
	}

	private static int Read( CoreAssets.ModelBuilder builder, string source, float scale )
	{
		return Path.GetExtension( source )?.ToLowerInvariant() switch
		{
			".fbx" => CoreFbxImport.ImportModel( builder, source, scale ),

			//Managed: parsing earns a native library nothing at import time, and a malformed file is an exception rather than a crash.
			".obj" => CoreObjImport.Import( builder, source, scale ),
			".gltf" or ".glb" => CoreGltfImport.ImportModel( builder, source, scale ),
			".smd" => CoreSmdImport.Import( builder, source, scale ),
			_ => -1,
		};
	}

	/// <summary>One mesh of a hull file: what it is called, and the corners it turns on.</summary>
	public readonly record struct Piece( string Name, Vector3[] Points );

	//Split by mesh because a convex hull cannot be concave, so a shape an artist modelled in several pieces is several hulls or it is not the shape.
	/// <summary>Each mesh of a file as its own set of corners, in model space.</summary>
	public static List<Piece> Pieces( string source, float scale = 1.0f )
	{
		var pieces = new List<Piece>();

		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( source ) ) return pieces;
		if ( !File.Exists( source ) || !CanRead( source ) ) return pieces;

		var builder = new CoreAssets.ModelBuilder();

		CoreAssets.ModelFile model = null;

		try
		{
			if ( Import( builder, source, scale ) <= 0 ) return pieces;

			//Read out of a built model because a builder does not hand its vertices back; both are in memory, so nothing beyond the file is touched.
			model = builder.Build();
			if ( model is null ) return pieces;

			var vertices = VerticesOf( model );
			if ( vertices is null ) return pieces;

			var indices = IndicesOf( model );
			var meshes = model.MeshCount;

			//Nothing to tell apart: the file is one shape.
			if ( meshes <= 0 || indices is null )
			{
				pieces.Add( new Piece( Path.GetFileNameWithoutExtension( source ), Corners( vertices ) ) );
				return pieces;
			}

			var range = stackalloc int[4];

			for ( var mesh = 0; mesh < meshes; mesh++ )
			{
				model.MeshRange( mesh, range );

				//Gathered through the indices, not the vertex range beside them: an importer may hand every mesh the whole vertex buffer, and OBJ does.
				var first = range[0];
				var length = range[1];
				if ( length <= 0 ) continue;

				var used = new List<Vector3>( length );

				for ( var i = first; i < first + length && i >= 0 && i < indices.Length; i++ )
				{
					var at = indices[i];
					if ( at < vertices.Length ) used.Add( vertices[at] );
				}

				if ( used.Count == 0 ) continue;

				var name = model.MeshName( mesh );
				pieces.Add( new Piece( name, Corners( used ) ) );
			}

			return pieces;
		}
		finally
		{
			if ( model is not null ) model.Dispose();

			builder.Dispose();
		}
	}

	//Every vertex position of a built model, or null.
	private static Vector3[] VerticesOf( CoreAssets.ModelFile model )
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

	//Every index of a built model, or null.
	private static uint[] IndicesOf( CoreAssets.ModelFile model )
	{
		var count = model.IndexCount;
		if ( count <= 0 ) return null;

		var indices = new uint[count];

		fixed ( uint* i = indices )
		{
			if ( model.CopyIndices( i, count ) <= 0 ) return null;
		}

		return indices;
	}

	//The points of a mesh with the repeats taken out. A file splits a corner into one vertex per face meeting there, so a modelled box arrives
	//as twenty-four points rather than eight - enough to look like a shape too complicated to hand the solver when it is nothing of the sort.
	private static Vector3[] Corners( IReadOnlyList<Vector3> points )
	{
		//A tenth of a millimetre: under anything that changes a collision shape, over what an exporter's rounding moves a corner by.
		const float grid = 10000.0f;

		var seen = new HashSet<(int, int, int)>();
		var kept = new List<Vector3>( points.Count );

		foreach ( var point in points )
		{
			var key = ((int)MathF.Round( point.x * grid ),
				(int)MathF.Round( point.y * grid ),
				(int)MathF.Round( point.z * grid ));

			if ( !seen.Add( key ) ) continue;

			kept.Add( point );
		}

		return kept.ToArray();
	}

	/// <summary>Imported mesh names in a source file, for the outliner include list.</summary>
	public static List<string> Catalog( string source, float scale = 1.0f )
	{
		var names = new List<string>();
		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( source ) ) return names;
		if ( !File.Exists( source ) || !CanRead( source ) ) return names;

		var builder = new CoreAssets.ModelBuilder();

		try
		{
			if ( Import( builder, source, scale ) <= 0 ) return names;

			var count = builder.MeshCount;
			for ( var i = 0; i < count; i++ )
			{
				var name = builder.MeshName( i );
				if ( string.IsNullOrWhiteSpace( name ) ) continue;

				var seen = false;
				for ( var n = 0; n < names.Count; n++ )
				{
					if ( !string.Equals( names[n], name, StringComparison.OrdinalIgnoreCase ) ) continue;
					seen = true;
					break;
				}

				if ( !seen ) names.Add( name );
			}
		}
		finally
		{
			builder.Dispose();
		}

		return names;
	}

	/// <summary>Bone names in a source file, for the hull picker before compile.</summary>
	public static List<string> Bones( string source, float scale = 1.0f )
	{
		var names = new List<string>();
		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( source ) ) return names;
		if ( !File.Exists( source ) || !CanRead( source ) ) return names;

		var builder = new CoreAssets.ModelBuilder();

		CoreAssets.ModelFile model = null;

		try
		{
			if ( Import( builder, source, scale ) <= 0 ) return names;

			model = builder.Build();
			if ( model is null ) return names;

			var count = model.BoneCount;
			for ( var i = 0; i < count; i++ )
			{
				var name = model.BoneName( i );
				if ( string.IsNullOrWhiteSpace( name ) ) continue;

				var seen = false;
				for ( var n = 0; n < names.Count; n++ )
				{
					if ( !string.Equals( names[n], name, StringComparison.OrdinalIgnoreCase ) ) continue;
					seen = true;
					break;
				}

				if ( !seen ) names.Add( name );
			}
		}
		finally
		{
			if ( model is not null ) model.Dispose();
			builder.Dispose();
		}

		return names;
	}

	/// <summary>Imported surface names, as the export called the materials.</summary>
	public static List<string> Materials( string source, float scale = 1.0f )
	{
		var names = new List<string>();
		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( source ) ) return names;
		if ( !File.Exists( source ) || !CanRead( source ) ) return names;

		var builder = new CoreAssets.ModelBuilder();

		try
		{
			if ( Import( builder, source, scale ) <= 0 ) return names;

			var count = builder.MeshCount;
			for ( var i = 0; i < count; i++ )
			{
				var name = builder.MeshMaterial( i );
				name = ModelImportCatalog.Bare( name );
				if ( string.IsNullOrWhiteSpace( name ) ) continue;

				var seen = false;
				for ( var n = 0; n < names.Count; n++ )
				{
					if ( !string.Equals( names[n], name, StringComparison.OrdinalIgnoreCase ) ) continue;
					seen = true;
					break;
				}

				if ( !seen ) names.Add( name );
			}
		}
		finally
		{
			builder.Dispose();
		}

		return names;
	}

	/// <summary>The same, handed back rather than written - what the editor's compiler wants, since it writes through the asset system rather than to a path.</summary>
	public static byte[] ToBytes( string source, float scale = 1.0f )
	{
		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( source ) ) return null;
		if ( !File.Exists( source ) || !CanRead( source ) ) return null;

		var builder = new CoreAssets.ModelBuilder();

		//Through a file rather than a buffer: the core writes models to paths, and a temporary file is a smaller thing to own than a second way of saving one.
		var scratch = Path.Combine( Path.GetTempPath(), $"rinten-compile-{System.Guid.NewGuid():N}.mdl_c" );

		try
		{
			if ( Import( builder, source, scale ) <= 0 ) return null;
			if ( builder.Save( scratch ) == 0 ) return null;

			return File.ReadAllBytes( scratch );
		}
		finally
		{
			builder.Dispose();

			if ( File.Exists( scratch ) ) File.Delete( scratch );
		}
	}

	/// <summary>Whether the bytes are our model format, since the compiled resource container uses the same extension</summary>
	public static bool IsOurs( ReadOnlySpan<byte> bytes )
		=> bytes.Length >= 4 && bytes[..4].SequenceEqual( Magic );

	/// <summary>Our model out of a compiled resource container, if that is what this is.</summary>
	public static byte[] Unwrap( byte[] bytes )
	{
		if ( bytes is null || IsOurs( bytes ) ) return bytes;

		return CoreResource.Unwrap( bytes, ModelMagic );
	}

	//'ESSM', as a number, for finding it in a container.
	private const uint ModelMagic = 0x4D535345;
}
