using Editor.TreeBrowser;

namespace Editor;

//Shared right-click menu options for any field that references an asset
internal static class AssetFieldMenu
{
	/// <summary>Adds the three ways of opening an asset to a field's right-click menu. Every option is added whether or not it leads anywhere, greyed out where it does not, so the menu is the same shape every time it opens.</summary>
	public static void AddOpenOptions( Menu menu, Asset asset )
	{
		var real = asset is { IsDeleted: false };

		//What it is, without leaving the field: the inspector shows the asset itself - its thumbnail, its
		//properties, what references it - where a moment ago it was showing whatever the field belongs to.
		menu.AddOption( "Open in Inspector", "manage_search", () => EditorUtility.Inspect( asset ) )
			.Enabled = real;

		//And in the editor for its kind, where there is one. Off rather than missing when there is not,
		//because a field whose menu changes length by what is in it is one you have to read before clicking.
		menu.AddOption( "Open in Editor", "edit", () => asset?.OpenInEditor() )
			.Enabled = real && !asset.IsProcedural && IAssetEditor.HasEditorFor( asset );

		menu.AddOption( "Find in Asset Browser", "search", () => TreeAssetBrowser.OpenTo( asset ) )
			.Enabled = real && !asset.IsProcedural;
	}

	/// <summary>Whether the property row above a control is the one that will carry its menu.</summary>
	public static bool RowCarriesMenu( ControlWidget widget )
	{
		if ( !widget.IsValid() ) return false;

		for ( var w = widget.Parent; w.IsValid(); w = w.Parent )
		{
			if ( w is ControlSheetRow ) return true;
		}

		return false;
	}
}
