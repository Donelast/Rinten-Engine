using Rinten;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests.Core;

//A skinned renderer exposes the attachments its model definition names, posed and as GameObjects
[TestClass]
[DoNotParallelize]
public class RendererAttachmentsTests
{
	//Where the definition is registered, which is also the model's own name.
	private const string Path = "models/test/renderer_attachments.mdl";

	//A root, and a hand a metre above it. Built on the core builder rather than the managed one because only this one writes a
	//skeleton - a bone is added with the matrix that undoes its rest pose, so a hand a metre up is a matrix that moves a metre down.
	private static unsafe Model Built()
	{
		var builder = new CoreAssets.ModelBuilder();

		Assert.IsNotNull( builder );

		try
		{
			var identity = stackalloc float[16];
			for ( var i = 0; i < 16; i++ ) identity[i] = ( i % 5 ) == 0 ? 1.0f : 0.0f;

			Assert.AreEqual( 0, builder.AddBone( "root", -1, identity ) );

			var hand = stackalloc float[16];
			for ( var i = 0; i < 16; i++ ) hand[i] = ( i % 5 ) == 0 ? 1.0f : 0.0f;
			hand[14] = -1.0f;

			Assert.AreEqual( 1, builder.AddBone( "hand", 0, hand ) );

			return Model.FromCore( CoreModel.FromBuilder( builder, Path ) );
		}
		finally
		{
			builder.Dispose();
		}
	}

	//A muzzle out in front of the hand, and one off a bone the definition adds.
	private static ModelDefinition Defined()
	{
		var definition = new ModelDefinition();

		definition.Attachments.Add( new ModelDefinition.Attachment
		{
			Name = "muzzle",
			Parent = "hand",
			Position = new Vector3( 0.1f, 0, 0 ),
		} );

		definition.Bones.Add( new ModelDefinition.ExtraBone
		{
			Name = "mount",
			Parent = "hand",
			Position = new Vector3( 0, 0.2f, 0 ),
		} );

		definition.Attachments.Add( new ModelDefinition.Attachment
		{
			Name = "sight",
			Parent = "mount",
			Position = new Vector3( 0, 0, 0.05f ),
		} );

		definition.Register( Path );

		return definition;
	}

	//A scene with the model on a renderer, its attachments already read.
	private static SkinnedModelRenderer Staged( out Scene scene, out GameObject thing )
	{
		ThreadSafe.MarkMainThread();

		var model = Built();

		Assert.IsTrue( model.IsValid() );
		Assert.AreEqual( 2, model.BoneCount );
		Assert.AreEqual( Path, model.ResourcePath, "the definition is found by the model's own path" );

		Defined();

		//Read before the renderer exists, so nothing is holding the list this drops - which is what the editor's reload has to be careful of too.
		model.ReloadAttachments();

		scene = new Scene();
		using var scope = scene.Push();

		thing = scene.CreateObject();

		var renderer = thing.Components.Create<SkinnedModelRenderer>( false );
		renderer.Model = model;
		renderer.Enabled = true;

		return renderer;
	}

	//Renderer.GetAttachment returns the model's place, including names added only by the definition
	[TestMethod]
	public void ARendererAnswersForTheAttachmentsTheDefinitionNames()
	{
		var renderer = Staged( out var scene, out _ );
		using var scope = scene.Push();

		var muzzle = renderer.GetAttachment( "muzzle", worldSpace: false );

		Assert.IsTrue( muzzle.HasValue, "the renderer has to find an attachment the definition names" );
		Assert.IsTrue( muzzle.Value.Position.AlmostEqual( new Vector3( 0.1f, 0, 1 ) ),
			$"the muzzle is a tenth of a metre out from the hand, not {muzzle.Value.Position}" );

		//One hung off a bone the definition adds, walked down to the real bone with both offsets composed on the way.
		var sight = renderer.GetAttachment( "sight", worldSpace: false );

		Assert.IsTrue( sight.HasValue, "a place on an added bone is reachable too" );
		Assert.IsTrue( sight.Value.Position.AlmostEqual( new Vector3( 0, 0.2f, 1.05f ) ),
			$"the sight is up the mount and out along it, not {sight.Value.Position}" );

		Assert.IsNull( renderer.GetAttachment( "nothing", worldSpace: false ),
			"a name nothing has is nothing, not the origin" );
	}

	//An attachment is carried about by the object the renderer is on. Read after a tick because a moved object pushes its
	//transform to the scene model on the frame, not on the assignment - the same one frame of lag everything else posed reads.
	[TestMethod]
	public void AnAttachmentIsWhereTheObjectCarriedIt()
	{
		var renderer = Staged( out var scene, out var thing );
		using var scope = scene.Push();

		thing.WorldPosition = new Vector3( 5, 0, 0 );
		scene.GameTick( 1.0 / 60.0 );

		var muzzle = renderer.GetAttachment( "muzzle" );

		Assert.IsTrue( muzzle.HasValue );
		Assert.IsTrue( muzzle.Value.Position.AlmostEqual( new Vector3( 5.1f, 0, 1 ) ),
			$"the muzzle went with the model, and is at {muzzle.Value.Position}" );
	}

	//CreateAttachments puts a GameObject on each attachment, including definition-only ones
	[TestMethod]
	public void TurningAttachmentsOnMakesAnObjectForEachOfThem()
	{
		var renderer = Staged( out var scene, out var thing );
		using var scope = scene.Push();

		Assert.IsNull( renderer.GetAttachmentObject( "muzzle" ), "off by default, so there is nothing yet" );

		renderer.CreateAttachments = true;

		var muzzle = renderer.GetAttachmentObject( "muzzle" );

		Assert.IsNotNull( muzzle, "an attachment the definition names has to get an object" );
		Assert.AreEqual( thing, muzzle.Parent, "it hangs off the renderer's own object" );
		Assert.IsTrue( muzzle.Flags.Contains( GameObjectFlags.Attachment ),
			"flagged, or the scene tree lists it as an ordinary object somebody made" );
		Assert.IsTrue( muzzle.LocalTransform.Position.AlmostEqual( new Vector3( 0.1f, 0, 1 ) ),
			$"and it stands on the attachment, not at the root - it is at {muzzle.LocalTransform.Position}" );

		Assert.IsNotNull( renderer.GetAttachmentObject( "sight" ), "including the ones on added bones" );

		renderer.CreateAttachments = false;

		Assert.IsNull( renderer.GetAttachmentObject( "muzzle" ), "and switching it off lets them go again" );
	}

	//One switched off in the editor is one the model does not have. The eye in the outliner has to mean something, and the only
	//thing it can mean for an attachment is that nothing finds it - there is nothing else about a named place to turn off.
	[TestMethod]
	public void ADisabledAttachmentIsNotThere()
	{
		var renderer = Staged( out var scene, out _ );
		using var scope = scene.Push();

		Assert.IsNotNull( renderer.GetAttachment( "muzzle", worldSpace: false ) );

		Assert.IsTrue( ResourceLibrary.TryGet<ModelDefinition>( Path, out var definition ) );

		definition.Attachments.First( x => x.Name == "muzzle" ).Enabled = false;
		renderer.Model.ReloadAttachments();

		Assert.IsNull( renderer.GetAttachment( "muzzle", worldSpace: false ),
			"a switched-off attachment is gone, not merely hidden in the editor" );
		Assert.IsNotNull( renderer.GetAttachment( "sight", worldSpace: false ),
			"and the ones beside it are untouched" );
	}
}
