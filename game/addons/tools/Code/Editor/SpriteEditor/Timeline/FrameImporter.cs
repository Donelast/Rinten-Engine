using Rinten.Rendering;

namespace Editor.SpriteEditor;

/// <summary>Getting frames in from files: the asset picker, and the spritesheet importer after it. What arrives goes to <see cref="FrameCommands"/>.</summary>
public sealed class FrameImporter
{
	private readonly ISpriteDocument _document;
	private readonly IAnimationSelection _selection;
	private readonly FrameCommands _frames;
	private readonly Func<FilterMode> _filter;

	public FrameImporter( ISpriteDocument document, IAnimationSelection selection, FrameCommands frames, Func<FilterMode> filter )
	{
		_document = document;
		_selection = selection;
		_frames = frames;
		_filter = filter;
	}

	/// <summary>One frame per picked image.</summary>
	public void ImportImages( Widget owner )
	{
		if ( _selection.Selected is not { } animation ) return;

		var picker = AssetPicker.Create( owner, AssetType.ImageFile, new() { EnableMultiselect = true } );

		picker.Window.StateCookie = "SpriteEditor.Import";
		picker.Window.RestoreFromStateCookie();
		picker.Window.Title = $"Import Image(s) - {_document.Sprite.ResourceName} - {animation.Name}";
		picker.OnAssetPicked = assets => _frames.ImportImages( assets.Select( a => a.RelativePath ) );

		picker.Window.Show();
	}

	/// <summary>Frames cut out of one picked sheet, chosen in the importer.</summary>
	public void ImportSpritesheet( Widget owner )
	{
		if ( _selection.Selected is not { } animation ) return;

		var picker = AssetPicker.Create( owner, AssetType.ImageFile, new() { EnableMultiselect = false } );

		picker.Window.StateCookie = "SpriteEditor.ImportSpritesheet";
		picker.Window.RestoreFromStateCookie();
		picker.Window.Title = $"Import Spritesheet - {_document.Sprite.ResourceName} - {animation.Name}";
		picker.OnAssetPicked = assets =>
		{
			if ( assets.FirstOrDefault() is not { } asset ) return;

			SpritesheetImporter.Open( asset.RelativePath, _filter(), _frames.ImportSpritesheet );
		};

		picker.Window.Show();
	}
}
