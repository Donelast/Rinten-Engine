namespace Editor;

/// <summary>Renders a clothing icon and writes it beside the resource, with no interface around it - the control asks for one, and the batch below asks for all of them.</summary>
public static class ClothingIconRenderer
{
	public static void RenderIcon( Asset asset, Clothing resource )
	{
		//force an icon path
		var iconInfo = resource.Icon;

		iconInfo.Path = resource.ResourcePath + ".png";
		resource.Icon = iconInfo;

		var clothingSetup = new ClothingScene();
		clothingSetup.Update();
		clothingSetup.InstallClothing( resource );

		clothingSetup.Scene.EditorTick( RealTime.Now - 2, 1 );
		clothingSetup.Scene.EditorTick( RealTime.Now - 1, 1 );

		clothingSetup.Update();

		int size = 512;
		int upscale = 4;

		using var bitmap = new Bitmap( size * upscale, size * upscale );
		clothingSetup.Scene.Camera.RenderToBitmap( bitmap );

		if ( asset.SaveToDisk( resource ) )
		{
			asset.Compile( false );
		}

		var root = asset.AbsolutePath[0..^(asset.RelativePath.Length)];
		var pngPath = root + iconInfo.Path;
		System.IO.Directory.CreateDirectory( System.IO.Path.GetDirectoryName( pngPath ) );

		using var downsampledBitmap = bitmap.Resize( size, size );
		var outputData = downsampledBitmap.ToPng();
		System.IO.File.WriteAllBytes( pngPath, outputData );
	}

	public static async Task SaveAllIcons()
	{
		using var progress = Application.Editor.ProgressSection();
		progress.Title = "Rendering Icons";

		var token = progress.GetCancel();
		var allClothes = AssetSystem.All.Where( x => x.AssetType.FileExtension == "clothing" ).ToArray();

		int i = 0;
		foreach ( var asset in allClothes )
		{
			progress.Title = asset.Name;
			progress.Current = ++i;
			progress.TotalCount = allClothes.Length;

			var resource = asset.LoadResource<Clothing>();

			await Task.Delay( 100 );

			RenderIcon( asset, resource );

			if ( token.IsCancellationRequested )
				return;
		}
	}
}
