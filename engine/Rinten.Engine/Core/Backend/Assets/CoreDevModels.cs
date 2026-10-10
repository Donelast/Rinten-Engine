using Rinten.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;

namespace Rinten;

//Built-in box, sphere, plane and error models, built in memory in round metre sizes
internal static unsafe class CoreDevModels
{
	private static readonly Logger log = new( "Core" );

	//A metre across, centred on the origin.
	private const float BoxSize = 1.0f;

	//Half a metre, so the sphere is a metre across like the box.
	private const float SphereRadius = 0.5f;

	//A metre square, lying flat.
	private const float PlaneSize = 1.0f;

	//The four, built once and kept. Keyed by the resource path old content still names them
	//by, so a scene asking for models/dev/error.mdl gets one even though no such file exists.
	private static readonly Dictionary<string, CoreModel> made = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Whether a path is one of ours to make rather than one to read off a disk.</summary>
	public static bool IsBuiltIn( string resourcePath )
	{
		if ( string.IsNullOrWhiteSpace( resourcePath ) ) return false;

		var key = Key( resourcePath );
		return key is not null && Shapes.ContainsKey( key ) && !OnDisk( key, resourcePath );
	}

	//Error marker is the logo rotor (models/dev/error.mdl)
	//Once compiled it loads from disk; the built-in cube stands in until then
	private static bool OnDisk( string key, string resourcePath )
	{
		if ( !string.Equals( key, "error", StringComparison.OrdinalIgnoreCase ) ) return false;

		//Once found it stays found: asked for every model that is missing, and a file does not leave mid-run.
		if ( errorOnDisk ) return true;

		errorOnDisk = CoreModel.ResolveOnDisk( CompiledPaths.Model( resourcePath ).Replace( '\\', '/' ) ) is not null;
		return errorOnDisk;
	}

	private static bool errorOnDisk;

	/// <summary>One of the four, built the first time it is asked for. Null if the core is not there or this is not one of them.</summary>
	public static CoreModel Get( string resourcePath )
	{
		if ( !CoreNative.Available || string.IsNullOrWhiteSpace( resourcePath ) ) return null;

		var key = Key( resourcePath );
		if ( key is null || !Shapes.ContainsKey( key ) || OnDisk( key, resourcePath ) ) return null;

		if ( made.TryGetValue( key, out var already ) && already is { IsValid: true } ) return already;

		try
		{
			var model = Build( key, Shapes[key]() );

			if ( model is null )
			{
				log.Warning( $"could not build the built-in model {key}" );
				return null;
			}

			//One object, however many models name it: a holder that disposed it would take the shape away from every other holder - see CoreModel.Shared.
			model.Shared = true;

			made[key] = model;
			return model;
		}
		catch ( Exception e )
		{
			log.Warning( $"could not build the built-in model {key}: {e.Message}" );
			return null;
		}
	}

	//What each of them is, made on demand.
	private static readonly Dictionary<string, Func<(Vertex[], uint[])>> Shapes = new( StringComparer.OrdinalIgnoreCase )
	{
		["box"] = () => BoxMesh( BoxSize ),
		["error"] = () => BoxMesh( BoxSize ),
		["sphere"] = () => SphereMesh( SphereRadius ),
		["plane"] = () => PlaneMesh( PlaneSize ),
	};

	//Which of them a path names, or null for anything else. The directory has to match as well as the name. A project is perfectly
	//entitled to a model of its own called box, and handing it a primitive instead would be a bug nobody would think to look for.
	private static string Key( string resourcePath )
	{
		var path = resourcePath.Replace( '\\', '/' ).TrimStart( '/' );
		var name = Path.GetFileNameWithoutExtension( path );

		return path.StartsWith( "models/dev/", StringComparison.OrdinalIgnoreCase ) ? name : null;
	}

	//Builds one and hands it straight to the core.
	private static CoreModel Build( string name, (Vertex[] Vertices, uint[] Indices) mesh )
	{
		var builder = new CoreAssets.ModelBuilder();

		try
		{
			builder.SetName( name );

			var vertices = mesh.Vertices;
			var indices = mesh.Indices;

			var bounds = BBox.FromPoints( Positions( vertices ), 0 );
			var minMax = stackalloc float[6]
			{
				bounds.Mins.x, bounds.Mins.y, bounds.Mins.z,
				bounds.Maxs.x, bounds.Maxs.y, bounds.Maxs.z,
			};

			builder.SetRenderBounds( minMax );
			builder.SetPhysicsBounds( minMax );

			fixed ( Vertex* v = vertices )
				builder.AddVertices( v, vertices.Length );

			fixed ( uint* i = indices )
				builder.AddIndices( i, indices.Length );

			builder.AddMesh( name, Material( name ), 0, indices.Length, 0, vertices.Length );

			//A sheet of their own, as every compiled model is given one: these are what a level is blocked out of, and without it nothing built
			//of them had a place in a bake - no bounce, no baked shadow - and read the volume's metres-wide answer instead. See BakedLighting.
			builder.GenerateLightmapUv( 0 );

			return CoreModel.FromBuilder( builder, $"models/dev/{name}.mdl" );
		}
		finally
		{
			builder.Dispose();
		}
	}

	//Material for a built-in model: the default plain material, or the error material for the error model
	private static string Material( string name )
		=> string.Equals( name, "error", StringComparison.OrdinalIgnoreCase )
			? ErrorMaterial
			: ModelDefinition.PlainMaterial;

	//The unlit magenta the error marker is drawn with.
	private const string ErrorMaterial = "materials/error.mat";

	private static IEnumerable<Vector3> Positions( Vertex[] vertices )
	{
		foreach ( var v in vertices )
			yield return v.Position;
	}

	//A cube centred on the origin. Split per face rather than shared at the corners, so each face gets its own normal and lights as a flat surface.
	internal static (Vertex[], uint[]) BoxMesh( float size )
	{
		var h = size * 0.5f;

		Vector3[] normals =
		{
			Vector3.Forward, Vector3.Backward,
			Vector3.Left, Vector3.Right,
			Vector3.Up, Vector3.Down,
		};

		var vertices = new List<Vertex>( 24 );
		var indices = new List<uint>( 36 );

		foreach ( var normal in normals )
		{
			//Tangent axes must be right-handed so faces wind counter-clockwise from outside
			var tangent = Math.Abs( normal.y ) > 0.5f ? Vector3.Forward : Vector3.Up;
			var right = Vector3.Cross( normal, tangent ).Normal;
			var up = Vector3.Cross( normal, right ).Normal;

			var first = (uint)vertices.Count;
			var centre = normal * h;

			vertices.Add( new Vertex( centre - right * h - up * h, normal, right, new Vector4( 0, 1, 0, 0 ) ) );
			vertices.Add( new Vertex( centre + right * h - up * h, normal, right, new Vector4( 1, 1, 0, 0 ) ) );
			vertices.Add( new Vertex( centre + right * h + up * h, normal, right, new Vector4( 1, 0, 0, 0 ) ) );
			vertices.Add( new Vertex( centre - right * h + up * h, normal, right, new Vector4( 0, 0, 0, 0 ) ) );

			indices.AddRange( new[] { first, first + 1, first + 2, first, first + 2, first + 3 } );
		}

		return (vertices.ToArray(), indices.ToArray());
	}

	//A UV sphere. 24 around, 16 top to bottom - smooth enough at any size a dev model gets used at.
	private static (Vertex[], uint[]) SphereMesh( float radius )
	{
		const int segments = 24;
		const int rings = 16;

		var vertices = new List<Vertex>( (segments + 1) * (rings + 1) );
		var indices = new List<uint>( segments * rings * 6 );

		for ( var ring = 0; ring <= rings; ++ring )
		{
			var v = ring / (float)rings;
			var phi = v * MathF.PI;

			for ( var segment = 0; segment <= segments; ++segment )
			{
				var u = segment / (float)segments;
				var theta = u * MathF.PI * 2f;

				//The poles are up, so the rings climb Y and turn on X and Z. Theta runs the way it does
				//- into -Z rather than +Z - because that is what leaves each quad below wound outward.
				var normal = new Vector3(
					MathF.Sin( phi ) * MathF.Cos( theta ),
					MathF.Cos( phi ),
					-MathF.Sin( phi ) * MathF.Sin( theta ) );

				var tangent = new Vector3( -MathF.Sin( theta ), 0, -MathF.Cos( theta ) );

				vertices.Add( new Vertex( normal * radius, normal, tangent, new Vector4( u, v, 0, 0 ) ) );
			}
		}

		var stride = (uint)(segments + 1);

		for ( uint ring = 0; ring < rings; ++ring )
		{
			for ( uint segment = 0; segment < segments; ++segment )
			{
				var a = ring * stride + segment;
				var b = a + stride;

				indices.AddRange( new[] { a, b, a + 1, a + 1, b, b + 1 } );
			}
		}

		return (vertices.ToArray(), indices.ToArray());
	}

	//A flat square on the ground plane, facing up.
	private static (Vertex[], uint[]) PlaneMesh( float size )
	{
		var h = size * 0.5f;
		var normal = Vector3.Up;
		var tangent = Vector3.Right;

		//The ground plane is X and Z with nothing on Y, and u runs along the tangent, which is why the corners go round the way they do.
		var vertices = new[]
		{
			new Vertex( new Vector3( -h, 0,  h ), normal, tangent, new Vector4( 0, 1, 0, 0 ) ),
			new Vertex( new Vector3(  h, 0,  h ), normal, tangent, new Vector4( 1, 1, 0, 0 ) ),
			new Vertex( new Vector3(  h, 0, -h ), normal, tangent, new Vector4( 1, 0, 0, 0 ) ),
			new Vertex( new Vector3( -h, 0, -h ), normal, tangent, new Vector4( 0, 0, 0, 0 ) ),
		};

		return (vertices, new uint[] { 0, 1, 2, 0, 2, 3 });
	}
}
