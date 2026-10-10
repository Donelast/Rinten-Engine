namespace Editor.SpriteEditor;

//Files, dialogs and the window are not here. A dock edits the sprite and hears about edits; where the sprite came from and where it goes is the window's.
/// <summary>The sprite being edited, as the docks see it: the resource, whether it is dirty, and one way to change it that undo knows about.</summary>
public interface ISpriteDocument
{
	/// <summary>The sprite open now. Never null, though it may be empty.</summary>
	Sprite Sprite { get; }

	/// <summary>The asset the sprite was loaded from, or null for one that has never been saved.</summary>
	Asset Asset { get; }

	/// <summary>Whether there are edits that are not on disk.</summary>
	bool IsModified { get; }

	/// <summary>A different sprite is open now - the objects in it are all new.</summary>
	event Action Loaded;

	/// <summary>Undo or redo put the sprite back to an earlier state - every object in it is new.</summary>
	event Action Restored;

	/// <summary>An edit was committed to the sprite that is open.</summary>
	event Action<SpriteChange> Changed;

	/// <summary>Applies an edit and commits it as one undo step.</summary>
	void Edit( string name, Action edit, object source = null );

	/// <summary>Commits whatever has been done to the sprite since the last commit as one undo step. Nothing happens if nothing changed.</summary>
	void Commit( string name, bool coalesce = false, object source = null );
}
