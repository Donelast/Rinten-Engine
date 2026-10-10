namespace Rinten;

public partial class Bitmap
{
	//Every transform below moves what is drawn after it, and there is no way back to where you were other than remembering. Save before a turn, undo it after, and the next thing drawn is where it expects to be.
	/// <summary>Remembers where everything is being drawn, so it can be put back.</summary>
	public void Push() => _canvas.Save();

	/// <summary>Back to where the last <see cref="Push"/> was.</summary>
	public void Pop() => _canvas.Restore();

	/// <summary>Moves everything drawn after it.</summary>
	public void TranslateBy( Vector2 by ) => _canvas.Translate( by.x, by.y );

	/// <summary>Scales everything drawn after it, about the origin.</summary>
	public void ScaleBy( float x, float y ) => _canvas.Scale( x, y );

	/// <summary>Turns everything drawn after it, in degrees, about the origin - unlike <see cref="Rotate(float)"/>, which turns the picture itself.</summary>
	public void RotateBy( float degrees ) => _canvas.RotateDegrees( degrees );

	/// <summary>Turns everything drawn after it about a point.</summary>
	public void RotateBy( float degrees, Vector2 around ) => _canvas.RotateDegrees( degrees, around.x, around.y );

	/// <summary>Forgets every transform: back to the picture's own pixels.</summary>
	public void ResetTransform() => _canvas.SetMatrix( SkiaSharp.SKMatrix.CreateIdentity() );

	/// <summary>Nothing outside this rectangle is drawn.</summary>
	public void ClipRect( Rect rect )
	{
		_canvas.ClipRect( new SkiaSharp.SKRect( rect.Left, rect.Top, rect.Right, rect.Bottom ) );
	}
}
