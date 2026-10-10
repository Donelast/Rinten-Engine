using System;

namespace Editor;

public partial class SceneEditorSession
{
	public SelectionSystem Selection { get; } = new SelectionSystem();

	//when changing the selection, we save it as the previous, so that undo has a frame of reference
	string previousSavedSelection = null;

	/// <summary>Serlialize the current selection to a json string. The aim here is to make something we can transfer back to objects.</summary>
	public string SerializeSelection()
	{
		return Json.Serialize( Selection.OfType<GameObject>().Select( x => x.Id ).Order().ToArray() );
	}

	/// <summary>Take a json string created by SerializeSelection and turn it into a selection</summary>
	public void DeserializeSelection( string selection )
	{
		previousSavedSelection = selection;

		if ( string.IsNullOrWhiteSpace( selection ) )
		{
			Selection.Clear();
			return;
		}

		var guids = Json.Deserialize<Guid[]>( selection );

		Selection.Clear();

		foreach ( var o in guids )
		{
			if ( Scene.Directory.FindByGuid( o ) is GameObject go )
			{
				Selection.Add( go );
			}
		}
	}
}
