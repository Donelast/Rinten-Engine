using System;
using Rinten.Layout;
using R = Rinten.Layout.Reference;

namespace LayoutTests.Harness;

//IFixtureNode over the frozen reference engine, a copy of the production engine taken the last time its results were
//changed on purpose. The enums and length types are copies with the same numbering, so every conversion is a cast.
internal sealed class ReferenceNode : IFixtureNode
{
	internal sealed class Factory : IFixtureEngine
	{
		public string Name => "reference";
		public IFixtureNode CreateNode() => new ReferenceNode( FixtureConfig );

		public IFixtureNode CreateUiNode()
		{
			var node = new ReferenceNode( UiConfig );
			node.Node.Style.PositionType = R.PositionType.Static;
			return node;
		}
	}

	private static readonly R.LayoutConfig FixtureConfig = new() { UseWebDefaults = false, PointScaleFactor = 0 };
	private static readonly R.LayoutConfig UiConfig = new() { UseWebDefaults = true, PointScaleFactor = 0 };

	internal R.LayoutNode Node { get; }
	private readonly StyleView _style;
	private FixtureMeasureFunc _measure;

	private ReferenceNode( R.LayoutConfig config )
	{
		Node = new R.LayoutNode( config ) { Context = this };
		_style = new StyleView( Node );
	}

	public IFixtureEngine Engine => FixtureEngines.Reference;
	IFixtureStyle IFixtureNode.Style => _style;

	public object Context { get; set; }

	public FixtureMeasureFunc MeasureFunc
	{
		get => _measure;
		set
		{
			_measure = value;
			if ( value is null )
			{
				Node.SetMeasureFunc( null );
				return;
			}

			Node.SetMeasureFunc( ( node, width, widthMode, height, heightMode ) =>
			{
				FixtureCounters.CountMeasure();
				var size = _measure( (ReferenceNode)node.Context, width, (MeasureMode)(int)widthMode, height, (MeasureMode)(int)heightMode );
				return new R.LayoutSize( size.Width, size.Height );
			} );
		}
	}

	public bool HasMeasureFunc => Node.HasMeasureFunc;

	public IFixtureNode Owner => Node.Owner?.Context as ReferenceNode;
	public int ChildCount => Node.ChildCount;
	public IFixtureNode GetChild( int index ) => (ReferenceNode)Node.ChildAt( index ).Context;
	public void AddChild( IFixtureNode child ) => Node.InsertChild( ((ReferenceNode)child).Node, Node.ChildCount );
	public void InsertChild( IFixtureNode child, int index ) => Node.InsertChild( ((ReferenceNode)child).Node, index );
	public bool RemoveChild( IFixtureNode child ) => Node.RemoveChild( ((ReferenceNode)child).Node );
	public void RemoveAllChildren() => Node.RemoveAllChildren();

	public void CalculateLayout( float ownerWidth = float.NaN, float ownerHeight = float.NaN, Direction ownerDirection = Direction.LTR )
		=> R.FlexLayout.Calculate( Node, ownerWidth, ownerHeight, (R.Direction)(int)ownerDirection );

	public void MarkDirty() => Node.MarkDirtyAndPropagate();

	public bool IsDirty => Node.IsDirty;

	public bool HasNewLayout
	{
		get => Node.HasNewLayout;
		set => Node.HasNewLayout = value;
	}

	public float LayoutLeft => Node.Layout.Position( R.PhysicalEdge.Left );
	public float LayoutTop => Node.Layout.Position( R.PhysicalEdge.Top );
	public float LayoutWidth => Node.Layout.GetDimension( R.Dimension.Width );
	public float LayoutHeight => Node.Layout.GetDimension( R.Dimension.Height );
	public bool LayoutHadOverflow => Node.Layout.HadOverflow;
	public float LayoutMargin( PhysicalEdge edge ) => Node.Layout.Margin( (R.PhysicalEdge)(int)edge );
	public float LayoutPadding( PhysicalEdge edge ) => Node.Layout.Padding( (R.PhysicalEdge)(int)edge );
	public float LayoutBorder( PhysicalEdge edge ) => Node.Layout.Border( (R.PhysicalEdge)(int)edge );

	public override string ToString() => $"reference {Node} ({Context})";

	//The reference style dirties its node itself, the same way the production one does.
	private sealed class StyleView : IFixtureStyle
	{
		private readonly R.LayoutNode _node;
		private R.LayoutStyle S => _node.Style;

		public StyleView( R.LayoutNode node )
		{
			_node = node;
		}

		private static R.StyleLength ToEdge( StyleLength value )
		{
			if ( value.IsAuto ) return R.StyleLength.Auto;
			if ( value.IsPercent ) return R.StyleLength.Percent( value.Value );
			if ( value.IsPoints ) return R.StyleLength.Points( value.Value );
			return R.StyleLength.Undefined;
		}

		private static R.SizeLength ToSize( StyleLength value )
		{
			if ( value.IsAuto ) return R.SizeLength.Auto;
			if ( value.IsPercent ) return R.SizeLength.Percent( value.Value );
			if ( value.IsPoints ) return R.SizeLength.Points( value.Value );
			return R.SizeLength.Undefined;
		}

		private static StyleLength FromSize( R.SizeLength value )
		{
			if ( value.IsAuto ) return StyleLength.Auto;
			if ( value.IsPercent ) return StyleLength.Percent( value.Value );
			if ( value.IsPoints ) return StyleLength.Points( value.Value );
			return StyleLength.Undefined;
		}

		public Direction Direction { get => (Direction)(int)S.Direction; set => S.Direction = (R.Direction)(int)value; }
		public FlexDirection FlexDirection { get => (FlexDirection)(int)S.FlexDirection; set => S.FlexDirection = (R.FlexDirection)(int)value; }
		public Justify JustifyContent { get => (Justify)(int)S.JustifyContent; set => S.JustifyContent = (R.Justify)(int)value; }
		public Align AlignContent { get => (Align)(int)S.AlignContent; set => S.AlignContent = (R.Align)(int)value; }
		public Align AlignItems { get => (Align)(int)S.AlignItems; set => S.AlignItems = (R.Align)(int)value; }
		public Align AlignSelf { get => (Align)(int)S.AlignSelf; set => S.AlignSelf = (R.Align)(int)value; }
		public PositionType PositionType { get => (PositionType)(int)S.PositionType; set => S.PositionType = (R.PositionType)(int)value; }
		public Wrap FlexWrap { get => (Wrap)(int)S.FlexWrap; set => S.FlexWrap = (R.Wrap)(int)value; }
		public Overflow Overflow { get => (Overflow)(int)S.Overflow; set => S.Overflow = (R.Overflow)(int)value; }
		public Display Display { get => (Display)(int)S.Display; set => S.Display = (R.Display)(int)value; }
		public BoxSizing BoxSizing { get => (BoxSizing)(int)S.BoxSizing; set => S.BoxSizing = (R.BoxSizing)(int)value; }

		public string GridTemplateColumns { get => S.GridTemplateColumns.ToString(); set => S.GridTemplateColumns = R.GridTrackList.Parse( value ); }
		public string GridTemplateRows { get => S.GridTemplateRows.ToString(); set => S.GridTemplateRows = R.GridTrackList.Parse( value ); }
		public string GridAutoColumns { get => S.GridAutoColumns.ToString(); set => S.GridAutoColumns = R.GridTrackList.Parse( value ); }
		public string GridAutoRows { get => S.GridAutoRows.ToString(); set => S.GridAutoRows = R.GridTrackList.Parse( value ); }
		public string GridAutoFlow { get => S.GridAutoFlow.ToString(); set => S.GridAutoFlow = ParseFlow( value ); }

		private static R.GridAutoFlow ParseFlow( string value ) => ( value ?? "row" ).Trim() switch
		{
			"column" => R.GridAutoFlow.Column,
			"row dense" or "dense" => R.GridAutoFlow.RowDense,
			"column dense" or "dense column" => R.GridAutoFlow.ColumnDense,
			_ => R.GridAutoFlow.Row,
		};
		public string GridColumnStart { get => S.GridColumnStart.ToString(); set => S.GridColumnStart = R.GridLine.Parse( value ); }
		public string GridColumnEnd { get => S.GridColumnEnd.ToString(); set => S.GridColumnEnd = R.GridLine.Parse( value ); }
		public string GridRowStart { get => S.GridRowStart.ToString(); set => S.GridRowStart = R.GridLine.Parse( value ); }
		public string GridRowEnd { get => S.GridRowEnd.ToString(); set => S.GridRowEnd = R.GridLine.Parse( value ); }
		public Align JustifyItems { get => (Align)(int)S.JustifyItems; set => S.JustifyItems = (R.Align)(int)value; }
		public Align JustifySelf { get => (Align)(int)S.JustifySelf; set => S.JustifySelf = (R.Align)(int)value; }

		public float Flex { get => S.Flex; set => S.Flex = value; }
		public float FlexGrow { get => S.FlexGrow; set => S.FlexGrow = value; }
		public float FlexShrink { get => S.FlexShrink; set => S.FlexShrink = value; }
		public float AspectRatio { get => S.AspectRatio; set => S.AspectRatio = value; }
		public StyleLength FlexBasis { get => FromSize( S.FlexBasis ); set => S.FlexBasis = ToSize( value ); }

		public StyleLength Width { get => FromSize( S.GetDimension( R.Dimension.Width ) ); set => S.SetDimension( R.Dimension.Width, ToSize( value ) ); }
		public StyleLength Height { get => FromSize( S.GetDimension( R.Dimension.Height ) ); set => S.SetDimension( R.Dimension.Height, ToSize( value ) ); }
		public StyleLength MinWidth { get => FromSize( S.MinDimension( R.Dimension.Width ) ); set => S.SetMinDimension( R.Dimension.Width, ToSize( value ) ); }
		public StyleLength MinHeight { get => FromSize( S.MinDimension( R.Dimension.Height ) ); set => S.SetMinDimension( R.Dimension.Height, ToSize( value ) ); }
		public StyleLength MaxWidth { get => FromSize( S.MaxDimension( R.Dimension.Width ) ); set => S.SetMaxDimension( R.Dimension.Width, ToSize( value ) ); }
		public StyleLength MaxHeight { get => FromSize( S.MaxDimension( R.Dimension.Height ) ); set => S.SetMaxDimension( R.Dimension.Height, ToSize( value ) ); }

		public void SetMargin( Edge edge, StyleLength value ) => S.SetMargin( (R.Edge)(int)edge, ToEdge( value ) );
		public void SetPadding( Edge edge, StyleLength value ) => S.SetPadding( (R.Edge)(int)edge, ToEdge( value ) );
		public void SetBorder( Edge edge, StyleLength value ) => S.SetBorder( (R.Edge)(int)edge, ToEdge( value ) );
		public void SetPosition( Edge edge, StyleLength value ) => S.SetPosition( (R.Edge)(int)edge, ToEdge( value ) );
		public void SetGap( Gutter gutter, StyleLength value ) => S.SetGap( (R.Gutter)(int)gutter, ToEdge( value ) );
	}
}
