using System;
using System.IO;
using System.Text.Json;

namespace EngineTests;

//A model definition naming clip definitions on disk yields playable sequences
[TestClass]
public class AnimSequencesTest
{
	private static string CitizenModel
		=> Path.Combine( Environment.CurrentDirectory, "addons/citizen/Assets/models/citizen/citizen.fbx" );

	private static string CitizenWalk
		=> Path.Combine( Environment.CurrentDirectory, "addons/citizen/Assets/models/citizen/animations/Citizen@Walk_N.fbx" );

	private static string TempDirectory()
	{
		var path = Path.Combine( Path.GetTempPath(), $"rinten-sequences-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( path );

		return path;
	}

	private static void Write( string path, object definition )
		=> File.WriteAllText( path, JsonSerializer.Serialize( definition, new JsonSerializerOptions { WriteIndented = true } ) );

	//A model definition, its compiled half, and a clip definition beside it - which is what a ported project looks like.
	[TestMethod]
	public void ModelPlaysTheClipsItsDefinitionNames()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( $"rintencore is not loaded: {CoreNative.BuildInfo}" );
			return;
		}

		if ( !File.Exists( CitizenModel ) || !File.Exists( CitizenWalk ) )
		{
			Assert.Inconclusive( $"the citizen is not in this checkout ({CitizenModel})" );
			return;
		}

		var directory = TempDirectory();

		try
		{
			var clipDefinition = Path.Combine( directory, "walk_n.anim" );
			var modelDefinition = Path.Combine( directory, "citizen.mdl" );
			var compiled = Path.Combine( directory, "citizen.mdl_c" );

			Write( clipDefinition, new
			{
				Source = CitizenWalk.Replace( '\\', '/' ),
				Take = 0,
				Scale = 1.0f,
				Looping = true,
				Additive = false,
			} );

			Write( modelDefinition, new
			{
				Meshes = new[] { new { File = CitizenModel.Replace( '\\', '/' ), Scale = 1.0f } },
				Scale = 1.0f,
				Animations = new[] { clipDefinition.Replace( '\\', '/' ) },
			} );

			var built = ModelCompiler.Compile(
				new ModelDefinition
				{
					Meshes = [new ModelDefinition.MeshPart { File = CitizenModel, Scale = 1.0f }],
					Scale = 1.0f,
				},
				directory, compiled );

			Assert.IsTrue( built.Success, built.Reason );

			var model = Model.FromCore( CoreModel.Load( modelDefinition ) );
			Assert.IsNotNull( model, "the compiled model should load" );

			//The skeleton answers what used to be a native question, because a model of ours has no native peer to ask.
			Assert.IsTrue( model.BoneCount > 20, $"the citizen has a skeleton - got {model.BoneCount}" );
			Assert.IsFalse( string.IsNullOrEmpty( model.GetBoneName( 0 ) ) );
			Assert.AreEqual( -1, model.GetBoneParent( 0 ), "the first bone is a root" );

			//A sequence is a clip's file name without its extension.
			Assert.AreEqual( 1, model.AnimationCount );
			Assert.AreEqual( "walk_n", model.GetAnimationName( 0 ) );

			CollectionAssert.Contains( model.SequenceNames.ToList(), "walk_n" );

			//Asking for it compiles it, because nothing else was going to.
			var clip = CoreSkeletons.Clip( model, "walk_n" );

			Assert.IsNotNull( clip, "the clip should have been compiled and loaded" );
			Assert.IsTrue( File.Exists( Path.ChangeExtension( clipDefinition, ".anim_c" ) ),
				"and the compiled half should be on disk beside the definition" );

			Assert.IsTrue( clip.FrameCount > 1, "a walk is more than one frame" );
			Assert.IsTrue( clip.Looping, "the definition said it loops" );

			//Twice is the same clip, not a second copy of it.
			Assert.AreSame( clip, CoreSkeletons.Clip( model, "walk_n" ) );

			//And a name nothing lists is nothing, rather than the first clip.
			Assert.IsNull( CoreSkeletons.Clip( model, "run" ) );

			var skeleton = CoreSkeletons.Skeleton( model );
			Assert.IsNotNull( skeleton );
			Assert.AreSame( skeleton, CoreSkeletons.Skeleton( model ), "one skeleton per model, shared" );

			//The two halves meet: every bone the clip drives is one the model has, or the pose would go nowhere.
			var driven = 0;

			for ( var bone = 0; bone < clip.Bones.Length; bone++ )
			{
				var name = clip.Bones[bone].Name;
				if ( skeleton.FindBone( name ) >= 0 ) driven++;
			}

			Assert.IsTrue( driven * 2 >= model.BoneCount,
				$"the walk should drive most of the rig - {driven} of {model.BoneCount}" );
		}
		finally
		{
			try { Directory.Delete( directory, recursive: true ); }
			catch ( IOException ) { }
		}
	}
}
