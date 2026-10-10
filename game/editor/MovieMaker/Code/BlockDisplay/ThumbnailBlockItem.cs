using Rinten;
using Rinten.MovieMaker;
using Rinten.UI;

namespace Editor.MovieMaker.BlockDisplays;

#nullable enable

/// <summary>A track whose value has a picture: the picture, and its name beside it.</summary>
public abstract class ThumbnailBlockItem<T> : PropertyBlockItem<T>
{
	private readonly ChildPool<ThumbnailSpanPanel> _spans;

	protected ThumbnailBlockItem()
	{
		_spans = new ChildPool<ThumbnailSpanPanel>( this, "block-span" );
	}

	protected abstract Pixmap? GetThumbnail( MovieTimeRange timeRange );

	protected virtual string? GetLabel( MovieTimeRange timeRange ) => null;

	protected override void OnResize() => Rebuild();

	protected override void OnBlockChanged( MovieTimeRange timeRange ) => Rebuild();

	private void Rebuild()
	{
		_spans.Begin();

		foreach ( var range in GetPaintBlocks( TimeRange ) )
		{
			var rect = GetRect( range );
			var span = _spans.Next();

			span.Style.Left = Length.Pixels( rect.Left );
			span.Style.Width = Length.Pixels( rect.Width );

			span.Show( GetThumbnail( range ), GetLabel( range ) );
		}

		_spans.End();
	}
}

/// <summary>One stretch of a thumbnail track.</summary>
public sealed class ThumbnailSpanPanel : Panel
{
	private readonly Image _image;
	private readonly Rinten.UI.Label _label;

	private Texture? _texture;
	private Pixmap? _shown;

	public ThumbnailSpanPanel()
	{
		_image = AddChild<Image>( "thumb" );
		_label = AddChild<Rinten.UI.Label>( "block-text" );
	}

	public void Show( Pixmap? thumbnail, string? text )
	{
		SetThumbnail( thumbnail );

		_label.Text = text ?? string.Empty;
		_label.SetClass( "hidden", text is null );
	}

	//The pixmap is the asset system's and is handed back the same each time, so the texture made from it is only made once.
	private void SetThumbnail( Pixmap? thumbnail )
	{
		if ( ReferenceEquals( _shown, thumbnail ) ) return;

		_shown = thumbnail;

		_texture?.Dispose();
		_texture = thumbnail?.picture?.ToTexture( mips: false );

		_image.Texture = _texture;
		_image.SetClass( "hidden", _texture is null );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		_texture?.Dispose();
		_texture = null;
	}
}

public sealed class ResourceBlockItem<T> : ThumbnailBlockItem<T>
	where T : Resource
{
	protected override string? GetLabel( MovieTimeRange timeRange ) => Block.GetValue( timeRange.Start ) is { ResourceName: { } name }
		? name
		: null;

	protected override Pixmap? GetThumbnail( MovieTimeRange timeRange ) => Block.GetValue( timeRange.Start ) is { ResourcePath: { } path }
		? AssetSystem.FindByPath( path )?.GetAssetThumb()
		: null;
}
