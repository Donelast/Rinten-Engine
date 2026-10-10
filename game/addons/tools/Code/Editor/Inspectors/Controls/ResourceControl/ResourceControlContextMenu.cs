namespace Editor.Inspectors;

//What a resource field puts in the property menu.
static class ResourceControlContextMenu
{
	public static void Fill( Menu menu, SerializedProperty property, Resource resource, Asset asset, bool readOnly )
	{
		//Shared open options from AssetFieldMenu, so rules like "has an editor" apply here too
		AssetFieldMenu.AddOpenOptions( menu, asset );

		menu.AddOption( "Clear", "backspace", () => Clear( property ) )
			.Enabled = !readOnly && resource is not null;
	}

	/// <summary>The asset's path, so it pastes into any other field that takes one.</summary>
	public static string ToClipboardString( Resource resource )
	{
		if ( resource is null ) return null;

		return AssetSystem.FindByPath( resource.ResourcePath )?.Path;
	}

	public static void Paste( SerializedProperty property, string path )
	{
		var asset = AssetSystem.FindByPath( path?.Trim() );
		if ( asset is null ) return;

		property.Parent?.NoteStartEdit( property );
		ResourceAssignment.Assign( property, asset );
		property.Parent?.NoteFinishEdit( property );
	}

	static void Clear( SerializedProperty property )
	{
		property.Parent?.NoteStartEdit( property );
		property.SetValue<Resource>( null );
		property.Parent?.NoteFinishEdit( property );
	}
}
