using Rinten.Rendering;
using Rinten.Resources;

namespace Editor.SpriteEditor;

//Where a frame's picture comes from, and the two ways of changing it by hand: another image, or another cell of the same
//sheet. Shared by the frame row in the inspector and the frame dialog, so a frame is edited the same way wherever it is met.
internal static class FrameSources
{
	/// <summary>The image file a picture was made from: the one its embedded generator names, or the texture's own path. Null for one made in memory.</summary>
	public static string ImagePath( Texture texture )
	{
		if ( texture is null ) return null;

		if ( texture.EmbeddedResource?.Data is { } data
			&& data.TryGetPropertyValue( "FilePath", out var path )
			&& path?.GetValue<string>() is { Length: > 0 } file )
			return file;

		return string.IsNullOrWhiteSpace( texture.ResourcePath ) ? null : texture.ResourcePath;
	}

	/// <summary>A texture generated from a file rather than loaded from it, so it is embedded in the sprite and follows the source file when that changes. The same file gives the same texture - the one the sprite's other frames already have - so frames cut from one sheet stay one picture, and are drawn together.</summary>
	public static Texture FromImage( string path )
	{
		var generator = new ImageFileGenerator { FilePath = path };

		return generator.FindOrCreate( ResourceGenerator.Options.Default );
	}

	/// <summary>Asks for an image, and hands over a texture made from it.</summary>
	public static void PickImage( Widget owner, string title, Action<Texture> picked )
	{
		var picker = AssetPicker.Create( owner, AssetType.ImageFile, new() { EnableMultiselect = false } );

		picker.Window.Title = title;
		picker.OnAssetPicked = assets =>
		{
			if ( assets.FirstOrDefault() is not { } asset ) return;
			if ( FromImage( asset.RelativePath ) is { } texture ) picked( texture );
		};

		picker.Window.Show();
	}

	/// <summary>Opens the frame's sheet in the spritesheet importer to pick one cell of it, cut into cells the size the frame's region already is. Nothing opens for a picture that did not come from a file.</summary>
	public static bool PickCell( Sprite.Frame frame, FilterMode filter, Action<RectInt> picked )
	{
		var path = ImagePath( frame?.Texture );
		if ( path is null ) return false;

		var cell = frame.Region is { Width: > 0, Height: > 0 } region ? new Vector2Int( region.Width, region.Height ) : (Vector2Int?)null;

		SpritesheetImporter.Open( path, filter, ( _, cells ) =>
		{
			if ( cells.FirstOrDefault() is not { Width: > 0, Height: > 0 } rect ) return;

			picked( new RectInt( (int)rect.Left, (int)rect.Top, (int)rect.Width, (int)rect.Height ) );
		}, action: "Use This Cell", cellSize: cell );

		return true;
	}
}
