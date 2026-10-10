using Rinten;
using Rinten.UI;
using System;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>A curve drawn into a picture, small enough to sit on a button.</summary>
public sealed class FunctionPreviewPanel : BitmapPanel
{
	private const int Steps = 16;
	private const float Margin = 1.5f;

	private Func<float, float>? _function;

	public Color Tint { get; set; } = Color.White;

	public Func<float, float>? Function
	{
		get => _function;
		set
		{
			if ( _function == value ) return;

			_function = value;

			MarkDirty();
		}
	}

	public FunctionPreviewPanel()
	{
		AddClass( "function-preview" );
	}

	protected override Vector2Int BitmapSize => new(
		Box.Rect.Width.FloorToInt(),
		Box.Rect.Height.FloorToInt() );

	protected override void OnDrawBitmap( Bitmap bitmap )
	{
		if ( _function is not { } function ) return;

		var rect = bitmap.Rect.Shrink( Margin );

		if ( rect.Width <= 0f || rect.Height <= 0f ) return;

		var points = new Vector2[Steps + 1];

		for ( var i = 0; i <= Steps; ++i )
		{
			var t = (float)i / Steps;

			points[i] = new Vector2(
				rect.Left + t * rect.Width,
				rect.Bottom - Math.Clamp( function( t ), 0f, 1f ) * rect.Height );
		}

		bitmap.SetAntialias( true );
		bitmap.SetPen( Tint, 2f );
		bitmap.DrawLines( points );
	}
}

/// <summary>An enum picker that shows each mode as the curve it applies.</summary>
public sealed class FunctionSelector<T> : EnumIconDropDown<T>
	where T : struct, Enum
{
	private readonly Func<T, Func<float, float>?> _getFunction;

	public FunctionSelector( string title, Func<T, Func<float, float>?> getFunction, Func<T> getValue, Action<T> setValue )
		: base( title, getValue, setValue )
	{
		_getFunction = getFunction;

		AddClass( "function-selector" );
	}

	protected override void OnBuildPreview( Panel host, T value )
	{
		if ( _getFunction( value ) is not { } function )
		{
			host.AddChild<IconPanel>().Text = "question_mark";
			return;
		}

		host.AddChild<FunctionPreviewPanel>().Function = function;
	}
}
