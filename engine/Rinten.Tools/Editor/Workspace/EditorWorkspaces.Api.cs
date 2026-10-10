using System;

namespace Editor;

/// <summary>What an editor built as a workspace may ask of the workspaces around it.</summary>
public static class EditorWorkspaces
{
	/// <summary>A workspace that belongs to an editor type has just become the one on screen. Fired after the switch, with the type the workspace was opened for - the editor's own class for a whole-editor workspace and for a workspace of documents alike.</summary>
	public static event Action<Type> Entered;

	/// <summary>Whether the workspace on screen is the one opened for this editor type.</summary>
	public static bool IsCurrent( Type editorType )
		=> WorkspaceArea.Current.IsValid() && WorkspaceArea.Current.CurrentEditorType == editorType;

	internal static void RaiseEntered( Type editorType )
	{
		if ( editorType is null ) return;

		try
		{
			Entered?.Invoke( editorType );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"A workspace listener threw on entering {editorType.Name}" );
		}
	}
}
