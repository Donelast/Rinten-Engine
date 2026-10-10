using Rinten.UI;
using SkiaSharp;

namespace Rinten;

public partial class Bitmap
{
	/// <summary>Draws a rectangle using the current pen settings.</summary>
	public void DrawRect( Rect rect )
	{
		var skRect = rect.ToSk();
		_canvas.DrawRect( skRect, GetPen() );
	}

	/// <summary>Draws a rectangle using the current pen settings.</summary>
	public void DrawRect( float x, float y, float width, float height )
	{
		DrawRect( new Rect( x, y, x + width, y + height ) );
	}

	/// <summary>Draws a rectangle using the current pen settings.</summary>
	public void DrawRoundRect( Rect rect, Margin margins )
	{
		using var rounded = new SKRoundRect( rect.ToSk() );
		rounded.SetRectRadii( rect.ToSk(), new[] { new SKPoint( margins.Left, margins.Left ), new SKPoint( margins.Top, margins.Top ), new SKPoint( margins.Right, margins.Right ), new SKPoint( margins.Bottom, margins.Bottom ) } );

		var skRect = rect.ToSk();
		_canvas.DrawRoundRect( rounded, GetPen() );
	}

	/// <summary>Draws a circle using the current pen settings.</summary>
	public void DrawCircle( Vector2 center, float radius )
	{
		_canvas.DrawCircle( center.ToSk(), radius, GetPen() );
	}

	/// <summary>Draws a circle using the current pen settings.</summary>
	public void DrawCircle( float x, float y, float radius )
	{
		DrawCircle( new Vector2( x, y ), radius );
	}

	/// <summary>Draws a polygon using the current pen settings.</summary>
	public void DrawPolygon( Vector2[] points )
	{
		using var path = new SKPath();
		path.MoveTo( points[0].ToSk() );

		for ( int i = 1; i < points.Length; i++ )
		{
			path.LineTo( points[i].ToSk() );
		}

		path.Close();
		_canvas.DrawPath( path, GetPen() );
	}

	/// <summary>Draws an arc using the current pen settings.</summary>
	public void DrawArc( Rect rect, float startAngle, float sweepAngle )
	{
		_canvas.DrawArc( rect.ToSk(), startAngle, sweepAngle, false, GetPen() );
	}

	/// <summary>Draws an arc using the current pen settings, with an option to connect to the center.</summary>
	public void DrawArc( Rect rect, float startAngle, float sweepAngle, bool useCenter )
	{
		_canvas.DrawArc( rect.ToSk(), startAngle, sweepAngle, useCenter, GetPen() );
	}

	/// <summary>Draws another bitmap onto this bitmap.</summary>
	public void DrawBitmap( Bitmap bitmap, Rect destRect )
	{
		if ( bitmap is null || !bitmap.IsValid ) return;

		_canvas.DrawBitmap( bitmap._bitmap, destRect.ToSk(), GetPen() );
	}

	/// <summary>Fills a rectangle by repeating a bitmap, aligned to origin and scaled.</summary>
	public void FillTiled( Bitmap tile, Rect dest, Vector2 origin, Vector2 scale )
	{
		if ( tile is null || !tile.IsValid || dest.Width <= 0 || dest.Height <= 0 ) return;

		var sx = scale.x == 0 ? 1.0f : scale.x;
		var sy = scale.y == 0 ? 1.0f : scale.y;

		var matrix = SKMatrix.CreateScale( 1.0f / sx, 1.0f / sy );
		matrix.TransX = -origin.x / sx;
		matrix.TransY = -origin.y / sy;

		using var shader = SKShader.CreateBitmap( tile._bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, matrix );

		var pen = GetPen();
		pen.Style = SKPaintStyle.Fill;
		pen.Shader = shader;

		_canvas.DrawRect( dest.ToSk(), pen );

		pen.Shader = null;
	}

	/// <summary>Draws a line using the current pen settings.</summary>
	public void DrawLine( Vector2 start, Vector2 end )
	{
		_canvas.DrawLine( start.ToSk(), end.ToSk(), GetPen() );
	}

	/// <summary>Draws connected lines through a series of points using the current pen settings.</summary>
	public void DrawLines( params Vector2[] points )
	{
		if ( points == null || points.Length < 2 )
			throw new ArgumentException( "At least two points are required to draw lines.", nameof( points ) );

		var pen = GetPen();

		using var path = new SKPath();
		path.MoveTo( points[0].ToSk() );

		for ( int i = 1; i < points.Length; i++ )
		{
			path.LineTo( points[i].ToSk() );
		}

		_canvas.DrawPath( path, pen );
	}
}
