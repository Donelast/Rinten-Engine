using Rinten;
using System.IO;

using CoreAssets = Rinten.CoreAssets;

namespace EngineTests.Core;

//Model attachments: names must resolve to bones and offsets compose in the right order
[TestClass]
[DoNotParallelize]
public class ModelAttachmentsTests
{
	//Where the definition is registered, which is also the model's own name.
	private const string Path = "models/test/attachments.mdl";

	//Two bones: a root, and one a metre above it. Built rather than loaded so the numbers are known: a bone is
	//added with the matrix that undoes its rest pose, so a hand a metre up is a matrix that moves a metre down.
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

	//A definition with one of everything, registered where the model will look.
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

	[TestMethod]
	public void AModelTakesItsAttachmentsFromItsDefinition()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		ThreadSafe.MarkMainThread();

		var model = Built();

		Assert.IsTrue( model.IsValid() );
		Assert.AreEqual( 2, model.BoneCount );

		Defined();

		//The list is read once and kept; this one was asked before there was a definition
		//to read, which is what the editor does when somebody adds the first attachment.
		model.ReloadAttachments();

		Assert.AreEqual( 3, model.Attachments.Count, "the added bone is a place too, so it is listed" );

		var muzzle = model.Attachments.Get( "MUZZLE" );

		Assert.IsNotNull( muzzle, "an attachment is found whatever the case of the name" );
		Assert.AreEqual( "hand", muzzle.Bone?.Name );

		var world = model.GetAttachment( "muzzle" );

		Assert.IsTrue( world.HasValue );
		Assert.IsTrue( world.Value.Position.AlmostEqual( new Vector3( 0.1f, 0, 1 ) ),
			$"the muzzle is a tenth of a metre out from the hand, not {world.Value.Position}" );
	}

	//Attachments on added bones walk down to a real bone, composing offsets correctly
	[TestMethod]
	public void AnAddedBoneCarriesWhatHangsOffIt()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		ThreadSafe.MarkMainThread();

		var model = Built();

		Defined();

		model.ReloadAttachments();

		var mount = model.GetAttachment( "mount" );
		var sight = model.GetAttachment( "sight" );

		Assert.IsTrue( mount.HasValue && sight.HasValue );

		Assert.IsTrue( mount.Value.Position.AlmostEqual( new Vector3( 0, 0.2f, 1 ) ),
			$"the added bone sits where it was put, not {mount.Value.Position}" );

		Assert.IsTrue( sight.Value.Position.AlmostEqual( new Vector3( 0, 0.2f, 1.05f ) ),
			$"and carries its attachment with it, not {sight.Value.Position}" );

		Assert.AreEqual( "hand", model.Attachments.Get( "sight" )?.Bone?.Name,
			"an attachment always ends up on a real bone" );
	}

	//A model with no skeleton can still have attachments
	[TestMethod]
	public void AModelWithNoBonesStillHasPlacesOnIt()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		ThreadSafe.MarkMainThread();

		const string bare = "models/test/attachments_boneless.mdl";

		var builder = new CoreAssets.ModelBuilder();
		var model = Model.FromCore( CoreModel.FromBuilder( builder, bare ) );

		builder.Dispose();

		Assert.IsTrue( model.IsValid() );
		Assert.AreEqual( 0, model.BoneCount, "this one is built with no bones on purpose" );

		var definition = new ModelDefinition();

		definition.Attachments.Add( new ModelDefinition.Attachment
		{
			Name = "top",
			Position = new Vector3( 0, 0, 0.5f ),
		} );

		definition.Bones.Add( new ModelDefinition.ExtraBone
		{
			Name = "post",
			Position = new Vector3( 0.25f, 0, 0 ),
		} );

		definition.Attachments.Add( new ModelDefinition.Attachment
		{
			Name = "lamp",
			Parent = "post",
			Position = new Vector3( 0, 0, 0.75f ),
		} );

		definition.Register( bare );

		model.ReloadAttachments();

		Assert.AreEqual( 3, model.Attachments.Count );

		var top = model.GetAttachment( "top" );
		var lamp = model.GetAttachment( "lamp" );

		Assert.IsTrue( top.HasValue && top.Value.Position.AlmostEqual( new Vector3( 0, 0, 0.5f ) ),
			"an attachment on a boneless model sits where it was put, relative to the model" );

		Assert.IsTrue( lamp.HasValue && lamp.Value.Position.AlmostEqual( new Vector3( 0.25f, 0, 0.75f ) ),
			"and a bone the definition invented carries one just the same" );
	}

	//Ported legacy attachments come across, from the prefab or the compiled file
	[TestMethod]
	public void TheirAttachmentsComeAcross()
	{
		var install = Rinten.Porting.SBox.SBoxBaseContent.Find();

		if ( install is null || !CoreNative.Available )
		{
			Assert.Inconclusive( "s&box is not installed here, or rintencore is not loaded" );
			return;
		}

		var citizen = Directory.EnumerateFiles( install, "citizen.vmdl_c", SearchOption.AllDirectories )
			.FirstOrDefault();

		if ( citizen is null )
		{
			Assert.Inconclusive( "their character is not in this installation" );
			return;
		}

		var read = Rinten.Porting.SBox.SBoxAttachments.Read( citizen, null, Rinten.ForeignBasis.Quake );

		Assert.IsTrue( read.Count > 20, $"their character has more attachments than {read.Count}" );

		var hand = read.FirstOrDefault( x => x.Name.Contains( "hand", System.StringComparison.OrdinalIgnoreCase ) );

		Assert.IsNotNull( hand, "a character with no hand attachment cannot hold anything" );
		Assert.IsFalse( string.IsNullOrWhiteSpace( hand.Parent ), "an attachment with no bone follows nothing" );

		//Their offsets are inches; a hand is not a hundred metres from the wrist.
		foreach ( var attachment in read )
		{
			Assert.IsTrue( attachment.Position.Length < 10.0f,
				$"{attachment.Name} is {attachment.Position.Length} metres out, so the units did not come across" );
		}
	}
}
