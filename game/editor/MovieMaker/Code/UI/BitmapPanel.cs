using Rinten;
using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>A panel that wears a picture it paints itself.</summary>
public abstract class BitmapPanel : Panel
{
	private Bitmap? _bitmap;
	private Texture? _texture;
	private Vector2Int _drawnSize;
	private bool _dirty = true;

	/// <summary>Repaint before the next frame.</summary>
	public void MarkDirty() => _dirty = true;

	//How big the picture should be, in pixels. Zero means nothing to paint.
	protected abstract Vector2Int BitmapSize { get; }

	//Paints onto a bitmap that has already been cleared.
	protected abstract void OnDrawBitmap( Bitmap bitmap );

	//What the bitmap is cleared to before OnDrawBitmap.
	protected virtual Color ClearColor => Color.Transparent;

	//Whether the picture is stretched over the panel, or left for the stylesheet to place.
	protected virtual bool StretchToFit => true;

	public override void Tick()
	{
		base.Tick();

		Repaint();
	}

	private void Repaint()
	{
		var size = BitmapSize;

		if ( size.x <= 0 || size.y <= 0 )
		{
			Clear();
			return;
		}

		var resized = _bitmap is null || _drawnSize != size;

		if ( !_dirty && !resized ) return;

		_dirty = false;
		_drawnSize = size;

		if ( resized )
		{
			_bitmap?.Dispose();
			_bitmap = new Bitmap( size.x, size.y );
		}

		_bitmap!.Clear( ClearColor );

		OnDrawBitmap( _bitmap );

		Present();
	}

	//Puts the bitmap on the panel, filling the texture it already has where that still fits.
	private void Present()
	{
		if ( _bitmap is null ) return;

		if ( _texture is not null && _texture.Width == _bitmap.Width && _texture.Height == _bitmap.Height )
		{
			_texture.Update( _bitmap );
			return;
		}

		_texture?.Dispose();
		_texture = _bitmap.ToTexture( mips: false );

		Style.BackgroundImage = _texture;

		if ( !StretchToFit ) return;

		Style.BackgroundSizeX = Length.Percent( 100 );
		Style.BackgroundSizeY = Length.Percent( 100 );
	}

	private void Clear()
	{
		if ( _texture is null ) return;

		Style.BackgroundImage = null;

		_texture.Dispose();
		_texture = null;

		_bitmap?.Dispose();
		_bitmap = null;

		_drawnSize = default;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		_texture?.Dispose();
		_texture = null;

		_bitmap?.Dispose();
		_bitmap = null;
	}
}
