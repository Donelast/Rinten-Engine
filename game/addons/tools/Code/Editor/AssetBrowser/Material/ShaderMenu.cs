namespace Editor;

internal static class ShaderMenu
{
	[Event( "asset.contextmenu", Priority = 50 )]
	public static void OnShaderFileAssetContext( AssetContextMenu e )
	{
		//Are all the files we have selected image assets?
		if ( !e.SelectedList.All( x => x.AssetType == AssetType.Shader ) )
			return;

		if ( e.SelectedList.Count == 1 )
			e.Menu.AddOption( $"Create Material", "image", action: () => CreateMaterialUsingShader( e.SelectedList.FirstOrDefault() ) );
	}

	private static void CreateMaterialUsingShader( AssetEntry entry )
	{
		var asset = entry.Asset;
		var assetName = asset.Name;

		var fd = new FileDialog( null );
		fd.Title = "Create Material from Shader..";
		fd.Directory = System.IO.Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = ".mat";
		fd.SelectFile( $"{assetName}.mat" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Material File (*.mat)" );

		if ( !fd.Execute() )
			return;

		//The shader as it is written, not as it is compiled: a material of ours names the file in the project, and the compiler finds what came of it.
		var content = new Dictionary<string, object>
		{
			{ "Shader", asset.RelativePath },
			{ "__version", 1 }
		};

		System.IO.File.WriteAllText( fd.SelectedFile, Json.Serialize( content ) );

		var resultAsset = AssetSystem.RegisterFile( fd.SelectedFile );

		//These 3 lines are gonna be quite common I think.
		TreeBrowser.TreeAssetBrowser.RefreshOpen();
		TreeBrowser.TreeAssetBrowser.Reveal( resultAsset );
		EditorUtility.InspectorObject = resultAsset;
	}
}
