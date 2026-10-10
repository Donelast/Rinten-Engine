using System;
using System.Text.Json.Nodes;
using Editor;

namespace UnitTests.Editor;

//The shape of what goes into the scene's history, and what comes back out of it. Applying a record needs a scene and so belongs in
//the editor; reading one does not, and reading one wrongly is how a history quietly stops being about the document it describes.
[TestClass]
public class SceneEditRecordTest
{
	private static JsonObject AnEdit( string name )
	{
		var record = SceneEdit.Record( name );

		SceneEdit.AddGameObject( record, Guid.NewGuid(), Guid.Empty, Guid.Empty,
			new JsonObject { ["Name"] = "Cube" } );

		return record;
	}

	[TestMethod]
	public void AnEditHasTwoSidesAndBothComeBack()
	{
		var after = AnEdit( "Move" );
		var before = AnEdit( "Move" );

		var record = SceneEdit.Both( "Move", after, before );

		Assert.AreEqual( "Move", SceneEdit.NameOf( record ) );
		Assert.AreSame( after, SceneEdit.Side( record, after: true ) );
		Assert.AreSame( before, SceneEdit.Side( record, after: false ) );
	}

	//A journal written before edits had two sides holds the one side at the top level. It
	//still reads, because somebody's history is not thrown away for being a version behind.
	[TestMethod]
	public void AnOlderRecordIsReadAsTheSideItHas()
	{
		var record = AnEdit( "Move" );

		Assert.AreSame( record, SceneEdit.Side( record, after: true ) );
		Assert.IsNull( SceneEdit.Side( record, after: false ),
			"there is no way back through a record that never wrote one" );
	}

	[TestMethod]
	public void ACheckpointSaysWhichVersionWasWritten()
	{
		var mark = SceneEdit.Checkpoint( 0xDEADBEEF );

		Assert.IsTrue( SceneEdit.IsCheckpoint( mark ) );
		Assert.AreEqual( 0xDEADBEEFul, SceneEdit.CheckpointDocument( mark ) );

		Assert.IsFalse( SceneEdit.IsCheckpoint( AnEdit( "Move" ) ),
			"an edit is not a mark, or replaying would stop at the first one" );
	}

	[TestMethod]
	public void AnEditThatChangesNothingSaysSo()
	{
		Assert.IsTrue( SceneEdit.IsEmpty( SceneEdit.Record( "Nothing" ) ) );
		Assert.IsFalse( SceneEdit.IsEmpty( AnEdit( "Move" ) ) );

		var removed = SceneEdit.Record( "Delete" );

		SceneEdit.AddRemoved( removed, Guid.NewGuid(), isGameObject: true );

		Assert.IsFalse( SceneEdit.IsEmpty( removed ), "taking something away is a change" );
	}

	//The state written into a record is a copy. The object it came from is the undo system's and goes on being
	//edited; a record that shared it would describe whatever happened next instead of what happened then.
	[TestMethod]
	public void ARecordKeepsItsOwnCopyOfTheState()
	{
		var state = new JsonObject { ["Name"] = "Cube" };
		var record = SceneEdit.Record( "Rename" );

		SceneEdit.AddGameObject( record, Guid.NewGuid(), Guid.Empty, Guid.Empty, state );

		state["Name"] = "Sphere";

		var written = (record["gameObjects"] as JsonArray)?[0]?["state"]?["Name"]?.ToString();

		Assert.AreEqual( "Cube", written );
	}
}
