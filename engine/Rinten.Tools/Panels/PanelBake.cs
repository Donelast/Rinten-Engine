using Rinten;
using Rinten.UI;
using System;

namespace Editor;

//Paints via the Paint API into a reused, step-grown bitmap shown as a panel's background texture
internal sealed class PanelBake : IDisposable
{
	//The bitmap painted into, which outlives any one frame.
	private Bitmap canvas;

	//What the panel is wearing.
	private Texture texture;

	//Whether that texture is ours to dispose, or somebody else's to keep.
	private bool own;

	//The size last painted, in the painter's own pixels.
	private Rect painted;

	//How long the bitmap has been bigger than it needs to be.
	private RealTimeSince sinceRoomy;

	//Bitmaps are sized in whole steps, so a slow resize is not a reallocation a frame.
	private const int Step = 64;

	//How long an oversized bitmap is kept before it is taken back down.
	private const float RoomyFor = 2.0f;

	/// <summary>Whether anything is being worn at all.</summary>
	public bool HasPicture => texture is not null;

	/// <summary>Paints into the picture and puts it on the panel, where the painter asked or the room it has changed shape.</summary>
	public void Paint( Panel target, Rect rect, bool asked, bool paintedOnce, Func<Bitmap, Rect, bool> paint )
	{
		if ( target?.Style is null ) return;

		var width = rect.Width.CeilToInt();
		var height = rect.Height.CeilToInt();

		if ( width <= 0 || height <= 0 ) return;
		if ( width > 16384 || height > 16384 ) return;

		var resized = painted.Size != new Vector2( width, height );
		if ( !asked && !resized && paintedOnce ) return;

		painted = new Rect( 0, 0, width, height );

		var wantedWidth = Grown( width );
		var wantedHeight = Grown( height );

		var roomy = canvas is not null
			&& ( canvas.Width > wantedWidth + Step || canvas.Height > wantedHeight + Step );

		if ( !roomy ) sinceRoomy = 0;

		var fits = canvas is not null
			&& canvas.Width >= width && canvas.Height >= height
			&& ( !roomy || sinceRoomy < RoomyFor );

		if ( !fits )
		{
			canvas?.Dispose();
			canvas = new Bitmap( wantedWidth, wantedHeight );
		}

		canvas.Clear( Color.Transparent );

		if ( !paint( canvas, painted ) )
		{
			Clear( target );
			return;
		}

		if ( own && texture is not null && texture.Width == canvas.Width && texture.Height == canvas.Height )
		{
			texture.Update( canvas );
			FitTo( target );
			return;
		}

		if ( own )
			texture?.Dispose();

		texture = canvas.ToTexture( mips: false );
		own = true;
		target.Style.BackgroundImage = texture;
		FitTo( target );
	}

	/// <summary>Wears a texture somebody else keeps - a scene already on the device, drawn by the renderer rather than painted here.</summary>
	public void Wear( Panel target, Texture ready )
	{
		if ( target?.Style is null ) return;
		if ( ReferenceEquals( texture, ready ) ) return;

		if ( own )
			texture?.Dispose();

		texture = ready;
		own = false;
		target.Style.BackgroundImage = ready;
		target.Style.BackgroundSizeX = Length.Percent( 100 );
		target.Style.BackgroundSizeY = Length.Percent( 100 );
	}

	/// <summary>Takes the picture off, so what is inside the panel is what shows.</summary>
	public void Clear( Panel target )
	{
		if ( target?.Style is null ) return;
		if ( target.Style.BackgroundImage is null ) return;

		if ( own )
			texture?.Dispose();

		texture = null;
		own = false;
		target.Style.BackgroundImage = null;
	}

	//The picture is drawn at the size of the bitmap rather than stretched to the panel, because the bitmap is the larger of the two whenever it was grown.
	private void FitTo( Panel target )
	{
		if ( canvas is null || target?.Style is null ) return;

		target.Style.BackgroundSizeX = Length.Pixels( canvas.Width );
		target.Style.BackgroundSizeY = Length.Pixels( canvas.Height );
	}

	private static int Grown( int size )
	{
		if ( size <= Step ) return size;

		return ((size + Step - 1) / Step) * Step;
	}

	public void Dispose()
	{
		if ( own )
			texture?.Dispose();

		texture = null;
		own = false;

		canvas?.Dispose();
		canvas = null;
	}
}
