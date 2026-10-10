using System.Globalization;

namespace Rinten.UI;

/// <summary>An SVG group or shape, positioned by view box and transforms rather than flex; styled with CSS</summary>
public abstract class SvgElement : Panel
{
	protected SvgElement()
	{
		//Out of the flow and of no size: where it draws is the view box's business.
		Style.Position = PositionMode.Absolute;
		Style.Left = 0;
		Style.Top = 0;
		Style.Width = 0;
		Style.Height = 0;
	}

	/// <summary>The element's own <c>transform</c> attribute.</summary>
	public VectorTransform LocalTransform { get; private set; } = VectorTransform.Identity;

	/// <summary>The <c>&lt;svg&gt;</c> this is drawn in, if it is in one.</summary>
	public SvgPanel Svg
	{
		get
		{
			for ( var p = Parent; p is not null; p = p.Parent )
			{
				if ( p is SvgPanel svg ) return svg;
				if ( p is not SvgElement ) return null;
			}

			return null;
		}
	}

	/// <summary>From this element's own coordinates to the view box's: its transform, then each group's.</summary>
	public VectorTransform ToViewBox
	{
		get
		{
			var t = LocalTransform;

			for ( var p = Parent; p is SvgElement e; p = p.Parent )
				t = t.Then( e.LocalTransform );

			return t;
		}
	}

	//The presentation attributes: what may be written as an attribute and means the CSS property of the same name.
	internal static bool IsPresentation( string name ) => name switch
	{
		"fill" or "fill-opacity" or "fill-rule" or "stroke" or "stroke-width" or "stroke-opacity"
			or "stroke-linecap" or "stroke-linejoin" or "stroke-miterlimit" or "stroke-dasharray"
			or "stroke-dashoffset" or "vector-effect" or "opacity" or "pointer-events" or "display"
			or "color" or "cursor" => true,
		_ => false,
	};

	public override void SetProperty( string name, string value )
	{
		switch ( name )
		{
			case "id" or "class" or "style" or "value":
				base.SetProperty( name, value );
				return;
		}

		if ( IsPresentation( name ) )
		{
			SetAttribute( name, value );
			Style.SetPresentation( name, value );
			return;
		}

		if ( name == "transform" )
		{
			SetAttribute( name, value );

			var parsed = VectorTransform.Parse( value );
			if ( parsed == LocalTransform ) return;

			LocalTransform = parsed;
			GeometryChanged( true );
			return;
		}

		if ( SetGeometry( name, value ) )
		{
			SetAttribute( name, value );
			GeometryChanged( false );
			return;
		}

		base.SetProperty( name, value );
	}

	/// <summary>An attribute bound to a value rather than written as text - <c>cx="@x"</c>. A number is written out invariantly: the current culture's decimal comma would read as two numbers.</summary>
	public override void SetPropertyObject( string name, object value )
	{
		if ( value is IFormattable number and not Enum )
		{
			SetProperty( name, number.ToString( null, CultureInfo.InvariantCulture ) );
			return;
		}

		base.SetPropertyObject( name, value );
	}

	//A geometry attribute - d , cx , points - taken if this element has it.
	protected virtual bool SetGeometry( string name, string value ) => false;

	//Something that moves what this draws; for a group, what everything in it draws.
	internal void GeometryChanged( bool descendants )
	{
		GeometryVersion++;
		IsRenderDirty = true;

		if ( !descendants ) return;

		foreach ( var child in Descendants )
		{
			if ( child is SvgElement e )
			{
				e.GeometryVersion++;
				e.IsRenderDirty = true;
			}
		}
	}

	//Moves whenever the element's own geometry does, so a tessellation of it knows it is stale.
	internal uint GeometryVersion;

	//A number attribute, taking any unit as a user unit.
	protected static float Number( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return 0;

		var at = 0;
		while ( at < value.Length && char.IsWhiteSpace( value[at] ) ) at++;

		return VectorPath.TryReadNumber( value, ref at, out var f ) ? f : 0;
	}
}

/// <summary><c>&lt;g&gt;</c>: a group, which carries a transform and styles for everything in it.</summary>
public sealed class SvgGroup : SvgElement
{
	//A group draws nothing of its own.
	internal override bool DrawnByWalk => true;

	internal override bool? InsideShape( Vector2 pos ) => false;
}

/// <summary>A vector shape in an <c>&lt;svg&gt;</c>: <c>&lt;path&gt;</c>, <c>&lt;line&gt;</c>, <c>&lt;polyline&gt;</c>, <c>&lt;polygon&gt;</c>, <c>&lt;circle&gt;</c>, <c>&lt;ellipse&gt;</c> or <c>&lt;rect&gt;</c>, filled and stroked by its style, tessellated on the processor and kept until something about it changes.</summary>
public sealed class SvgShape : SvgElement
{
	/// <summary>Which element this is.</summary>
	public enum ShapeKind { Path, Line, Polyline, Polygon, Circle, Ellipse, Rect }

	public ShapeKind Kind { get; }

	public SvgShape() : this( ShapeKind.Path )
	{
	}

	public SvgShape( ShapeKind kind )
	{
		Kind = kind;
		ElementName = kind.ToString().ToLowerInvariant();
	}

	//The element name to the shape, or nothing for a name that is not one.
	internal static SvgShape Create( string elementName ) => elementName switch
	{
		"path" => new SvgShape( ShapeKind.Path ),
		"line" => new SvgShape( ShapeKind.Line ),
		"polyline" => new SvgShape( ShapeKind.Polyline ),
		"polygon" => new SvgShape( ShapeKind.Polygon ),
		"circle" => new SvgShape( ShapeKind.Circle ),
		"ellipse" => new SvgShape( ShapeKind.Ellipse ),
		"rect" => new SvgShape( ShapeKind.Rect ),
		_ => null,
	};

	/// <summary>The path data of a <c>&lt;path&gt;</c>.</summary>
	public string D { get => d; set { if ( d == value ) return; d = value; GeometryChanged( false ); } }
	private string d;

	/// <summary>The points of a <c>&lt;polyline&gt;</c> or <c>&lt;polygon&gt;</c>.</summary>
	public string Points { get => points; set { if ( points == value ) return; points = value; GeometryChanged( false ); } }
	private string points;

	private float x, y, width, height, rx = float.NaN, ry = float.NaN, cx, cy, r, x1, y1, x2, y2;

	protected override bool SetGeometry( string name, string value )
	{
		switch ( name )
		{
			case "d" when Kind == ShapeKind.Path: d = value; return true;
			case "points" when Kind is ShapeKind.Polyline or ShapeKind.Polygon: points = value; return true;
			case "x": x = Number( value ); return true;
			case "y": y = Number( value ); return true;
			case "width": width = Number( value ); return true;
			case "height": height = Number( value ); return true;
			case "rx": rx = string.IsNullOrWhiteSpace( value ) || value.Trim() == "auto" ? float.NaN : Number( value ); return true;
			case "ry": ry = string.IsNullOrWhiteSpace( value ) || value.Trim() == "auto" ? float.NaN : Number( value ); return true;
			case "cx": cx = Number( value ); return true;
			case "cy": cy = Number( value ); return true;
			case "r": r = Number( value ); return true;
			case "x1": x1 = Number( value ); return true;
			case "y1": y1 = Number( value ); return true;
			case "x2": x2 = Number( value ); return true;
			case "y2": y2 = Number( value ); return true;
		}

		return false;
	}

	//A shape draws itself through the walk; nothing is left for OnDraw.
	internal override bool DrawnByWalk => true;

	private VectorPath path;
	private uint pathVersion = uint.MaxValue;

	/// <summary>The shape as a path, in its own coordinates.</summary>
	public VectorPath Path
	{
		get
		{
			if ( path is not null && pathVersion == GeometryVersion ) return path;

			pathVersion = GeometryVersion;
			path = Build();
			return path;
		}
	}

	private VectorPath Build()
	{
		switch ( Kind )
		{
			case ShapeKind.Path:
				return VectorPath.Parse( d );

			case ShapeKind.Line:
				return new VectorPath().MoveTo( new( x1, y1 ) ).LineTo( new( x2, y2 ) );

			case ShapeKind.Polyline:
			case ShapeKind.Polygon:
			{
				var list = VectorPath.ParsePoints( points );
				return VectorPath.Polyline( System.Runtime.InteropServices.CollectionsMarshal.AsSpan( list ), Kind == ShapeKind.Polygon );
			}

			case ShapeKind.Circle:
				return VectorPath.Ellipse( new( cx, cy ), r, r );

			case ShapeKind.Ellipse:
			{
				//One radius given and the other auto is a circle, as for a rect.
				var a = float.IsNaN( rx ) ? ry : rx;
				var b = float.IsNaN( ry ) ? rx : ry;
				return VectorPath.Ellipse( new( cx, cy ), float.IsNaN( a ) ? 0 : a, float.IsNaN( b ) ? 0 : b );
			}

			case ShapeKind.Rect:
			{
				var a = float.IsNaN( rx ) ? ry : rx;
				var b = float.IsNaN( ry ) ? rx : ry;
				return VectorPath.Rectangle( x, y, width, height, float.IsNaN( a ) ? 0 : a, float.IsNaN( b ) ? 0 : b );
			}
		}

		return new VectorPath();
	}

	//-- what it is drawn as ----------------------------------------------------------------

	//One tessellation of the shape: its triangles in the pixels of the svg's content box,
	//from the box's corner, and the flattened outline its hit tests are made against.
	internal sealed class Tessellation
	{
		public uint Version;
		public int StyleKey;
		public VectorTransform Mapping;
		public float Fringe;

		public readonly List<VectorVertex> Fill = new();
		public readonly List<VectorVertex> Stroke = new();
		public readonly List<VectorContour> Contours = new();

		/// <summary>What a stroke thinner than a pixel is faded by.</summary>
		public float StrokeAlpha = 1;

		/// <summary>Half the stroke's width in pixels, for hit testing.</summary>
		public float StrokeHalf;

		public FillRule Rule;
		public bool Filled;
		public bool Stroked;

		/// <summary>Everything the triangles cover.</summary>
		public Rect Bounds;
	}

	private Tessellation tessellation;

	//How many times any shape was tessellated, for the walk's counts.
	internal static int Tessellated;

	//What of the style moves the triangles, as against their colour - which changes nothing about them.
	private static int GeometryKey( Styles style, bool filled, bool stroked )
	{
		var hash = new HashCode();
		hash.Add( filled );
		hash.Add( stroked );
		hash.Add( style.FillRule );

		if ( stroked )
		{
			hash.Add( style.StrokeWidth );
			hash.Add( style.StrokeLinecap );
			hash.Add( style.StrokeLinejoin );
			hash.Add( style.StrokeMiterlimit );
			hash.Add( style.StrokeDasharray );
			hash.Add( style.StrokeDashoffset );
			hash.Add( style.VectorEffect );
		}

		return hash.ToHashCode();
	}

	//The shape's triangles for this frame: made again only when its geometry, its view
	//box mapping, what of its style shapes them, or the size of a screen pixel moved.
	internal Tessellation Tessellate( float fringe )
	{
		var style = ComputedStyle;
		var svg = Svg;
		if ( style is null || svg is null ) return null;

		//A line has no inside to fill; everything else does, open or not.
		var filled = Kind != ShapeKind.Line && ( style.Fill ?? Color.Black ).a > 0.0f && ( style.FillOpacity ?? 1 ) > 0.0f;
		var stroked = ( style.Stroke ?? Color.Transparent ).a > 0.0f && ( style.StrokeOpacity ?? 1 ) > 0.0f;

		var mapping = ToViewBox.Then( svg.ViewBoxMapping );

		//A pixel's worth of fringe, held to steps of a quarter octave so a zoom does not tessellate every frame.
		fringe = MathF.Pow( 2.0f, MathF.Round( MathF.Log2( Math.Clamp( fringe, 1.0f / 64.0f, 64.0f ) ) * 4.0f ) / 4.0f );

		var key = GeometryKey( style, filled, stroked );
		var t = tessellation;

		if ( t is not null && t.Version == GeometryVersion && t.StyleKey == key && t.Mapping == mapping && t.Fringe == fringe )
			return t;

		t ??= tessellation = new Tessellation();
		t.Version = GeometryVersion;
		t.StyleKey = key;
		t.Mapping = mapping;
		t.Fringe = fringe;
		t.Fill.Clear();
		t.Stroke.Clear();
		t.Contours.Clear();
		t.Filled = filled;
		t.Stroked = stroked;
		t.Rule = style.FillRule ?? FillRule.NonZero;
		t.StrokeAlpha = 1;
		t.StrokeHalf = 0;

		Tessellated++;

		var shape = Path;
		if ( shape.IsEmpty ) { t.Bounds = default; return t; }

		//A quarter of a screen pixel, in the pixels the outline is made in.
		VectorTessellator.Flatten( shape, mapping, 0.25f * fringe, t.Contours );

		if ( filled ) VectorTessellator.Fill( t.Contours, t.Rule, fringe, t.Fill );

		if ( stroked )
		{
			//The width is in the shape's units and scales with it, unless it is asked not to.
			var nonScaling = style.VectorEffect == UI.VectorEffect.NonScalingStroke;
			var scale = nonScaling ? 1.0f : mapping.Scale;
			var diagonal = svg.ViewBoxDiagonal;
			var width = ( style.StrokeWidth ?? Length.Pixels( 1 ).Value ).GetPixels( diagonal ) * scale;

			var stroke = new VectorStrokeStyle
			{
				Width = width,
				Cap = style.StrokeLinecap ?? StrokeLinecap.Butt,
				Join = style.StrokeLinejoin ?? StrokeLinejoin.Miter,
				MiterLimit = style.StrokeMiterlimit ?? 4,
				Dashes = Dashes( style.StrokeDasharray, scale ),
				DashOffset = ( style.StrokeDashoffset ?? 0 ).GetPixels( diagonal ) * scale,
			};

			t.StrokeHalf = width * 0.5f;
			t.StrokeAlpha = VectorTessellator.Stroke( t.Contours, stroke, fringe, t.Stroke );
		}

		t.Bounds = BoundsOf( t.Fill, BoundsOf( t.Stroke, null ) ) ?? default;
		return t;
	}

	private static float[] Dashes( string text, float scale )
	{
		if ( string.IsNullOrWhiteSpace( text ) || text.Trim() == "none" ) return null;

		var numbers = VectorPath.ParseNumbers( text );
		if ( numbers.Count == 0 ) return null;

		var dashes = new float[numbers.Count];
		for ( var i = 0; i < dashes.Length; i++ ) dashes[i] = numbers[i] * scale;

		return dashes;
	}

	private static Rect? BoundsOf( List<VectorVertex> vertices, Rect? into )
	{
		if ( vertices.Count == 0 ) return into;

		var min = vertices[0].Position;
		var max = min;

		foreach ( var v in vertices )
		{
			min = new Vector2( MathF.Min( min.x, v.Position.x ), MathF.Min( min.y, v.Position.y ) );
			max = new Vector2( MathF.Max( max.x, v.Position.x ), MathF.Max( max.y, v.Position.y ) );
		}

		var rect = new Rect( min, max - min );
		if ( into is { } other ) rect.Add( other );

		return rect;
	}

	//Hit where it is painted: inside its fill, if it has one, or within its stroke. A shape
	//with neither painted is hit inside its outline, which is what pointer-events: all asks.
	internal override bool? InsideShape( Vector2 pos )
	{
		var svg = Svg;
		if ( svg is null ) return false;

		var t = Tessellate( tessellation?.Fringe ?? 1.0f );
		if ( t is null ) return false;

		var local = pos - svg.ContentOrigin;

		if ( t.Filled || !t.Stroked )
		{
			if ( VectorTessellator.InsideFill( t.Contours, t.Rule, local ) ) return true;
		}

		return t.Stroked && VectorTessellator.NearStroke( t.Contours, t.StrokeHalf, local );
	}
}
