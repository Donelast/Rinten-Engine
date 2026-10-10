namespace Editor.MdlEditor;

/// <summary>What the model page needs from its host editor: undo, the file and clip playback</summary>
public interface IModelPageHost
{
	/// <summary>An edit is about to be made under this name; the state before it is what undo goes back to.</summary>
	void PushModelUndo( string name );

	/// <summary>The edit is done; commit it as one step.</summary>
	void CommitModelUndo( string name = null );

	/// <summary>Write the definition to its file and compile it.</summary>
	bool SaveModel();

	/// <summary>Play one of the model's own clips in the preview, or stop.</summary>
	void PlayModelClip( ModelDefinition.ModelClip clip );

	/// <summary>Something on the page was edited.</summary>
	void OnModelEdited();
}
