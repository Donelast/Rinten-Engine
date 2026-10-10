using System;
using System.Collections.Generic;
using Rinten.Layout;

namespace Benchmarks.Layout;

//Tree shapes for the layout benchmarks, built on engine nodes; same shapes as the unit-test metrics
internal static class LayoutTrees
{
	//What the panels build their nodes with: web defaults, no pixel snapping, position static.
	public static readonly LayoutConfig UiConfig = new() { UseWebDefaults = true, PointScaleFactor = 0 };

	public const int Levels = 30;
	private const float GlyphWidth = 8f;
	private const float LineHeight = 12f;

	//How many times any measure function ran since the counter was last reset.
	public static int MeasureCalls;

	private static readonly string[] Vocabulary =
	{
		"panel", "row", "inspector", "value", "name", "x", "position", "rotation", "scale", "mesh",
		"material", "light", "camera", "on", "off", "a", "the", "component", "hierarchy", "tool",
	};

	//A monospace text measure: 8 x 12 glyphs, greedy word wrap. The context is the text.
	public static LayoutSize MeasureText( LayoutNode node, float width, MeasureMode widthMode, float height, MeasureMode heightMode )
	{
		MeasureCalls++;
		var text = (string)node.Context;
		var natural = text.Length * GlyphWidth;

		var measuredWidth = widthMode switch
		{
			MeasureMode.Exactly => width,
			MeasureMode.AtMost => MathF.Min( natural, width ),
			_ => natural,
		};

		var contentHeight = CountLines( text, measuredWidth ) * LineHeight;
		var measuredHeight = heightMode switch
		{
			MeasureMode.Exactly => height,
			MeasureMode.AtMost => MathF.Min( contentHeight, height ),
			_ => contentHeight,
		};

		return new LayoutSize( measuredWidth, measuredHeight );
	}

	//Scans rather than splits, so the measure function never allocates and the allocation columns describe the engine alone.
	private static int CountLines( string text, float width )
	{
		if ( text.Length == 0 ) return 1;

		var lines = 1;
		var used = 0f;
		var start = 0;
		for ( var i = 0; i <= text.Length; i++ )
		{
			if ( i < text.Length && text[i] != ' ' ) continue;

			var wordWidth = (i - start) * GlyphWidth;
			start = i + 1;
			var needed = used == 0 ? wordWidth : used + GlyphWidth + wordWidth;
			if ( used > 0 && needed > width )
			{
				lines++;
				used = wordWidth;
			}
			else
			{
				used = needed;
			}
		}

		return lines;
	}

	//splitmix64, the same generator as the unit tests, so a name is the same tree here.
	public sealed class Rng
	{
		private ulong state;
		public Rng( ulong seed ) { state = seed; }

		public static ulong Hash( string name )
		{
			var hash = 14695981039346656037UL;
			foreach ( var c in name ) hash = (hash ^ c) * 1099511628211UL;
			return hash;
		}

		public ulong Next()
		{
			state += 0x9E3779B97F4A7C15UL;
			var z = state;
			z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
			z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
			return z ^ (z >> 31);
		}

		public int Between( int low, int high ) => low + (int)(Next() % (ulong)(high - low + 1));
		public bool Roll( int percent ) => Between( 0, 99 ) < percent;
		public T Pick<T>( params T[] items ) => items[(int)(Next() % (ulong)items.Length)];
	}

	public sealed class Tree
	{
		public LayoutNode Root;
		public List<LayoutNode> All = new();
		public List<LayoutNode> Texts = new();
		public float Width;
		public float Height;
	}

	public static Tree Build( string shape )
	{
		return shape switch
		{
			"deep_plain" => Spine( "plain", false ),
			"deep_grow" => Spine( "grow", true ),
			"widerow" => WideRow(),
			"inspector" => Inspector(),
			"grid" => GridInspector(),
			_ => throw new ArgumentException( shape ),
		};
	}

	private static LayoutNode Node( Tree tree )
	{
		var node = new LayoutNode( UiConfig );
		node.Style.PositionType = PositionType.Static;
		tree.All.Add( node );
		return node;
	}

	private static LayoutNode Label( Tree tree, Rng rng, int words )
	{
		var node = Node( tree );
		var parts = new string[words];
		for ( var i = 0; i < words; i++ ) parts[i] = rng.Pick( Vocabulary );
		node.Context = string.Join( ' ', parts );
		node.SetMeasureFunc( MeasureText );
		tree.Texts.Add( node );
		return node;
	}

	private static LayoutNode FixedBox( Tree tree, Rng rng )
	{
		var node = Node( tree );
		node.Style.SetDimension( Dimension.Width, SizeLength.Points( rng.Between( 6, 64 ) ) );
		node.Style.SetDimension( Dimension.Height, SizeLength.Points( rng.Between( 6, 40 ) ) );
		return node;
	}

	private static void Add( LayoutNode parent, LayoutNode child ) => parent.InsertChild( child, parent.ChildCount );

	//The plain and grow deep-tree scenarios of the unit tests: a spine of 30 nested containers.
	private static Tree Spine( string scenario, bool grow )
	{
		var tree = new Tree { Width = 900, Height = 700 };
		var rng = new Rng( Rng.Hash( scenario ) );

		var root = Node( tree );
		root.Style.FlexDirection = FlexDirection.Column;
		root.Style.SetPadding( Edge.All, StyleLength.Points( 3 ) );
		tree.Root = root;

		var spine = root;
		for ( var level = 0; level < Levels; level++ )
		{
			var parentIsRow = level == 0 || (level - 1) % 2 == 0;
			var container = Node( tree );
			var style = container.Style;
			style.FlexDirection = level % 2 == 0 ? FlexDirection.Row : FlexDirection.Column;
			if ( grow )
			{
				style.FlexGrow = 1;
				style.FlexShrink = 1;
				style.SetMargin( Edge.All, StyleLength.Points( 10 ) );
			}
			else
			{
				style.SetMargin( Edge.All, StyleLength.Points( rng.Between( 0, 6 ) ) );
				style.SetPadding( Edge.All, StyleLength.Points( rng.Between( 0, 5 ) ) );
				if ( rng.Roll( 20 ) ) style.SetBorder( Edge.All, StyleLength.Points( 1 ) );
				if ( rng.Roll( 40 ) ) style.FlexGrow = 1;
				if ( rng.Roll( 20 ) ) style.FlexShrink = 0;
				if ( rng.Roll( 15 ) ) style.AlignSelf = rng.Pick( Align.FlexStart, Align.Center, Align.FlexEnd, Align.Stretch );
			}

			var leading = rng.Between( 0, 2 );
			var trailing = rng.Between( 0, 2 );
			for ( var i = 0; i < leading; i++ ) Add( spine, Sibling( tree, rng, parentIsRow ) );
			Add( spine, container );
			for ( var i = 0; i < trailing; i++ ) Add( spine, Sibling( tree, rng, parentIsRow ) );

			spine = container;
		}

		Add( spine, Label( tree, rng, rng.Between( 1, 7 ) ) );
		Add( spine, FixedBox( tree, rng ) );
		Add( spine, Label( tree, rng, rng.Between( 1, 7 ) ) );
		return tree;
	}

	private static LayoutNode Sibling( Tree tree, Rng rng, bool parentIsRow )
	{
		var roll = rng.Between( 0, 99 );
		if ( roll < 60 )
		{
			var label = Label( tree, rng, rng.Between( 1, 7 ) );
			if ( rng.Roll( 30 ) ) label.Style.FlexGrow = 1;
			if ( rng.Roll( 30 ) ) label.Style.FlexShrink = 0;
			return label;
		}

		if ( roll < 85 )
		{
			var box = FixedBox( tree, rng );
			if ( rng.Roll( 50 ) ) box.Style.FlexGrow = 1;
			return box;
		}

		var group = Node( tree );
		group.Style.FlexDirection = parentIsRow ? FlexDirection.Column : FlexDirection.Row;
		group.Style.SetPadding( Edge.All, StyleLength.Points( rng.Between( 0, 3 ) ) );
		if ( rng.Roll( 50 ) ) group.Style.FlexGrow = 1;
		Add( group, Label( tree, rng, rng.Between( 1, 7 ) ) );
		Add( group, Label( tree, rng, rng.Between( 1, 7 ) ) );
		return group;
	}

	//Twenty wrapping rows of fifty labels each.
	private static Tree WideRow()
	{
		var tree = new Tree { Width = 1200, Height = 800 };
		var rng = new Rng( 11 );
		var root = Node( tree );
		root.Style.FlexDirection = FlexDirection.Column;
		tree.Root = root;

		for ( var r = 0; r < 20; r++ )
		{
			var row = Node( tree );
			row.Style.FlexDirection = FlexDirection.Row;
			row.Style.FlexWrap = Wrap.Wrap;
			row.Style.SetGap( Gutter.All, StyleLength.Points( 4 ) );
			row.Style.SetPadding( Edge.All, StyleLength.Points( 2 ) );
			Add( root, row );
			for ( var i = 0; i < 50; i++ )
			{
				var label = Label( tree, rng, rng.Between( 1, 3 ) );
				label.Style.SetMargin( Edge.All, StyleLength.Points( 1 ) );
				Add( row, label );
			}
		}

		return tree;
	}

	//An inspector: groups of rows, each a label and an auto-sized value box with text.
	private static Tree Inspector()
	{
		var tree = new Tree { Width = 420, Height = float.NaN };
		var rng = new Rng( 23 );
		var root = Node( tree );
		root.Style.FlexDirection = FlexDirection.Column;
		tree.Root = root;

		for ( var g = 0; g < 25; g++ )
		{
			var group = Node( tree );
			group.Style.FlexDirection = FlexDirection.Column;
			group.Style.SetPadding( Edge.All, StyleLength.Points( 4 ) );
			group.Style.SetMargin( Edge.Bottom, StyleLength.Points( 6 ) );
			Add( root, group );

			var header = Node( tree );
			header.Style.FlexDirection = FlexDirection.Row;
			header.Style.SetPadding( Edge.Vertical, StyleLength.Points( 3 ) );
			Add( group, header );
			Add( header, Label( tree, rng, rng.Between( 1, 3 ) ) );

			for ( var r = 0; r < 20; r++ )
			{
				var row = Node( tree );
				row.Style.FlexDirection = FlexDirection.Row;
				row.Style.AlignItems = Align.Center;
				row.Style.SetPadding( Edge.Vertical, StyleLength.Points( 2 ) );
				Add( group, row );

				var label = Label( tree, rng, rng.Between( 1, 2 ) );
				label.Style.SetDimension( Dimension.Width, SizeLength.Percent( 40 ) );
				Add( row, label );

				var value = Node( tree );
				value.Style.FlexDirection = FlexDirection.Row;
				value.Style.FlexGrow = 1;
				value.Style.FlexShrink = 1;
				value.Style.SetPadding( Edge.All, StyleLength.Points( 2 ) );
				value.Style.SetBorder( Edge.All, StyleLength.Points( 1 ) );
				Add( row, value );

				var inner = Node( tree );
				inner.Style.FlexDirection = FlexDirection.Row;
				inner.Style.FlexGrow = 1;
				inner.Style.SetPadding( Edge.Horizontal, StyleLength.Points( 3 ) );
				Add( value, inner );
				Add( inner, Label( tree, rng, rng.Between( 1, 4 ) ) );
			}
		}

		return tree;
	}
	//The inspector again, with every row a three-column grid: a label, a control that takes the rest, and a button.
	private static Tree GridInspector()
	{
		var tree = new Tree { Width = 420, Height = float.NaN };
		var rng = new Rng( 29 );
		var root = Node( tree );
		root.Style.FlexDirection = FlexDirection.Column;
		tree.Root = root;

		for ( var g = 0; g < 25; g++ )
		{
			var group = Node( tree );
			group.Style.FlexDirection = FlexDirection.Column;
			group.Style.SetPadding( Edge.All, StyleLength.Points( 4 ) );
			group.Style.SetMargin( Edge.Bottom, StyleLength.Points( 6 ) );
			Add( root, group );

			for ( var r = 0; r < 20; r++ )
			{
				var row = Node( tree );
				row.Style.Display = Display.Grid;
				row.Style.GridTemplateColumns = GridTrackList.Parse( "120px 1fr auto" );
				row.Style.AlignItems = Align.Center;
				row.Style.SetGap( Gutter.Column, StyleLength.Points( 4 ) );
				row.Style.SetPadding( Edge.Vertical, StyleLength.Points( 2 ) );
				Add( group, row );

				Add( row, Label( tree, rng, rng.Between( 1, 2 ) ) );

				var value = Node( tree );
				value.Style.FlexDirection = FlexDirection.Row;
				value.Style.SetPadding( Edge.All, StyleLength.Points( 2 ) );
				value.Style.SetBorder( Edge.All, StyleLength.Points( 1 ) );
				Add( row, value );
				Add( value, Label( tree, rng, rng.Between( 1, 4 ) ) );

				Add( row, FixedBox( tree, rng ) );
			}
		}

		return tree;
	}
}
