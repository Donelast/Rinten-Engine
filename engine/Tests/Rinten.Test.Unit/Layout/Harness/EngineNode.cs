using System;
using Rinten.Layout;

namespace LayoutTests.Harness;

//IFixtureNode over the production engine. The wrapper is stored in the node's LayoutNode.Context
//so a measure callback can find its way back; the fixture's own context lives on the wrapper.
internal sealed class EngineNode : IFixtureNode
{
	internal sealed class Factory : IFixtureEngine
	{
		public string Name => "production";
		public IFixtureNode CreateNode() => new EngineNode( FixtureConfig );
		public IFixtureNode CreateUiNode()
		{
			//The interface declares position: static by default; the engine's own default is relative.
			var node = new EngineNode( UiConfig );
			node.Node.Style.PositionType = PositionType.Static;
			return node;
		}
	}

	//The library defaults: column, flex-start, no pixel snapping.
	private static readonly LayoutConfig FixtureConfig = new() { UseWebDefaults = false, PointScaleFactor = 0 };

	//What the panels build their nodes with.
	private static readonly LayoutConfig UiConfig = new() { UseWebDefaults = true, PointScaleFactor = 0 };

	internal LayoutNode Node { get; }
	private readonly StyleView _style;
	private FixtureMeasureFunc _measure;

	private EngineNode( LayoutConfig config )
	{
		Node = new LayoutNode( config ) { Context = this };
		_style = new StyleView( Node );
	}

	public IFixtureEngine Engine => FixtureEngines.Production;
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
				return _measure( (EngineNode)node.Context, width, widthMode, height, heightMode );
			} );
		}
	}

	public bool HasMeasureFunc => Node.HasMeasureFunc;

	public IFixtureNode Owner => Node.Owner?.Context as EngineNode;
	public int ChildCount => Node.ChildCount;
	public IFixtureNode GetChild( int index ) => (EngineNode)Node.ChildAt( index ).Context;
	public void AddChild( IFixtureNode child ) => Node.InsertChild( ((EngineNode)child).Node, Node.ChildCount );
	public void InsertChild( IFixtureNode child, int index ) => Node.InsertChild( ((EngineNode)child).Node, index );
	public bool RemoveChild( IFixtureNode child ) => Node.RemoveChild( ((EngineNode)child).Node );
	public void RemoveAllChildren() => Node.RemoveAllChildren();

	public void CalculateLayout( float ownerWidth = float.NaN, float ownerHeight = float.NaN, Direction ownerDirection = Direction.LTR )
		=> FlexLayout.Calculate( Node, ownerWidth, ownerHeight, ownerDirection );

	public void MarkDirty() => Node.MarkDirtyAndPropagate();

	public bool IsDirty => Node.IsDirty;

	public bool HasNewLayout
	{
		get => Node.HasNewLayout;
		set => Node.HasNewLayout = value;
	}

	public float LayoutLeft => Node.Layout.Position( PhysicalEdge.Left );
	public float LayoutTop => Node.Layout.Position( PhysicalEdge.Top );
	public float LayoutWidth => Node.Layout.GetDimension( Dimension.Width );
	public float LayoutHeight => Node.Layout.GetDimension( Dimension.Height );
	public bool LayoutHadOverflow => Node.Layout.HadOverflow;
	public float LayoutMargin( PhysicalEdge edge ) => Node.Layout.Margin( edge );
	public float LayoutPadding( PhysicalEdge edge ) => Node.Layout.Padding( edge );
	public float LayoutBorder( PhysicalEdge edge ) => Node.Layout.Border( edge );

	public override string ToString() => $"{Node} ({Context})";

	//Writes straight into LayoutStyle and dirties the node, which is what the panel seam does after its own comparison.
	internal sealed class StyleView : IFixtureStyle
	{
		private readonly LayoutNode _node;
		private LayoutStyle S => _node.Style;

		public StyleView( LayoutNode node )
		{
			_node = node;
		}

		private void Dirty() => _node.MarkDirtyAndPropagate();

		public StyleLength Margin( Edge edge ) => S.Margin( edge );
		public StyleLength Padding( Edge edge ) => S.Padding( edge );
		public StyleLength Inset( Edge edge ) => S.Position( edge );
		public StyleLength Border( Edge edge ) => S.Border( edge );

		private static SizeLength ToSize( StyleLength value )
		{
			if ( value.IsAuto ) return SizeLength.Auto;
			if ( value.IsPercent ) return SizeLength.Percent( value.Value );
			if ( value.IsPoints ) return SizeLength.Points( value.Value );
			return SizeLength.Undefined;
		}

		private static StyleLength FromSize( SizeLength value )
		{
			if ( value.IsAuto ) return StyleLength.Auto;
			if ( value.IsPercent ) return StyleLength.Percent( value.Value );
			if ( value.IsPoints ) return StyleLength.Points( value.Value );
			return StyleLength.Undefined;
		}

		public Direction Direction { get => S.Direction; set { S.Direction = value; Dirty(); } }
		public FlexDirection FlexDirection { get => S.FlexDirection; set { S.FlexDirection = value; Dirty(); } }
		public Justify JustifyContent { get => S.JustifyContent; set { S.JustifyContent = value; Dirty(); } }
		public Align AlignContent { get => S.AlignContent; set { S.AlignContent = value; Dirty(); } }
		public Align AlignItems { get => S.AlignItems; set { S.AlignItems = value; Dirty(); } }
		public Align AlignSelf { get => S.AlignSelf; set { S.AlignSelf = value; Dirty(); } }
		public PositionType PositionType { get => S.PositionType; set { S.PositionType = value; Dirty(); } }
		public Wrap FlexWrap { get => S.FlexWrap; set { S.FlexWrap = value; Dirty(); } }
		public Overflow Overflow { get => S.Overflow; set { S.Overflow = value; Dirty(); } }
		public Display Display { get => S.Display; set { S.Display = value; Dirty(); } }
		public BoxSizing BoxSizing { get => S.BoxSizing; set { S.BoxSizing = value; Dirty(); } }

		public string GridTemplateColumns { get => S.GridTemplateColumns.ToString(); set { S.GridTemplateColumns = GridTrackList.Parse( value ); Dirty(); } }
		public string GridTemplateRows { get => S.GridTemplateRows.ToString(); set { S.GridTemplateRows = GridTrackList.Parse( value ); Dirty(); } }
		public string GridAutoColumns { get => S.GridAutoColumns.ToString(); set { S.GridAutoColumns = GridTrackList.Parse( value ); Dirty(); } }
		public string GridAutoRows { get => S.GridAutoRows.ToString(); set { S.GridAutoRows = GridTrackList.Parse( value ); Dirty(); } }
		public string GridAutoFlow { get => S.GridAutoFlow.ToString(); set { S.GridAutoFlow = ParseFlow( value ); Dirty(); } }
		public string GridColumnStart { get => S.GridColumnStart.ToString(); set { S.GridColumnStart = GridLine.Parse( value ); Dirty(); } }
		public string GridColumnEnd { get => S.GridColumnEnd.ToString(); set { S.GridColumnEnd = GridLine.Parse( value ); Dirty(); } }
		public string GridRowStart { get => S.GridRowStart.ToString(); set { S.GridRowStart = GridLine.Parse( value ); Dirty(); } }
		public string GridRowEnd { get => S.GridRowEnd.ToString(); set { S.GridRowEnd = GridLine.Parse( value ); Dirty(); } }
		public Align JustifyItems { get => S.JustifyItems; set { S.JustifyItems = value; Dirty(); } }
		public Align JustifySelf { get => S.JustifySelf; set { S.JustifySelf = value; Dirty(); } }

		internal static GridAutoFlow ParseFlow( string value ) => ( value ?? "row" ).Trim() switch
		{
			"column" => Rinten.Layout.GridAutoFlow.Column,
			"row dense" or "dense" => Rinten.Layout.GridAutoFlow.RowDense,
			"column dense" or "dense column" => Rinten.Layout.GridAutoFlow.ColumnDense,
			_ => Rinten.Layout.GridAutoFlow.Row,
		};

		public float Flex { get => S.Flex; set { S.Flex = value; Dirty(); } }
		public float FlexGrow { get => S.FlexGrow; set { S.FlexGrow = value; Dirty(); } }
		public float FlexShrink { get => S.FlexShrink; set { S.FlexShrink = value; Dirty(); } }
		public float AspectRatio { get => S.AspectRatio; set { S.AspectRatio = value; Dirty(); } }
		public StyleLength FlexBasis { get => FromSize( S.FlexBasis ); set { S.FlexBasis = ToSize( value ); Dirty(); } }

		public StyleLength Width { get => FromSize( S.GetDimension( Dimension.Width ) ); set { S.SetDimension( Dimension.Width, ToSize( value ) ); Dirty(); } }
		public StyleLength Height { get => FromSize( S.GetDimension( Dimension.Height ) ); set { S.SetDimension( Dimension.Height, ToSize( value ) ); Dirty(); } }
		public StyleLength MinWidth { get => FromSize( S.MinDimension( Dimension.Width ) ); set { S.SetMinDimension( Dimension.Width, ToSize( value ) ); Dirty(); } }
		public StyleLength MinHeight { get => FromSize( S.MinDimension( Dimension.Height ) ); set { S.SetMinDimension( Dimension.Height, ToSize( value ) ); Dirty(); } }
		public StyleLength MaxWidth { get => FromSize( S.MaxDimension( Dimension.Width ) ); set { S.SetMaxDimension( Dimension.Width, ToSize( value ) ); Dirty(); } }
		public StyleLength MaxHeight { get => FromSize( S.MaxDimension( Dimension.Height ) ); set { S.SetMaxDimension( Dimension.Height, ToSize( value ) ); Dirty(); } }

		public void SetMargin( Edge edge, StyleLength value ) { S.SetMargin( edge, value ); Dirty(); }
		public void SetPadding( Edge edge, StyleLength value ) { S.SetPadding( edge, value ); Dirty(); }
		public void SetBorder( Edge edge, StyleLength value ) { S.SetBorder( edge, value ); Dirty(); }
		public void SetPosition( Edge edge, StyleLength value ) { S.SetPosition( edge, value ); Dirty(); }
		public void SetGap( Gutter gutter, StyleLength value ) { S.SetGap( gutter, value ); Dirty(); }
	}
}
