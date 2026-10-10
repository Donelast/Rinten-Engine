using System;
using System.Linq;
using Rinten.CoreAnimation;

namespace Rinten.Porting.SBox;

//Scratch. One bone, one vertex, a known turn, and both read back: the only way to tell a yaw from a tumble without opening the game, and the question reading the code twice did not answer.
/// <summary>What a whole-model turn actually does to a skinned bone.</summary>
public static class Probe
{
	/// <summary>A model definition compiled and its skeleton read back, so which way the finished thing faces is measured rather than argued about.</summary>
	public static void Model( string definitionPath )
	{
		var tools = System.Reflection.Assembly.Load( "Rinten.Tools" );
		var compiler = tools.GetType( "Rinten.Tools.ModelDefinitionCompiler" )
			?? tools.GetTypes().First( t => t.Name == "ModelDefinitionCompiler" );

		var compile = compiler.GetMethod( "CompileToFile",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
			null, new[] { typeof( string ) }, null );

		if ( compile is null )
		{
			Console.WriteLine( "no CompileToFile(string) on the model compiler" );
			return;
		}

		var built = compile.Invoke( null, new object[] { definitionPath } );
		Console.WriteLine( $"compiled: {built}" );

		var file = System.IO.Path.ChangeExtension( definitionPath, ".mdl_c" );

		var model = CoreAssets.ModelFile.Load( file );
		if ( model is not { Valid: true } ) { Console.WriteLine( "could not load " + file ); return; }

		var skeleton = CoreAnimRuntime.CreateSkeleton( model );
		if ( skeleton is null ) { Console.WriteLine( "no skeleton" ); return; }

		//Composed with the runtime's own transform maths rather than by hand: a bone's rest is an AnimTrs and Then is how the runtime puts one under another.
		var stands = new AnimTrs[skeleton.Bones.Length];

		for ( var i = 0; i < skeleton.Bones.Length; i++ )
		{
			var parent = skeleton.Bones[i].Parent;
			var rest = skeleton.Bones[i].Rest;

			stands[i] = parent >= 0 && parent < i ? AnimTrs.Then( rest, stands[parent] ) : rest;
		}

		var place = new Vector3[skeleton.Bones.Length];

		for ( var i = 0; i < place.Length; i++ ) place[i] = stands[i].Position;

		int Named( string name )
		{
			for ( var i = 0; i < skeleton.Bones.Length; i++ )
				if ( string.Equals( skeleton.Bones[i].Name, name, StringComparison.OrdinalIgnoreCase ) ) return i;

			return -1;
		}

		void Say( string what, string from, string to )
		{
			var a = Named( from );
			var b = Named( to );

			if ( a < 0 || b < 0 ) { Console.WriteLine( $"   {what}: no {from}/{to}" ); return; }

			Console.WriteLine( $"   {what}: {( place[b] - place[a] ).Normal}" );
		}

		Console.WriteLine( $"the compiled skeleton, {skeleton.Bones.Length} bone(s):" );

		Say( "ankle -> ball ", "ankle_L", "ball_L" );
		Say( "pelvis -> head", "pelvis", "head" );
	}

	/// <summary>Scratch: prints each bone's rest pose and scale of a built model to check ragdoll joint maths</summary>
	public static void Skeleton( string compiledModel )
	{
		var model = CoreAssets.ModelFile.Load( compiledModel );
		if ( model is not { Valid: true } ) { Console.WriteLine( "could not load " + compiledModel ); return; }

		var skeleton = CoreAnimRuntime.CreateSkeleton( model );
		if ( skeleton is null ) { Console.WriteLine( "no skeleton" ); return; }

		var stands = new AnimTrs[skeleton.Bones.Length];

		for ( var i = 0; i < skeleton.Bones.Length; i++ )
		{
			var parent = skeleton.Bones[i].Parent;
			var rest = skeleton.Bones[i].Rest;

			stands[i] = parent >= 0 && parent < i ? AnimTrs.Then( rest, stands[parent] ) : rest;
		}

		for ( var i = 0; i < skeleton.Bones.Length; i++ )
		{
			var b = skeleton.Bones[i];
			var m = stands[i];
			var r = b.Rest;

			Console.WriteLine( string.Join( "\t",
				i, b.Name, b.Parent,
				$"{r.Position.x:R}", $"{r.Position.y:R}", $"{r.Position.z:R}",
				$"{r.Rotation.X:R}", $"{r.Rotation.Y:R}", $"{r.Rotation.Z:R}", $"{r.Rotation.W:R}",
				$"{r.Scale.x:R}", $"{r.Scale.y:R}", $"{r.Scale.z:R}",
				$"{m.Position.x:R}", $"{m.Position.y:R}", $"{m.Position.z:R}",
				$"{m.Rotation.X:R}", $"{m.Rotation.Y:R}", $"{m.Rotation.Z:R}", $"{m.Rotation.W:R}",
				$"{m.Scale.x:R}", $"{m.Scale.y:R}", $"{m.Scale.z:R}" ) );
		}
	}

	/// <summary>Scratch: prints a built model's ragdoll bodies and joints, flagging bodies no joint holds</summary>
	public static void Ragdoll( string definitionPath )
	{
		var tools = System.Reflection.Assembly.Load( "Rinten.Tools" );
		var compiler = tools.GetType( "Rinten.Tools.ModelDefinitionCompiler" )
			?? tools.GetTypes().First( t => t.Name == "ModelDefinitionCompiler" );

		var compile = compiler.GetMethod( "CompileToFile",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
			null, new[] { typeof( string ) }, null );

		compile.Invoke( null, new object[] { definitionPath } );

		var file = System.IO.Path.ChangeExtension( definitionPath, ".mdl_c" );
		var model = CoreAssets.ModelFile.Load( file );

		if ( model is not { Valid: true } ) { Console.WriteLine( "could not load " + file ); return; }

		var group = CoreModelPhysics.Describe( model, definitionPath );

		if ( group is null ) { Console.WriteLine( "no physics" ); return; }

		Console.WriteLine( $"{group.Parts.Count} body/bodies, {group.Joints.Count} joint(s)" );

		var held = new System.Collections.Generic.HashSet<int>();

		foreach ( var joint in group.Joints )
		{
			held.Add( joint.Body1 );
			held.Add( joint.Body2 );

			Console.WriteLine( $"  joint {joint.Type} {group.Parts[joint.Body1].BoneName} -> " +
				$"{group.Parts[joint.Body2].BoneName}" );
		}

		for ( var i = 0; i < group.Parts.Count; i++ )
		{
			var part = group.Parts[i];
			var shapes = part.Spheres.Count + part.Capsules.Count + part.Hulls.Count + part.Meshes.Count;

			Console.WriteLine( $"  body {i} {part.BoneName}: {shapes} shape(s)" +
				( held.Contains( i ) ? "" : "   <-- HELD BY NOTHING" ) );
		}

		//What is actually attached to what. A body every joint reaches is not the same as a ragdoll in one piece: an arm jointed
		//elbow-to-wrist and to nothing above it looks held from every body's own point of view and still falls off the character.
		var group_ = new int[group.Parts.Count];

		for ( var i = 0; i < group_.Length; i++ ) group_[i] = i;

		int Root( int i ) { while ( group_[i] != i ) i = group_[i] = group_[group_[i]]; return i; }

		foreach ( var joint in group.Joints ) group_[Root( joint.Body1 )] = Root( joint.Body2 );

		var islands = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.List<string>>();

		for ( var i = 0; i < group_.Length; i++ )
		{
			var at = Root( i );
			if ( !islands.TryGetValue( at, out var members ) ) islands[at] = members = new();
			members.Add( group.Parts[i].BoneName );
		}

		Console.WriteLine( $"{islands.Count} piece(s):" );

		foreach ( var island in islands.Values )
			Console.WriteLine( "  " + string.Join( ", ", island ) );
	}

	//Scratch. For a model assembled out of several exports, which is where a piece goes missing or ends up somewhere else entirely - a sight bolted to a receiver being the case this was written for.
	/// <summary>Every mesh of a built model: how many vertices, where they are, and which bones they are weighted to.</summary>
	public static unsafe void Meshes( string definitionPath )
	{
		var tools = System.Reflection.Assembly.Load( "Rinten.Tools" );
		var compiler = tools.GetType( "Rinten.Tools.ModelDefinitionCompiler" )
			?? tools.GetTypes().First( t => t.Name == "ModelDefinitionCompiler" );

		compiler.GetMethod( "CompileToFile",
			System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static,
			null, new[] { typeof( string ) }, null )
			.Invoke( null, new object[] { definitionPath } );

		var file = System.IO.Path.ChangeExtension( definitionPath, ".mdl_c" );
		var model = CoreAssets.ModelFile.Load( file );

		if ( model is not { Valid: true } ) { Console.WriteLine( "could not load " + file ); return; }

		var skeleton = CoreAnimRuntime.CreateSkeleton( model );
		var names = skeleton is null ? Array.Empty<string>() : skeleton.Bones.Select( b => b.Name ).ToArray();

		Console.WriteLine( $"{model.MeshCount} mesh(es), {model.VertexCount} vertex/vertices, {names.Length} bone(s), " +
			$"skin covers {model.Skin.Length / sizeof( ModelFormat.SkinVertex )}" );

		var vertices = model.Vertices;
		var skin = model.Skin;
		var perVertex = sizeof( ModelFormat.SkinVertex );

		var range = stackalloc int[4];

		for ( var m = 0; m < model.MeshCount; m++ )
		{
			model.MeshRange( m, range );

			var first = range[2];
			var count = range[3];

			var min = new Vector3( float.MaxValue );
			var max = new Vector3( float.MinValue );
			var used = new System.Collections.Generic.SortedSet<int>();

			for ( var v = first; v < first + count && v < vertices.Length; v++ )
			{
				var at = vertices[v].Position;

				min = Vector3.Min( min, at );
				max = Vector3.Max( max, at );

				var offset = v * perVertex;

				if ( offset + perVertex <= skin.Length )
				{
					for ( var w = 0; w < 4; w++ )
					{
						if ( skin[offset + 4 + w] == 0 ) continue;
						used.Add( skin[offset + w] );
					}
				}
			}

			var boned = string.Join( ", ", used.Select( i => i < names.Length ? names[i] : $"#{i}" ) );

			var sample = "";

			for ( var v = first; v < first + count && v < first + 3; v++ )
			{
				var offset = v * perVertex;
				if ( offset + perVertex > skin.Length ) { sample += " [past the end]"; continue; }

				sample += $" [{skin[offset]},{skin[offset + 1]},{skin[offset + 2]},{skin[offset + 3]}" +
					$" w {skin[offset + 4]},{skin[offset + 5]},{skin[offset + 6]},{skin[offset + 7]}]";
			}

			Console.WriteLine( $"  mesh {m} \"{model.MeshName( m )}\": {count} vert from {first}, " +
				$"min {min}, max {max}, bones [{boned}]" );
			Console.WriteLine( $"      first three:{sample}" );
		}
	}

	public static unsafe void Turn( float pitch, float yaw, float roll )
	{
		var builder = new CoreAssets.ModelBuilder();

		try
		{
			//A bone standing a metre up, facing along the engine's forward.
			var bind = stackalloc float[16]
			{
				1, 0, 0, 0,
				0, 1, 0, 0,
				0, 0, 1, 0,
				0, 1, 0, 1,
			};

			//The inverse of that, which is what a model stores.
			var inverse = stackalloc float[16]
			{
				1, 0, 0, 0,
				0, 1, 0, 0,
				0, 0, 1, 0,
				0, -1, 0, 1,
			};

			var bone = builder.AddBone( "root", -1, inverse );

			var turn = Rotation.From( pitch, yaw, roll );

			Console.WriteLine( $"turn  pitch {pitch} yaw {yaw} roll {roll}  ->  quat " +
				$"({turn.x:0.000},{turn.y:0.000},{turn.z:0.000},{turn.w:0.000})" );

			Console.WriteLine( $"      that quaternion turns +Z to {Vector3.Forward * turn}, " +
				$"and +Y to {Vector3.Up * turn}" );

			var pos = stackalloc float[3];
			var quat = stackalloc float[4] { turn.x, turn.y, turn.z, turn.w };
			var scale = stackalloc float[3] { 1, 1, 1 };

			builder.ApplyTransform( pos, quat, scale );

			var after = builder.BoneInverseBind( bone );

			//The bind is the inverse of what is stored, and its fourth row is where the bone
			//stands - see AnimTrs.FromMatrix, which reads a translation out of row four.
			Console.WriteLine( "      the bone's stored inverse bind, row by row:" );

			for ( var row = 0; row < 4; row++ )
			{
				Console.WriteLine( $"        {after[( row * 4 ) + 0],7:0.000} {after[( row * 4 ) + 1],7:0.000} " +
					$"{after[( row * 4 ) + 2],7:0.000} {after[( row * 4 ) + 3],7:0.000}" );
			}

			Console.WriteLine( "      so the bone now stands at " +
				$"({-after[12]:0.000}, {-after[13]:0.000}, {-after[14]:0.000}) if the turn were only an offset" );
		}
		finally
		{
			builder.Dispose();
		}
	}
}
