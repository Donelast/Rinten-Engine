using System;

namespace Editor.MdlEditor;

//The short way to a model's graph: a right click on a .mdl , or on a skinned renderer, instead of the model editor and then its button.
internal static class ModelGraphMenu
{
	[Event( "asset.contextmenu", Priority = 45 )]
	public static void OnAssetContext( AssetContextMenu e )
	{
		if ( e.SelectedList.Count != 1 ) return;

		var asset = e.SelectedList[0].Asset;
		if ( asset is null || !ModelGraphResolver.IsModelExtension( asset.AssetType?.FileExtension ) ) return;

		e.Menu.AddOption( "Open Animation Graph", "account_tree", () => Window.OpenGraphOf( asset ) );
	}

	/// <summary>Raised by editors that only know a model's path, such as the component menu.</summary>
	[Event( "model.opengraph" )]
	public static void OnOpenGraph( string modelPath )
	{
		if ( string.IsNullOrWhiteSpace( modelPath ) ) return;

		if ( AssetSystem.FindByPath( modelPath ) is { } asset )
			Window.OpenGraphOf( asset );
	}
}
