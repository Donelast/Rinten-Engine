using Rinten.Rendering;

namespace Rinten.UI;

/// <summary>Draws an SVG file from src, or its child shapes as a vector canvas mapped to the viewBox</summary>
[Library( "svg" ), Expose]
public partial class SvgPanel : Panel
{
	/// <summary>The view box: the rectangle of user space mapped onto the content box. Null for none - one user unit a pixel.</summary>
	public Rect? ViewBox { get; private set; }

	/// <summary>How the view box sits in a box of another shape: <c>preserveAspectRatio</c>.</summary>
	public string PreserveAspectRatio { get; private set; } = "xMidYMid meet";

	public override void SetProperty( string name, string value )
	{
		switch ( name )
		{
			case "viewBox" or "viewbox":
			{
				SetAttribute( name, value );

				var numbers = VectorPath.ParseNumbers( value );
				ViewBox = numbers.Count == 4 && numbers[2] > 0 && numbers[3] > 0
					? new Rect( numbers[0], numbers[1], numbers[2], numbers[3] )
					: null;

				//With a view box and one side given, the other follows its proportions.
				if ( ViewBox is { } box )
					Style.SetPresentation( "aspect-ratio", ( box.Width / box.Height ).ToString( System.Globalization.CultureInfo.InvariantCulture ) );

				MappingChanged();
				return;
			}

			case "preserveAspectRatio" or "preserveaspectratio":
				SetAttribute( name, value );
				PreserveAspectRatio = string.IsNullOrWhiteSpace( value ) ? "xMidYMid meet" : value.Trim();
				MappingChanged();
				return;

			//The svg's size as attributes: under any stylesheet rule, a bare number in pixels.
			case "width" or "height":
				SetAttribute( name, value );
				Style.SetPresentation( name, value );
				return;

			case "fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-width" or "stroke-opacity"
				or "stroke-linecap" or "stroke-linejoin" or "stroke-miterlimit" or "stroke-dasharray"
				or "stroke-dashoffset" or "vector-effect":
				SetAttribute( name, value );
				Style.SetPresentation( name, value );
				return;
		}

		base.SetProperty( name, value );
	}

	public override void SetPropertyObject( string name, object value )
	{
		if ( value is IFormattable number and not Enum )
		{
			SetProperty( name, number.ToString( null, System.Globalization.CultureInfo.InvariantCulture ) );
			return;
		}

		base.SetPropertyObject( name, value );
	}

	/// <summary>Where the view box's pixels start: the content box's corner.</summary>
	public Vector2 ContentOrigin => Box.RectInner.Position;

	/// <summary>From view box coordinates to pixels from <see cref="ContentOrigin"/>.</summary>
	public VectorTransform ViewBoxMapping { get; private set; } = VectorTransform.Identity;

	//What a percentage stroke width or dash is a percentage of: the view box's diagonal over the square root of two, as SVG has it.
	internal float ViewBoxDiagonal
	{
		get
		{
			var size = ViewBox is { } box ? box.Size : Box.RectInner.Size;
			return MathF.Sqrt( ( size.x * size.x + size.y * size.y ) * 0.5f );
		}
	}

	//The view box mapping for a content box of this size, as preserveAspectRatio says.
	private VectorTransform Mapping( Vector2 size )
	{
		if ( ViewBox is not { } box ) return VectorTransform.Identity;

		var sx = size.x / box.Width;
		var sy = size.y / box.Height;

		var words = PreserveAspectRatio.Split( ' ', StringSplitOptions.RemoveEmptyEntries );
		var align = words.Length > 0 ? words[0] : "xMidYMid";
		var slice = words.Length > 1 && words[1] == "slice";

		if ( align != "none" )
		{
			var s = slice ? MathF.Max( sx, sy ) : MathF.Min( sx, sy );
			sx = s;
			sy = s;
		}

		var tx = -box.Left * sx;
		var ty = -box.Top * sy;

		if ( align != "none" )
		{
			var spareX = size.x - box.Width * sx;
			var spareY = size.y - box.Height * sy;

			if ( align.Contains( "xMid" ) ) tx += spareX * 0.5f;
			else if ( align.Contains( "xMax" ) ) tx += spareX;

			if ( align.Contains( "YMid" ) ) ty += spareY * 0.5f;
			else if ( align.Contains( "YMax" ) ) ty += spareY;
		}

		return new VectorTransform( sx, 0, 0, sy, tx, ty );
	}

	private Vector2 mappedOrigin;

	//The mapping worked out again, and every shape told if it moved.
	private void MappingChanged()
	{
		var mapping = Mapping( Box.RectInner.Size );

		//The content box's corner as well: padding moves it without moving the shapes' own boxes, and a kept copy of what they drew would not know.
		var origin = ContentOrigin;
		if ( mapping == ViewBoxMapping && origin == mappedOrigin ) return;

		var remap = mapping != ViewBoxMapping;

		ViewBoxMapping = mapping;
		mappedOrigin = origin;
		IsRenderDirty = true;

		foreach ( var child in Descendants )
		{
			if ( child is SvgElement e )
			{
				//Their triangles are kept from the content box's corner, so a move alone only redraws.
				if ( remap ) e.GeometryVersion++;
				e.IsRenderDirty = true;
			}
		}
	}

	/// <summary>Content path to the SVG file</summary>
	public string Src
	{
		get => _src;

		set
		{
			if ( _src == value )
				return;

			_src = value;
			ReloadTexture();
		}
	}
	internal string _src;

	/// <summary>Optional color to draw the SVG with</summary>
	public string Color
	{
		get => _color;

		set
		{
			if ( _color == value )
				return;

			_color = value;
			ReloadTexture();
		}
	}
	internal string _color;

	Texture texture;
	int sizeHash;

	//The picture a src was drawn into, once it is ready.
	internal Texture Picture => texture;

	public override void FinalLayout( Vector2 offset )
	{
		base.FinalLayout( offset );

		MappingChanged();

		if ( !IsVisible || string.IsNullOrEmpty( Src ) ) return;

		var hash = HashCode.Combine( Box.Rect.Width, Box.Rect.Height, ComputedStyle.BackgroundSizeX, ComputedStyle.BackgroundSizeY );
		if ( hash == sizeHash ) return;

		sizeHash = hash;
		ReloadTexture();
	}

	private async void ReloadTexture()
	{
		if ( ComputedStyle is null || string.IsNullOrEmpty( Src ) )
			return;

		var rect = Box.Rect;
		var width = rect.Width;
		var height = rect.Height;

		if ( ComputedStyle.BackgroundSizeX.HasValue && ComputedStyle.BackgroundSizeX != Length.Undefined )
			width = ComputedStyle.BackgroundSizeX.Value.GetPixels( width );

		if ( ComputedStyle.BackgroundSizeY.HasValue && ComputedStyle.BackgroundSizeY != Length.Undefined )
			height = ComputedStyle.BackgroundSizeY.Value.GetPixels( height );

		var url = $"{Src}?w={(int)width}&h={(int)height}";

		if ( !string.IsNullOrEmpty( Color ) )
		{
			url += $"&color={Color}";
		}

		texture = await Texture.LoadAsync( url );
		IsRenderDirty = true;
	}

	//The editor walk draws this itself.
	internal override bool DrawnByWalk => true;

	public override void OnDraw()
	{
		if ( texture == null )
			return;

		DrawBackgroundTexture( texture, Length.Cover );
	}
}
