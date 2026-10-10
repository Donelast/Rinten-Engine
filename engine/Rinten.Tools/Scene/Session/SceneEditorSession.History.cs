using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>The scene's history on the disk: every edit, in order, as it was made.</summary>
public partial class SceneEditorSession
{
	//The journal for this scene, while there is a scene to keep one for.
	internal EditJournal Journal { get; private set; }

	//Edits held back because the journal is not open yet - or because they were made while the journal
	//was being replayed, which is the editor putting the scene together rather than somebody editing it.
	private bool replaying;

	//Where this scene's history lives. Beside the project rather than beside the scene: a journal is one person's unfinished
	//work, not a thing to commit, hand over or ship. Named after the scene's path so two scenes cannot share one.
	private string JournalPath()
	{
		var resource = Scene?.Source?.ResourcePath;

		if ( string.IsNullOrEmpty( resource ) ) return null;

		var root = Project.Current?.GetRootPath();

		if ( string.IsNullOrEmpty( root ) ) return null;

		var name = Path.GetFileNameWithoutExtension( resource );
		var id = (uint)resource.ToLowerInvariant().FastHash();

		return Path.Combine( root, ".rinten", "history", $"{name}.{id:x}.journal" );
	}

	//Hash of the scene file from the resource's own record, not re-read (background write may lag)
	private ulong DocumentHash()
	{
		try
		{
			//FastHash is a 32 bit number; the journal keeps 64, so there is room to put something else beside it later.
			if ( Scene?.Source is { LastSavedSourceHash: not 0 } written )
				return (ulong)(uint)written.LastSavedSourceHash;

			//Nothing has been written or read through the resource system yet, which is a scene opened from source and not saved since.
			var asset = Scene?.Source is null ? null : AssetSystem.FindByPath( Scene.Source.ResourcePath );
			var file = asset?.GetSourceFile( true );

			if ( string.IsNullOrEmpty( file ) || !File.Exists( file ) ) return 0;

			return (ulong)(uint)File.ReadAllText( file ).FastHash();
		}
		catch ( Exception )
		{
			return 0;
		}
	}

	//Opens this scene's history, and puts back anything that was not written out before the editor last stopped.
	internal void OpenHistory()
	{
		if ( IsMounted ) return;

		var path = JournalPath();

		if ( string.IsNullOrEmpty( path ) ) return;

		Journal?.Dispose();
		Journal = EditJournal.Open( path, DocumentHash() );

		if ( Journal is null ) return;

		if ( Journal.Count == 0 ) return;

		var records = Read();

		//Which version of the document the journal expects to find: the last checkpoint it wrote, or the one it was started from.
		var expected = Journal.BaseHash;

		foreach ( var record in records )
		{
			if ( SceneEdit.IsCheckpoint( record ) ) expected = SceneEdit.CheckpointDocument( record );
		}

		//Edits made against another version of this file. Not applied - they describe
		//objects in a document that is no longer there - and not thrown away either.
		if ( expected != DocumentHash() )
		{
			SetAside( path );
			return;
		}

		Recover( records );
		SeedUndo( records );
	}

	//Every record in the journal, as objects.
	private List<JsonObject> Read()
	{
		var records = new List<JsonObject>();

		if ( Journal is null ) return records;

		foreach ( var bytes in Journal.Read() )
		{
			try
			{
				if ( JsonNode.Parse( bytes ) is JsonObject json ) records.Add( json );
			}
			catch ( Exception )
			{
				//A record that will not parse is one nobody can act on. The ones around it are still good.
			}
		}

		return records;
	}

	//Puts a journal that does not belong to this file out of the way, and says so.
	private void SetAside( string path )
	{
		var kept = $"{path}.orphan";
		var held = Journal.Count;

		try
		{
			Journal.Dispose();
			Journal = null;

			if ( File.Exists( kept ) ) File.Delete( kept );

			File.Move( path, kept );

			Log.Warning( $"'{Scene?.Name}' has {held} edits from a session that was working on a " +
				$"different version of the file. They have been kept at {kept} and not applied." );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not set aside the edit journal at {path}" );
		}

		Journal = EditJournal.Open( path, DocumentHash() );
	}

	//Replays journal edits after a crash and saves; on failure leaves scene and journal untouched
	private void Recover( List<JsonObject> records )
	{
		//Everything after the last checkpoint: the edits the document on the disk does not have yet.
		//What is before it is history, and history is not replayed - the file already holds it.
		var from = records.FindLastIndex( SceneEdit.IsCheckpoint ) + 1;
		var pending = records.Count - from;

		if ( pending <= 0 ) return;

		Log.Info( $"'{Scene?.Name}': putting back {pending} edits that were not written out" );

		replaying = true;

		try
		{
			using var scope = Scene.Push();

			for ( var i = from; i < records.Count; i++ )
			{
				//The side that says what the edit left behind. The other side is what undo walks back over, and putting a scene back together is going forwards.
				var after = SceneEdit.Side( records[i], after: true );

				if ( after is null ) continue;

				SceneEdit.Apply( Scene, after );
			}
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Could not put back the edits for '{Scene?.Name}' - it is as it was on the disk, " +
				$"and the edits are still in {Journal.Path}" );

			return;
		}
		finally
		{
			replaying = false;
		}

		//On the disk now, and the journal starts again from it.
		HasUnsavedChanges = true;

		Checkpoint();
	}

	//Writes one edit into the history. Called as the edit finishes, from the undo scope
	//that made it - it already knows which objects were touched and holds their state.
	internal void RecordEdit( JsonObject record )
	{
		if ( replaying ) return;
		if ( record is null ) return;
		if ( Journal is null ) return;

		Journal.Append( record.ToJsonString() );
	}

	//Journal size kept for undo history before it is restarted from the current document
	private const long HistoryToKeep = 8 * 1024 * 1024;

	//Marks the file as up to date instead of clearing; restarts only past HistoryToKeep
	private void RebaseHistory()
	{
		if ( Journal is null ) return;

		var document = DocumentHash();

		if ( Journal.Bytes > HistoryToKeep )
		{
			Journal.Rebase( document );
			return;
		}

		Journal.Append( SceneEdit.Checkpoint( document ).ToJsonString() );

		//And onto the disk. Records are pushed there on a cadence rather than one at a time - see EditJournal.SyncSoon - and a checkpoint
		//is exactly the moment the journal has to have landed, because it is the record that says the file holds everything before it.
		Journal.Sync();
	}

	//How many edits back the history goes when a scene is opened.
	private const int UndoToRestore = 100;

	//Loads journal edits into undo so history survives restarts; undos are written as new records
	private void SeedUndo( List<JsonObject> records )
	{
		var edits = records.Where( x => !SceneEdit.IsCheckpoint( x ) ).ToList();

		if ( edits.Count == 0 ) return;

		var from = Math.Max( 0, edits.Count - UndoToRestore );

		for ( var i = from; i < edits.Count; i++ )
		{
			var record = edits[i];

			var name = SceneEdit.NameOf( record );
			var after = SceneEdit.Side( record, after: true );
			var before = SceneEdit.Side( record, after: false );

			//Only an edit with both sides can be walked in both directions. One from an older journal has only the side that puts it back.
			if ( after is null || before is null ) continue;

			UndoSystem.Insert( name,
				() => ApplyFromHistory( name, before, after ),
				() => ApplyFromHistory( name, after, before ) );
		}

		Log.Info( $"'{Scene?.Name}': {edits.Count - from} edits of history to undo through" );
	}

	//Applies one side of a remembered edit, and writes down that it happened.
	private void ApplyFromHistory( string name, JsonObject side, JsonObject other )
	{
		using var scope = Scene.Push();

		SceneEdit.Apply( Scene, side );

		HasUnsavedChanges = true;

		//Written as an edit of its own: what the scene is now is the side just applied, and the way back is the side that was not.
		Journal?.Append( SceneEdit.Both( name, side, other ).ToJsonString() );
	}

	//Closes the history, leaving whatever is in it on the disk.
	private void CloseHistory()
	{
		Journal?.Dispose();
		Journal = null;
	}
}
