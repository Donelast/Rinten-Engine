using System.Globalization;
using L = Rinten.Layout;

namespace Rinten.UI;

//Bridge from panel style to its layout node; skips unchanged values, but dynamic units (em, vw, calc)
//record their dependencies so ReferenceSizeChanged can trigger relayout
[SkipHotload]
internal sealed class PanelLayout
{
	//What a control that sizes itself is asked, and what it answers. A label and an image install one.
	internal delegate Vector2 MeasureDelegate( float width, L.MeasureMode widthMode, float height, L.MeasureMode heightMode );

	//What a control answers when asked where its baseline is, at the size it was measured at.
	internal delegate float BaselineDelegate( float width, float height );

	//What the panels build their nodes with: web defaults, and no pixel snapping, the interface does its own.
	private static readonly L.LayoutConfig Config = new() { UseWebDefaults = true, PointScaleFactor = 0 };

	//One delegate for every node: the node's context is the seam, and the seam holds the control's delegate.
	private static readonly L.MeasureFunc SharedMeasure = static ( node, width, widthMode, height, heightMode ) =>
	{
		//Under -uitrace a zone of its own: what a layout pass spends asking text how big it is, apart from what it spends placing boxes.
		using var zone = Panel.TracingTick ? Rinten.Diagnostics.Performance.Scope( "layout measure" ) : default;

		var size = ((PanelLayout)node.Context).measureFunc( width, widthMode, height, heightMode );
		return new L.LayoutSize( size.x, size.y );
	};

	private static readonly L.BaselineFunc SharedBaseline = static ( node, width, height ) =>
		((PanelLayout)node.Context).baselineFunc( width, height );

	private L.LayoutNode node;
	private Panel panel;
	private MeasureDelegate measureFunc;
	private BaselineDelegate baselineFunc;

	internal L.LayoutNode Node => node ?? throw new Exception( "Tried to access destroyed node" );

	private PanelLayout Parent => panel?.Parent?.LayoutTree;

	//Parent's laid-out size, the reference for relative units and for percentages in calc().
	private float ParentWidth => Parent?.LayoutWidth ?? 0;
	private float ParentHeight => Parent?.LayoutHeight ?? 0;
	private L.FlexDirection ParentDirection => node?.Owner?.Style.FlexDirection ?? L.FlexDirection.Row;

	public PanelLayout( Panel panel )
	{
		node = new L.LayoutNode( Config ) { Context = this };
		this.panel = panel;
	}

	public void Dispose()
	{
		if ( node is null ) return;

		node.Owner?.RemoveChild( node );
		node.RemoveAllChildren();
		node.Context = null;
		node = null;
		panel = null;
		measureFunc = null;
		baselineFunc = null;
	}

	//-- what the layout wrote ----------------------------------------------------------

	private Rect rect;
	internal Margin Margin;
	internal Margin Padding;
	internal Margin Border;

	public bool HasNewLayout => Node.HasNewLayout;
	public bool IsDirty => Node.IsDirty;

	internal float LayoutX => Node.Layout.Position( L.PhysicalEdge.Left );
	internal float LayoutY => Node.Layout.Position( L.PhysicalEdge.Top );
	internal float LayoutWidth => Node.Layout.GetDimension( L.Dimension.Width );
	internal float LayoutHeight => Node.Layout.GetDimension( L.Dimension.Height );

	internal Margin LayoutMargin => Edges( Node.Layout.Margin( L.PhysicalEdge.Left ), Node.Layout.Margin( L.PhysicalEdge.Top ), Node.Layout.Margin( L.PhysicalEdge.Right ), Node.Layout.Margin( L.PhysicalEdge.Bottom ) );
	internal Margin LayoutPadding => Edges( Node.Layout.Padding( L.PhysicalEdge.Left ), Node.Layout.Padding( L.PhysicalEdge.Top ), Node.Layout.Padding( L.PhysicalEdge.Right ), Node.Layout.Padding( L.PhysicalEdge.Bottom ) );
	internal Margin LayoutBorder => Edges( Node.Layout.Border( L.PhysicalEdge.Left ), Node.Layout.Border( L.PhysicalEdge.Top ), Node.Layout.Border( L.PhysicalEdge.Right ), Node.Layout.Border( L.PhysicalEdge.Bottom ) );

	private static Margin Edges( float left, float top, float right, float bottom ) => new( left, top, right, bottom );

	/// <summary>The rect of the last layout. Reading it acknowledges the layout as seen.</summary>
	public Rect LayoutRect
	{
		get
		{
			if ( !HasNewLayout )
				return rect;

			Node.HasNewLayout = false;

			rect = new Rect( LayoutX, LayoutY, LayoutWidth, LayoutHeight );

			Margin = LayoutMargin;
			Padding = LayoutPadding;
			Border = LayoutBorder;

			return rect;
		}
	}

	//-- measuring ----------------------------------------------------------------------

	internal bool IsMeasureDefined => measureFunc is not null;

	internal void SetMeasureFunction( MeasureDelegate target )
	{
		measureFunc = target;
		Node.SetMeasureFunc( target is null ? null : SharedMeasure );
	}

	//A control that knows where its baseline is says so here; a text control's is its first line's.
	internal void SetBaselineFunction( BaselineDelegate target )
	{
		baselineFunc = target;
		Node.SetBaselineFunc( target is null ? null : SharedBaseline );
	}

	//-- the tree -----------------------------------------------------------------------

	internal void RemoveChild( PanelLayout child ) => Node.RemoveChild( child.Node );

	internal void AddChild( PanelLayout child ) => Node.InsertChild( child.Node, Node.ChildCount );

	internal void AddChild( int index, PanelLayout child ) => Node.InsertChild( child.Node, Math.Clamp( index, 0, Node.ChildCount ) );

	internal void CalculateLayout( float width = float.NaN, float height = float.NaN )
	{
		L.FlexLayout.Calculate( Node, width, height, L.Direction.LTR );
	}

	//A control whose measured content changed says so here; a node without a measure function has nothing to say.
	internal void MarkDirty()
	{
		if ( !IsMeasureDefined ) return;

		Node.MarkDirty();
	}

	//-- what the style depends on -------------------------------------------------------

	//The parts of the world a dynamic unit reads. Recorded during a push, compared before the next.
	[Flags]
	private enum Dependencies
	{
		None = 0,
		Width = 1,
		Height = 2,
		Font = 4,
		RootFont = 8,
		ViewWidth = 16,
		ViewHeight = 32,
		Direction = 64,
	}

	private readonly record struct Context( float Width, float Height, float Font, float RootFont, Vector2 Viewport, L.FlexDirection Direction )
	{
		internal bool Changed( Context other, Dependencies dependencies )
			=> ((dependencies & Dependencies.Width) != 0 && Width != other.Width)
				|| ((dependencies & Dependencies.Height) != 0 && Height != other.Height)
				|| ((dependencies & Dependencies.Font) != 0 && Font != other.Font)
				|| ((dependencies & Dependencies.RootFont) != 0 && RootFont != other.RootFont)
				|| ((dependencies & Dependencies.ViewWidth) != 0 && Viewport.x != other.Viewport.x)
				|| ((dependencies & Dependencies.ViewHeight) != 0 && Viewport.y != other.Viewport.y)
				|| ((dependencies & Dependencies.Direction) != 0 && Direction != other.Direction);
	}

	private Dependencies dependencies;
	private Context context;

	private Context CurrentContext => new( ParentWidth, ParentHeight,
		panel?.ComputedStyle?.FontSize?.Value ?? Length.CurrentFontSize.Value,
		Length.RootFontSize.Value, Length.RootSize, ParentDirection );

	//True when something a dynamic unit was resolved against has moved since the last push.
	internal bool ReferenceSizeChanged => dependencies != Dependencies.None && context.Changed( CurrentContext, dependencies );

	//True when a length was resolved against the parent's laid-out size (a percentage inside a calc(), a percentage border).
	//The parent tells such children to cascade again when its own size changes, since nothing else would reach them.
	internal bool DependsOnParentSize => (dependencies & (Dependencies.Width | Dependencies.Height)) != 0;

	//How many times the panel pushed its style into the node. For the tests that say what must not repush.
	internal int PushCount { get; private set; }

	//Forgets the dependencies of the previous push; the setters that follow record the new ones.
	internal void BeginPush()
	{
		dependencies = Dependencies.None;
		PushCount++;
	}

	//Remembers the context the push resolved against.
	internal void EndPush()
	{
		context = CurrentContext;
		Initialized = true;
	}

	internal bool Initialized;

	private enum Axis { Width, Height }

	//Parent's laid-out size along an axis - the reference for resolving relative units.
	private float Reference( Axis axis ) => axis == Axis.Width ? ParentWidth : ParentHeight;

	private static Dependencies DependenciesOf( Length length, Axis axis ) => length.Unit switch
	{
		LengthUnit.Em => Dependencies.Font,
		LengthUnit.RootEm => Dependencies.RootFont,
		LengthUnit.ViewWidth => Dependencies.ViewWidth,
		LengthUnit.ViewHeight => Dependencies.ViewHeight,
		LengthUnit.ViewMin or LengthUnit.ViewMax => Dependencies.ViewWidth | Dependencies.ViewHeight,
		LengthUnit.Expression => ExpressionDependencies( length.ToString(), axis ),
		_ => Dependencies.None,
	};

	//A calc() can mix anything; the units in its text say what it reads.
	private static Dependencies ExpressionDependencies( string expression, Axis axis )
	{
		var result = Dependencies.None;
		var text = expression.AsSpan();
		for ( var i = 0; i < text.Length; i++ )
		{
			if ( text[i] == '%' ) result |= axis == Axis.Width ? Dependencies.Width : Dependencies.Height;
			if ( !char.IsLetter( text[i] ) ) continue;

			var start = i;
			while ( i + 1 < text.Length && char.IsLetter( text[i + 1] ) ) i++;
			var unit = text.Slice( start, i - start + 1 );

			if ( unit.Equals( "em", StringComparison.OrdinalIgnoreCase ) ) result |= Dependencies.Font;
			else if ( unit.Equals( "rem", StringComparison.OrdinalIgnoreCase ) ) result |= Dependencies.RootFont;
			else if ( unit.Equals( "vw", StringComparison.OrdinalIgnoreCase ) ) result |= Dependencies.ViewWidth;
			else if ( unit.Equals( "vh", StringComparison.OrdinalIgnoreCase ) ) result |= Dependencies.ViewHeight;
			else if ( unit.Equals( "vmin", StringComparison.OrdinalIgnoreCase ) || unit.Equals( "vmax", StringComparison.OrdinalIgnoreCase ) ) result |= Dependencies.ViewWidth | Dependencies.ViewHeight;
		}

		return result;
	}

	//A Length as the layout wants it: points, a percentage, auto, or nothing. The parent dimension is only read for relative
	//units; percentages go through and resolve inside the layout, against whatever the algorithm says they are a percentage of.
	private L.StyleLength Resolve( Length? value, Axis axis )
	{
		if ( !value.HasValue ) return L.StyleLength.Undefined;

		var length = value.Value;
		dependencies |= DependenciesOf( length, axis );

		switch ( length.Unit )
		{
			case LengthUnit.Undefined: return L.StyleLength.Undefined;
			case LengthUnit.Auto: return L.StyleLength.Auto;
			case LengthUnit.Percentage: return L.StyleLength.Percent( length.Value );
			case LengthUnit.Pixels: return L.StyleLength.Points( length.Value );
			case LengthUnit.ViewWidth:
			case LengthUnit.ViewHeight:
			case LengthUnit.ViewMin:
			case LengthUnit.ViewMax: return L.StyleLength.Points( length.GetPixels( 0 ) );
			default: return L.StyleLength.Points( length.GetPixels( Reference( axis ) ) ); //em, rem, calc
		}
	}

	private static L.SizeLength AsSize( L.StyleLength value )
	{
		if ( value.IsAuto ) return L.SizeLength.Auto;
		if ( value.IsPercent ) return L.SizeLength.Percent( value.Value );
		if ( value.IsPoints ) return L.SizeLength.Points( value.Value );
		return L.SizeLength.Undefined;
	}

	//Like Resolve for the properties with no auto : padding, border, insets.
	private L.StyleLength ResolveNoAuto( Length? value, Axis axis )
	{
		var resolved = Resolve( value, axis );
		return resolved.IsAuto ? L.StyleLength.Undefined : resolved;
	}

	//Whether a remembered Length can be trusted to mean what it meant last time.
	//Asked by every setter of every push; inlined, for the browser's interpreter.
	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining )]
	private bool Unchanged( Length? previous, Length? value )
		=> Initialized && previous == value && (!value.HasValue || !value.GetValueOrDefault().Unit.IsDynamic());

	private L.LayoutStyle Style => Node.Style;

	//-- the properties, as the panel names them ---------------------------------------------

	private Length? width;
	public Length? Width
	{
		set
		{
			if ( Unchanged( width, value ) ) return;
			width = value;
			Style.SetDimension( L.Dimension.Width, AsSize( Resolve( value, Axis.Width ) ) );
		}
	}

	private Length? height;
	public Length? Height
	{
		set
		{
			if ( Unchanged( height, value ) ) return;
			height = value;
			Style.SetDimension( L.Dimension.Height, AsSize( Resolve( value, Axis.Height ) ) );
		}
	}

	private Length? maxWidth;
	public Length? MaxWidth
	{
		set
		{
			if ( Unchanged( maxWidth, value ) ) return;
			maxWidth = value;
			Style.SetMaxDimension( L.Dimension.Width, AsSize( ResolveNoAuto( value, Axis.Width ) ) );
		}
	}

	private Length? maxHeight;
	public Length? MaxHeight
	{
		set
		{
			if ( Unchanged( maxHeight, value ) ) return;
			maxHeight = value;
			Style.SetMaxDimension( L.Dimension.Height, AsSize( ResolveNoAuto( value, Axis.Height ) ) );
		}
	}

	private Length? minWidth;
	public Length? MinWidth
	{
		set
		{
			if ( Unchanged( minWidth, value ) ) return;
			minWidth = value;
			Style.SetMinDimension( L.Dimension.Width, AsSize( ResolveNoAuto( value, Axis.Width ) ) );
		}
	}

	private Length? minHeight;
	public Length? MinHeight
	{
		set
		{
			if ( Unchanged( minHeight, value ) ) return;
			minHeight = value;
			Style.SetMinDimension( L.Dimension.Height, AsSize( ResolveNoAuto( value, Axis.Height ) ) );
		}
	}

	public DisplayMode? Display
	{
		set => Style.Display = (L.Display)(int)(value ?? DisplayMode.Flex);
	}

	private Length? left, right, top, bottom;
	public Length? Left { set { if ( Unchanged( left, value ) ) return; left = value; Style.SetPosition( L.Edge.Left, ResolveNoAuto( value, Axis.Width ) ); } }
	public Length? Right { set { if ( Unchanged( right, value ) ) return; right = value; Style.SetPosition( L.Edge.Right, ResolveNoAuto( value, Axis.Width ) ); } }
	public Length? Top { set { if ( Unchanged( top, value ) ) return; top = value; Style.SetPosition( L.Edge.Top, ResolveNoAuto( value, Axis.Height ) ); } }
	public Length? Bottom { set { if ( Unchanged( bottom, value ) ) return; bottom = value; Style.SetPosition( L.Edge.Bottom, ResolveNoAuto( value, Axis.Height ) ); } }

	private Length? marginLeft, marginRight, marginTop, marginBottom;
	public Length? MarginLeft { set { if ( Unchanged( marginLeft, value ) ) return; marginLeft = value; Style.SetMargin( L.Edge.Left, Resolve( value, Axis.Width ) ); } }
	public Length? MarginRight { set { if ( Unchanged( marginRight, value ) ) return; marginRight = value; Style.SetMargin( L.Edge.Right, Resolve( value, Axis.Width ) ); } }
	public Length? MarginTop { set { if ( Unchanged( marginTop, value ) ) return; marginTop = value; Style.SetMargin( L.Edge.Top, Resolve( value, Axis.Height ) ); } }
	public Length? MarginBottom { set { if ( Unchanged( marginBottom, value ) ) return; marginBottom = value; Style.SetMargin( L.Edge.Bottom, Resolve( value, Axis.Height ) ); } }

	private Length? paddingLeft, paddingRight, paddingTop, paddingBottom;
	public Length? PaddingLeft { set { if ( Unchanged( paddingLeft, value ) ) return; paddingLeft = value; Style.SetPadding( L.Edge.Left, ResolveNoAuto( value, Axis.Width ) ); } }
	public Length? PaddingRight { set { if ( Unchanged( paddingRight, value ) ) return; paddingRight = value; Style.SetPadding( L.Edge.Right, ResolveNoAuto( value, Axis.Width ) ); } }
	public Length? PaddingTop { set { if ( Unchanged( paddingTop, value ) ) return; paddingTop = value; Style.SetPadding( L.Edge.Top, ResolveNoAuto( value, Axis.Height ) ); } }
	public Length? PaddingBottom { set { if ( Unchanged( paddingBottom, value ) ) return; paddingBottom = value; Style.SetPadding( L.Edge.Bottom, ResolveNoAuto( value, Axis.Height ) ); } }

	//A border is always points: a percentage border resolves against the parent up front.
	private L.StyleLength ResolveBorder( Length? value, Axis axis )
	{
		var resolved = ResolveNoAuto( value, axis );
		if ( !resolved.IsPercent ) return resolved;

		dependencies |= axis == Axis.Width ? Dependencies.Width : Dependencies.Height;
		return L.StyleLength.Points( value.Value.GetPixels( Reference( axis ) ) );
	}

	private Length? borderLeft, borderRight, borderTop, borderBottom;
	public Length? BorderLeftWidth { set { if ( Unchanged( borderLeft, value ) ) return; borderLeft = value; Style.SetBorder( L.Edge.Left, WithGutter( ResolveBorder( value, Axis.Width ), gutter.Left ) ); } }
	public Length? BorderRightWidth { set { if ( Unchanged( borderRight, value ) ) return; borderRight = value; Style.SetBorder( L.Edge.Right, WithGutter( ResolveBorder( value, Axis.Width ), gutter.Right ) ); } }
	public Length? BorderTopWidth { set { if ( Unchanged( borderTop, value ) ) return; borderTop = value; Style.SetBorder( L.Edge.Top, ResolveBorder( value, Axis.Height ) ); } }
	public Length? BorderBottomWidth { set { if ( Unchanged( borderBottom, value ) ) return; borderBottom = value; Style.SetBorder( L.Edge.Bottom, ResolveBorder( value, Axis.Height ) ); } }

	private Margin gutter;

	/// <summary>Extra left and right space between the padding box and the border, for a scrollbar gutter. Added to the layout's border, which is always in points. <see cref="Border"/> includes it.</summary>
	public Margin Gutter
	{
		get => gutter;
		set
		{
			if ( gutter.Left == value.Left && gutter.Right == value.Right ) return;
			gutter = value;

			Style.SetBorder( L.Edge.Left, WithGutter( ResolveBorder( borderLeft, Axis.Width ), gutter.Left ) );
			Style.SetBorder( L.Edge.Right, WithGutter( ResolveBorder( borderRight, Axis.Width ), gutter.Right ) );
		}
	}

	private static L.StyleLength WithGutter( L.StyleLength border, float extra )
	{
		if ( extra == 0 ) return border;
		if ( !border.IsDefined ) return L.StyleLength.Points( extra );

		return L.StyleLength.Points( border.Value + extra );
	}

	public PositionMode? PositionType { set => Style.PositionType = (L.PositionType)(int)(value ?? PositionMode.Static); }
	public BoxSizingMode? BoxSizing { set => Style.BoxSizing = (L.BoxSizing)(int)(value ?? BoxSizingMode.BorderBox); }

	//Grid: the track lists and lines are parsed by the layout engine, once per distinct string.
	public string GridTemplateColumns { set => Style.GridTemplateColumns = L.GridTrackList.Parse( value ); }
	public string GridTemplateRows { set => Style.GridTemplateRows = L.GridTrackList.Parse( value ); }
	public string GridAutoColumns { set => Style.GridAutoColumns = L.GridTrackList.Parse( value ); }
	public string GridAutoRows { set => Style.GridAutoRows = L.GridTrackList.Parse( value ); }
	public string GridColumnStart { set => Style.GridColumnStart = L.GridLine.Parse( value ); }
	public string GridColumnEnd { set => Style.GridColumnEnd = L.GridLine.Parse( value ); }
	public string GridRowStart { set => Style.GridRowStart = L.GridLine.Parse( value ); }
	public string GridRowEnd { set => Style.GridRowEnd = L.GridLine.Parse( value ); }
	public Align? JustifyItems { set => Style.JustifyItems = (L.Align)(int)(value ?? UI.Align.Stretch); }
	public Align? JustifySelf { set => Style.JustifySelf = (L.Align)(int)(value ?? UI.Align.Auto); }

	public string GridAutoFlow
	{
		set => Style.GridAutoFlow = ( value ?? "row" ).Trim() switch
		{
			"column" => L.GridAutoFlow.Column,
			"row dense" or "dense" => L.GridAutoFlow.RowDense,
			"column dense" or "dense column" => L.GridAutoFlow.ColumnDense,
			_ => L.GridAutoFlow.Row,
		};
	}
	public float? AspectRatio { set => Style.AspectRatio = value ?? float.NaN; }
	public float? FlexGrow { set => Style.FlexGrow = value ?? 0; }
	public float? FlexShrink { set => Style.FlexShrink = value ?? 1; }

	private Length? flexBasis;
	public Length? FlexBasis
	{
		set
		{
			//A flex item's basis follows its container's main axis, not its own direction.
			var axis = ParentDirection is L.FlexDirection.Column or L.FlexDirection.ColumnReverse ? Axis.Height : Axis.Width;
			if ( Unchanged( flexBasis, value ) && (value?.Unit != LengthUnit.Expression) ) return;
			flexBasis = value;
			if ( value?.Unit == LengthUnit.Expression ) dependencies |= Dependencies.Direction;
			Style.FlexBasis = AsSize( Resolve( value, axis ) );
		}
	}

	public Wrap? Wrap { set => Style.FlexWrap = (L.Wrap)(int)(value ?? UI.Wrap.NoWrap); }
	public Align? AlignContent { set => Style.AlignContent = (L.Align)(int)(value ?? UI.Align.FlexStart); }
	public Align? AlignItems { set => Style.AlignItems = (L.Align)(int)(value ?? UI.Align.Stretch); }
	public Align? AlignSelf { set => Style.AlignSelf = (L.Align)(int)(value ?? UI.Align.Auto); }
	public FlexDirection? FlexDirection { set => Style.FlexDirection = (L.FlexDirection)(int)(value ?? UI.FlexDirection.Row); }
	public Justify? JustifyContent { set => Style.JustifyContent = (L.Justify)(int)(value ?? UI.Justify.FlexStart); }

	public OverflowMode? Overflow
	{
		set
		{
			//Clip and ClipWhole behave like Visible for layout purposes: they only affect rendering.
			Style.Overflow = value switch
			{
				OverflowMode.Scroll => L.Overflow.Scroll,
				OverflowMode.Hidden => L.Overflow.Hidden,
				_ => L.Overflow.Visible,
			};
		}
	}

	private Length? rowGap, columnGap;
	public Length? RowGap { set { if ( Unchanged( rowGap, value ) ) return; rowGap = value; Style.SetGap( L.Gutter.Row, ResolveGap( value ) ); } }
	public Length? ColumnGap { set { if ( Unchanged( columnGap, value ) ) return; columnGap = value; Style.SetGap( L.Gutter.Column, ResolveGap( value ) ); } }

	private L.StyleLength ResolveGap( Length? value )
	{
		if ( !value.HasValue || value.Value.Unit == LengthUnit.Auto || value.Value.Unit == LengthUnit.Undefined ) return L.StyleLength.Undefined;
		if ( value.Value.Unit == LengthUnit.Percentage ) return L.StyleLength.Percent( value.Value.Value );

		dependencies |= DependenciesOf( value.Value, Axis.Width ) & ~(Dependencies.Width | Dependencies.Height);
		return L.StyleLength.Points( value.Value.GetPixels( 0 ) );
	}
}
