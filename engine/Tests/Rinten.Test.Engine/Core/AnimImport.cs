using System;
using System.Collections.Generic;
using System.IO;

using Rinten.CoreAnimation;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests;

//Reading animation out of the formats source art is actually in. A clip is only useful if it lands on the model it was authored
//for, so the last test here does the thing that matters: a real character's skeleton and a real clip's bones, matched by name.
[TestClass]
public class AnimImportTest
{
	private static string TempPath( string extension )
		=> Path.Combine( Path.GetTempPath(), $"rinten-anim-{Guid.NewGuid():N}{extension}" );

	//glTF with one animation: root slides 2m along Z, child turns 90 degrees about Y. Built in code
	private static string AnimatedGltf()
	{
		var quarter = MathF.Sqrt( 0.5f );

		var floats = new List<float>();

		//Times, then translations, then rotations - each starting where the buffer view below says it does.
		floats.AddRange( new[] { 0.0f, 1.0f } );
		floats.AddRange( new[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 2.0f } );
		floats.AddRange( new[] { 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, quarter, 0.0f, quarter } );

		var bytes = new byte[floats.Count * 4];
		for ( var i = 0; i < floats.Count; i++ )
			BitConverter.GetBytes( floats[i] ).CopyTo( bytes, i * 4 );

		var base64 = Convert.ToBase64String( bytes );

		return $$"""
			{
				"asset": { "version": "2.0" },
				"scene": 0,
				"scenes": [ { "nodes": [ 0 ] } ],
				"nodes": [
					{ "name": "root", "children": [ 1 ] },
					{ "name": "spine" }
				],
				"animations": [ {
					"name": "test",
					"samplers": [
						{ "input": 0, "output": 1, "interpolation": "LINEAR" },
						{ "input": 0, "output": 2, "interpolation": "LINEAR" }
					],
					"channels": [
						{ "sampler": 0, "target": { "node": 0, "path": "translation" } },
						{ "sampler": 1, "target": { "node": 1, "path": "rotation" } }
					]
				} ],
				"buffers": [ { "byteLength": {{bytes.Length}}, "uri": "data:application/octet-stream;base64,{{base64}}" } ],
				"bufferViews": [
					{ "buffer": 0, "byteOffset": 0, "byteLength": 8 },
					{ "buffer": 0, "byteOffset": 8, "byteLength": 24 },
					{ "buffer": 0, "byteOffset": 32, "byteLength": 32 }
				],
				"accessors": [
					{ "bufferView": 0, "componentType": 5126, "count": 2, "type": "SCALAR", "min": [ 0.0 ], "max": [ 1.0 ] },
					{ "bufferView": 1, "componentType": 5126, "count": 2, "type": "VEC3" },
					{ "bufferView": 2, "componentType": 5126, "count": 2, "type": "VEC4" }
				]
			}
			""";
	}

	//Imports a clip and loads what was written, so the caller can sample it.
	private static AnimClipData ImportGltf( string source, string compiled, float scale, out int frames )
	{
		var builder = new AnimClipBuilder();

		try
		{
			frames = CoreGltfImport.ImportClip( builder, source, scale, 0 );
			if ( frames <= 0 ) return null;

			Assert.IsTrue( builder.Save( compiled ) );
		}
		finally
		{
			builder.Dispose();
		}

		return AnimClipData.Load( compiled );
	}

	//A glTF animation arrives as a clip, in our axes, resampled onto frames.
	[TestMethod]
	public void ImportsAnimationFromGltf()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var source = TempPath( ".gltf" );
		var compiled = TempPath( ".anim_c" );

		File.WriteAllText( source, AnimatedGltf() );

		var clip = ImportGltf( source, compiled, 1.0f, out var frames );
		Assert.IsNotNull( clip, "the imported clip should load back" );

		try
		{
			//A second of animation at thirty frames a second, counting both ends.
			Assert.AreEqual( 31, frames );
			Assert.AreEqual( 31, clip.FrameCount );
			Assert.AreEqual( 30.0f, clip.FrameRate, 0.0001f );
			Assert.AreEqual( 1.0f, clip.Duration, 0.001f );

			Assert.AreEqual( 2, clip.Bones.Length );
			Assert.AreEqual( "root", clip.Bones[0].Name );
			Assert.AreEqual( "spine", clip.Bones[1].Name );

			Assert.AreEqual( -1, clip.Bones[0].Parent );
			Assert.AreEqual( 0, clip.Bones[1].Parent, "the spine hangs off the root" );

			var pose = new AnimTrs[2];

			clip.Sample( 0.0f, false, pose );

			Assert.AreEqual( 0.0f, pose[0].Position.Length, 0.001f, "nothing has moved yet" );
			Assert.AreEqual( 0.0f, ( (Rotation)pose[1].Rotation ).Angle(), 0.5f, "nothing has turned yet" );

			clip.Sample( 1.0f, false, pose );

			//glTF's basis is ours - Y-up, -Z forward, right handed - so two metres along its Z is two metres along our Z and nothing
			//is converted at all. Which is what the mesh importer does too, and has to be, or a clip would pull its own model apart.
			Assert.AreEqual( 0.0f, pose[0].Position.x, 0.001f );
			Assert.AreEqual( 0.0f, pose[0].Position.y, 0.001f );
			Assert.AreEqual( 2.0f, pose[0].Position.z, 0.001f );

			var turned = (Rotation)pose[1].Rotation;

			Assert.AreEqual( 90.0f, turned.Angle(), 0.5f, "a quarter turn is a quarter turn in any axes" );
			Assert.IsTrue( MathF.Abs( turned.Up.Dot( Vector3.Up ) ) > 0.99f,
				$"and it is about up, which the file and the engine agree on - got {turned.Up}" );

			//Halfway through: the keys are a second apart, so this is the resampling and not a key.
			clip.Sample( 0.5f, false, pose );

			Assert.AreEqual( 1.0f, pose[0].Position.z, 0.01f );
		}
		finally
		{
			File.Delete( compiled );
		}

		//The same file at twice the scale, for an export that disagrees about units. Positions scale; the turn does not.
		var scaled = TempPath( ".anim_c" );
		var twice = ImportGltf( source, scaled, 2.0f, out _ );

		try
		{
			var pose = new AnimTrs[2];
			twice.Sample( 1.0f, false, pose );

			Assert.AreEqual( 4.0f, pose[0].Position.z, 0.001f );

			Assert.AreEqual( 90.0f, ( (Rotation)pose[1].Rotation ).Angle(), 0.5f, "scale is a distance, not an angle" );
		}
		finally
		{
			File.Delete( scaled );
			File.Delete( source );
		}
	}

	//A file that cannot be read says so, and one with no animation in it says
	//something different - the caller needs to tell "broken" from "nothing to import".
	[TestMethod]
	public void RefusesWhatItCannotRead()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var broken = TempPath( ".gltf" );
		var empty = TempPath( ".gltf" );

		File.WriteAllText( broken, "this is not a glTF file" );
		File.WriteAllText( empty, """
			{
				"asset": { "version": "2.0" },
				"scene": 0,
				"scenes": [ { "nodes": [ 0 ] } ],
				"nodes": [ { "name": "lonely" } ]
			}
			""" );

		var builder = new AnimClipBuilder();

		try
		{
			Assert.AreEqual( -1, CoreGltfImport.ImportClip( builder, broken, 1.0f, 0 ) );
			Assert.AreEqual( -1, CoreGltfImport.ImportClip( builder, "no/such/file.gltf", 1.0f, 0 ) );
			Assert.AreEqual( -1, CoreFbxImport.ImportClip( builder, broken, 1.0f, 0 ) );

			Assert.AreEqual( 0, CoreGltfImport.ImportClip( builder, empty, 1.0f, 0 ),
				"a readable file with nothing animated in it is not an error" );

			//And nothing was written from any of that.
			var nothing = TempPath( ".anim_c" );
			Assert.IsFalse( builder.Save( nothing ),
				"a clip with no bones is the importer having found nothing" );

			File.Delete( nothing );
		}
		finally
		{
			builder.Dispose();
			File.Delete( broken );
			File.Delete( empty );
		}
	}

	//Inverse bind uses row-vector convention; a wrong one only shows once bones move
	[TestMethod]
	public unsafe void InverseBindIsWrittenForRowVectors()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var source = Path.Combine( Environment.CurrentDirectory, "addons/citizen/Assets/models/citizen/citizen.fbx" );

		if ( !File.Exists( source ) )
		{
			Assert.Inconclusive( $"the citizen is not in this checkout ({source})" );
			return;
		}

		var compiled = TempPath( ".mdl_c" );
		var builder = new CoreAssets.ModelBuilder();

		try
		{
			Assert.IsTrue( CoreFbxImport.ImportModel( builder, source, 1.0f ) > 0 );
			Assert.AreEqual( 1, builder.Save( compiled ) );
		}
		finally
		{
			builder.Dispose();
		}

		var model = CoreAssets.ModelFile.Load( compiled );
		Assert.IsNotNull( model );

		try
		{
			var pelvis = -1;
			var bones = model.BoneCount;

			for ( var bone = 0; bone < bones; bone++ )
			{
				if ( model.BoneName( bone ) != "pelvis" ) continue;

				pelvis = bone;
				break;
			}

			Assert.IsTrue( pelvis >= 0, "the citizen should have a pelvis" );

			var values = stackalloc float[16];
			model.BoneInverseBind( pelvis, values );

			//The last column is what the other convention uses for its translation, so it being empty is the convention itself.
			Assert.AreEqual( 0.0f, values[3], 0.0001f );
			Assert.AreEqual( 0.0f, values[7], 0.0001f );
			Assert.AreEqual( 0.0f, values[11], 0.0001f );
			Assert.AreEqual( 1.0f, values[15], 0.0001f );

			var inverseBind = new System.Numerics.Matrix4x4(
				values[0], values[1], values[2], values[3],
				values[4], values[5], values[6], values[7],
				values[8], values[9], values[10], values[11],
				values[12], values[13], values[14], values[15] );

			Assert.IsTrue( System.Numerics.Matrix4x4.Invert( inverseBind, out var rest ),
				"an inverse bind has to be invertible - that is what it is for" );

			//Where the pelvis rests, which for a standing person is about a metre up. Read straight
			//off the matrix: if the convention were the other way round this would be zero.
			var up = rest.Translation.Z;
			Assert.IsTrue( up is > 0.5f and < 1.5f, $"the pelvis should rest about a metre up - got {up}" );
		}
		finally
		{
			model.Dispose();
			File.Delete( compiled );
		}
	}

	//The citizen walk's bone names match the citizen model, since clips bind by name
	[TestMethod]
	public unsafe void CitizenWalkMatchesTheCitizenSkeleton()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		var content = Environment.CurrentDirectory;
		var modelSource = Path.Combine( content, "addons/citizen/Assets/models/citizen/citizen.fbx" );
		var clipSource = Path.Combine( content, "addons/citizen/Assets/models/citizen/animations/Citizen@Walk_N.fbx" );

		if ( !File.Exists( modelSource ) || !File.Exists( clipSource ) )
		{
			Assert.Inconclusive( $"the citizen is not in this checkout ({modelSource})" );
			return;
		}

		var modelPath = TempPath( ".mdl_c" );
		var clipPath = TempPath( ".anim_c" );

		CoreAssets.ModelFile model = null;
		AnimClipData clip = null;

		try
		{
			var modelBuilder = new CoreAssets.ModelBuilder();

			try
			{
				Assert.IsTrue( CoreFbxImport.ImportModel( modelBuilder, modelSource, 1.0f ) > 0,
					"the citizen should have meshes" );
				Assert.AreEqual( 1, modelBuilder.Save( modelPath ) );
			}
			finally
			{
				modelBuilder.Dispose();
			}

			var clipBuilder = new AnimClipBuilder();

			try
			{
				var frames = CoreFbxImport.ImportClip( clipBuilder, clipSource, 1.0f, 0 );

				Assert.IsTrue( frames > 1, $"a walk should be more than one frame - got {frames}" );

				clipBuilder.Looping = true;
				Assert.IsTrue( clipBuilder.Save( clipPath ) );
			}
			finally
			{
				clipBuilder.Dispose();
			}

			model = CoreAssets.ModelFile.Load( modelPath );
			clip = AnimClipData.Load( clipPath );

			Assert.IsNotNull( model );
			Assert.IsNotNull( clip );

			var boneCount = model.BoneCount;
			Assert.IsTrue( boneCount > 20, $"the citizen has a skeleton - got {boneCount} bones" );

			//A person's worth of metres. The clip below is checked against the same yardstick, and the two have to
			//agree or a pose would pull the mesh apart - so the model's own size is asserted here rather than assumed.
			var bounds = stackalloc float[6];
			model.GetRenderBounds( bounds );

			var height = bounds[5] - bounds[2];
			Assert.IsTrue( height is > 1.0f and < 3.0f, $"the citizen should be a person's height in metres - got {height}" );

			var animated = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
			var clipBones = clip.Bones.Length;

			for ( var bone = 0; bone < clipBones; bone++ )
				animated.Add( clip.Bones[bone].Name );

			var rigged = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

			for ( var bone = 0; bone < boneCount; bone++ )
				rigged.Add( model.BoneName( bone ) );

			//Neither name set contains the other; they just need to overlap enough
			var driven = rigged.Count( name => animated.Contains( name ) );

			Assert.IsTrue( driven * 2 >= boneCount,
				$"a walk should move most of the rig - {driven} of {boneCount}" );

			//And the overlap is by name, so name a few: a clip that landed on the right count by accident would still fail this.
			foreach ( var name in new[] { "pelvis", "spine_0", "spine_2", "hand_L", "head" } )
			{
				Assert.IsTrue( rigged.Contains( name ), $"the citizen should have a {name}" );
				Assert.IsTrue( animated.Contains( name ), $"the walk should move the {name}" );
			}

			//The pose it produces has to be a pose: a rig that came through with a broken unit conversion
			//is metres out, and one that came through with a broken decomposition is not a number at all.
			var pose = new AnimTrs[clipBones];

			Assert.AreEqual( clipBones, clip.Sample( clip.Duration * 0.5f, true, pose ) );

			for ( var bone = 0; bone < clipBones; bone++ )
			{
				var trs = pose[bone];
				var name = clip.Bones[bone].Name;

				bool Wrong( float value ) => float.IsNaN( value ) || float.IsInfinity( value );

				Assert.IsFalse(
					Wrong( trs.Position.x ) || Wrong( trs.Position.y ) || Wrong( trs.Position.z ) ||
					Wrong( trs.Rotation.X ) || Wrong( trs.Rotation.Y ) ||
					Wrong( trs.Rotation.Z ) || Wrong( trs.Rotation.W ) ||
					Wrong( trs.Scale.x ) || Wrong( trs.Scale.y ) || Wrong( trs.Scale.z ),
					$"{name} has a value that is not a number" );

				var offset = trs.Position.Length;

				//A bone under another sits a limb's length from it, and a metre is already generous. A root is not measured against
				//anything - its position is where the character stands - so it only has to be a plausible distance rather than a limb.
				var reach = clip.Bones[bone].Parent < 0 ? 100.0f : 1.0f;

				Assert.IsTrue( offset < reach,
					$"{name} sits {offset} metres from its parent - a unit conversion gone wrong looks exactly like this" );

				Assert.AreEqual( 1.0f, trs.Rotation.Length(), 0.01f,
					$"bone {bone}'s rotation should be a unit quaternion" );
			}
		}
		finally
		{
			if ( model is not null ) model.Dispose();

			File.Delete( modelPath );
			File.Delete( clipPath );
		}
	}
}
