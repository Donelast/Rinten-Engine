using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using LayoutTests.Harness;
using Rinten.Layout;

namespace LayoutTests.Core;

//30-level deep trees with scenario-specific traits, seeded by name so every engine gets the same tree
internal static class DeepTrees
{
	public const int Levels = 30;

	public static readonly string[] Scenarios =
	{
		"plain", "percent", "minmax", "automargin", "wrap", "scroll",
		"mixed", "mixed_unbounded", "mixed_rtl", "mixed_fraction", "grow", "block", "grid",
	};

	[Flags]
	private enum Traits
	{
		None = 0,
		Percent = 1,
		MinMax = 2,
		AutoMargin = 4,
		Wrap = 8,
		Scroll = 16,
		Baseline = 32,
		Fraction = 64,
		Grow = 128,
		Block = 256,
		Grid = 512,
		Mixed = Percent | MinMax | AutoMargin | Wrap | Scroll | Baseline,
	}

	private sealed record Recipe( Traits Traits, float Width, float Height, Direction Direction );

	private static Recipe RecipeFor( string scenario ) => scenario switch
	{
		"plain" => new Recipe( Traits.None, 900, 700, Direction.LTR ),
		"percent" => new Recipe( Traits.Percent, 900, 700, Direction.LTR ),
		"minmax" => new Recipe( Traits.MinMax, 900, 700, Direction.LTR ),
		"automargin" => new Recipe( Traits.AutoMargin, 900, 700, Direction.LTR ),
		"wrap" => new Recipe( Traits.Wrap, 900, 700, Direction.LTR ),
		"scroll" => new Recipe( Traits.Scroll, 900, 700, Direction.LTR ),
		"mixed" => new Recipe( Traits.Mixed, 900, 700, Direction.LTR ),
		"mixed_unbounded" => new Recipe( Traits.Mixed, float.NaN, float.NaN, Direction.LTR ),
		"mixed_rtl" => new Recipe( Traits.Mixed, 900, 700, Direction.RTL ),
		"mixed_fraction" => new Recipe( Traits.Mixed | Traits.Fraction, 900, 700, Direction.LTR ),
		"grow" => new Recipe( Traits.Grow, 900, 700, Direction.LTR ),
		"block" => new Recipe( Traits.Block | Traits.MinMax, 900, 700, Direction.LTR ),
		"grid" => new Recipe( Traits.Grid | Traits.MinMax, 900, 700, Direction.LTR ),
		_ => throw new ArgumentException( scenario ),
	};

	private sealed class Builder
	{
		public readonly IFixtureEngine Engine;
		public readonly Recipe Recipe;
		public readonly Rng Rng;
		public readonly List<IFixtureNode> All;

		public Builder( IFixtureEngine engine, Recipe recipe, Rng rng, List<IFixtureNode> all )
		{
			Engine = engine;
			Recipe = recipe;
			Rng = rng;
			All = all;
		}

		public IFixtureNode Node()
		{
			var node = Engine.CreateUiNode();
			All.Add( node );
			return node;
		}

		//A size, whole or with a half-pixel fraction when the scenario asks for fractions.
		public float Size( int low, int high ) => Rng.Between( low, high ) + ((Recipe.Traits & Traits.Fraction) != 0 && Rng.Roll( 50 ) ? 0.5f : 0f);

		public IFixtureNode Label()
		{
			var node = Node();
			node.Context = TextMeasure.Words( Rng, Rng.Between( 1, 7 ) );
			node.MeasureFunc = TextMeasure.Measure;
			return node;
		}

		public IFixtureNode FixedBox()
		{
			var node = Node();
			node.Style.Width = StyleLength.Points( Size( 6, 64 ) );
			node.Style.Height = StyleLength.Points( Size( 6, 40 ) );
			return node;
		}

		//A small auto-sized group of two labels, so the spine measures containers off its own path.
		public IFixtureNode Group( bool parentIsRow )
		{
			var node = Node();
			node.Style.FlexDirection = parentIsRow ? FlexDirection.Column : FlexDirection.Row;
			node.Style.SetPadding( Edge.All, StyleLength.Points( Rng.Between( 0, 3 ) ) );
			if ( Rng.Roll( 50 ) ) node.Style.FlexGrow = 1;
			Decorate( node.Style, !parentIsRow, 0 );
			node.AddChild( Label() );
			node.AddChild( Label() );
			return node;
		}

		public IFixtureNode Sibling( bool parentIsRow, int level )
		{
			var roll = Rng.Between( 0, 99 );
			if ( roll < 60 )
			{
				var label = Label();
				if ( Rng.Roll( 30 ) ) label.Style.FlexGrow = 1;
				if ( Rng.Roll( 30 ) ) label.Style.FlexShrink = 0;
				DecorateLeaf( label.Style, parentIsRow, level );
				return label;
			}

			if ( roll < 85 )
			{
				var box = FixedBox();
				if ( Rng.Roll( 50 ) ) box.Style.FlexGrow = 1;
				return box;
			}

			return Group( parentIsRow );
		}

		public IFixtureNode SpineContainer( int level )
		{
			var node = Node();
			var style = node.Style;
			var isRow = level % 2 == 0;
			style.FlexDirection = isRow ? FlexDirection.Row : FlexDirection.Column;

			if ( (Recipe.Traits & Traits.Grow) != 0 )
			{
				style.FlexGrow = 1;
				style.FlexShrink = 1;
				style.SetMargin( Edge.All, StyleLength.Points( 10 ) );
				return node;
			}

			style.SetMargin( Edge.All, StyleLength.Points( Rng.Between( 0, 6 ) ) );
			style.SetPadding( Edge.All, StyleLength.Points( Rng.Between( 0, 5 ) ) );
			if ( Rng.Roll( 20 ) ) style.SetBorder( Edge.All, StyleLength.Points( 1 ) );
			if ( Rng.Roll( 40 ) ) style.FlexGrow = 1;
			if ( Rng.Roll( 20 ) ) style.FlexShrink = 0;
			if ( Rng.Roll( 15 ) ) style.AlignSelf = Rng.Pick( Align.FlexStart, Align.Center, Align.FlexEnd, Align.Stretch );
			Decorate( style, isRow, level );
			if ( (Recipe.Traits & Traits.Block) != 0 && Rng.Roll( 50 ) ) style.Display = Display.Block;
			if ( (Recipe.Traits & Traits.Grid) != 0 && Rng.Roll( 50 ) )
			{
				style.Display = Display.Grid;
				style.GridTemplateColumns = Rng.Pick( "1fr 1fr", "80px auto", "repeat(3, 1fr)", "auto 1fr", "minmax(40px, 1fr) 2fr" );
				if ( Rng.Roll( 30 ) ) style.GridAutoFlow = "column";
			}
			return node;
		}

		//The scenario's traits on a container.
		public void Decorate( IFixtureStyle style, bool isRow, int level )
		{
			var traits = Recipe.Traits;

			if ( (traits & Traits.Percent) != 0 )
			{
				if ( level % 3 == 1 ) style.Width = StyleLength.Percent( Rng.Between( 40, 95 ) );
				if ( level % 4 == 2 ) style.Height = StyleLength.Percent( Rng.Between( 30, 90 ) );
				if ( Rng.Roll( 30 ) ) style.SetPadding( Edge.Horizontal, StyleLength.Percent( Rng.Between( 1, 4 ) ) );
				if ( Rng.Roll( 30 ) ) style.SetMargin( Edge.Left, StyleLength.Percent( Rng.Between( 1, 3 ) ) );
			}

			if ( (traits & Traits.MinMax) != 0 )
			{
				switch ( Rng.Between( 0, 5 ) )
				{
					case 0: style.MinWidth = StyleLength.Points( Size( 40, 200 ) ); break;
					case 1: style.MaxWidth = StyleLength.Points( Size( 120, 600 ) ); break;
					case 2: style.MinHeight = StyleLength.Points( Size( 20, 80 ) ); break;
					case 3: style.MaxHeight = StyleLength.Points( Size( 60, 400 ) ); break;
					case 4: style.MaxWidth = StyleLength.Percent( Rng.Between( 50, 90 ) ); break;
				}
			}

			if ( (traits & Traits.AutoMargin) != 0 )
			{
				switch ( Rng.Between( 0, 4 ) )
				{
					case 0: style.SetMargin( isRow ? Edge.Left : Edge.Top, StyleLength.Auto ); break;
					case 1: style.SetMargin( isRow ? Edge.Top : Edge.Left, StyleLength.Auto ); break;
					case 2: style.JustifyContent = Rng.Pick( Justify.FlexStart, Justify.Center, Justify.FlexEnd, Justify.SpaceBetween, Justify.SpaceAround, Justify.SpaceEvenly ); break;
					case 3: style.AlignItems = Rng.Pick( Align.FlexStart, Align.Center, Align.FlexEnd, Align.Stretch ); break;
				}
			}

			if ( (traits & Traits.Wrap) != 0 && Rng.Roll( 35 ) )
			{
				style.FlexWrap = Rng.Roll( 80 ) ? Wrap.Wrap : Wrap.WrapReverse;
				style.SetGap( Gutter.All, StyleLength.Points( Rng.Between( 1, 6 ) ) );
				if ( Rng.Roll( 50 ) ) style.AlignContent = Rng.Pick( Align.FlexStart, Align.Center, Align.FlexEnd, Align.Stretch, Align.SpaceBetween, Align.SpaceAround, Align.SpaceEvenly );
			}

			if ( (traits & Traits.Scroll) != 0 && Rng.Roll( 25 ) )
				style.Overflow = Rng.Roll( 50 ) ? Overflow.Scroll : Overflow.Hidden;

			if ( (traits & Traits.Baseline) != 0 && isRow && Rng.Roll( 20 ) )
				style.AlignItems = Align.Baseline;
		}

		//The subset of traits that make sense on a leaf.
		public void DecorateLeaf( IFixtureStyle style, bool parentIsRow, int level )
		{
			var traits = Recipe.Traits;
			if ( (traits & Traits.Percent) != 0 && Rng.Roll( 25 ) ) style.Width = StyleLength.Percent( Rng.Between( 20, 60 ) );
			if ( (traits & Traits.MinMax) != 0 && Rng.Roll( 25 ) ) style.MinWidth = StyleLength.Points( Size( 20, 120 ) );
			if ( (traits & Traits.AutoMargin) != 0 && Rng.Roll( 20 ) ) style.SetMargin( parentIsRow ? Edge.Left : Edge.Top, StyleLength.Auto );
		}
	}

	public static IFixtureNode Build( IFixtureEngine engine, string scenario, List<IFixtureNode> all, out IFixtureNode deepLeaf,
		out float rootWidth, out float rootHeight, out Direction direction )
	{
		var recipe = RecipeFor( scenario );
		rootWidth = recipe.Width;
		rootHeight = recipe.Height;
		direction = recipe.Direction;

		var b = new Builder( engine, recipe, new Rng( Rng.Hash( scenario ) ), all );

		var root = b.Node();
		root.Style.FlexDirection = FlexDirection.Column;
		root.Style.SetPadding( Edge.All, StyleLength.Points( 3 ) );

		var spine = root;
		for ( var level = 0; level < Levels; level++ )
		{
			var parentIsRow = level == 0 || (level - 1) % 2 == 0;
			var container = b.SpineContainer( level );

			var leading = b.Rng.Between( 0, 2 );
			var trailing = b.Rng.Between( 0, 2 );
			for ( var i = 0; i < leading; i++ ) spine.AddChild( b.Sibling( parentIsRow, level ) );
			spine.AddChild( container );
			for ( var i = 0; i < trailing; i++ ) spine.AddChild( b.Sibling( parentIsRow, level ) );

			spine = container;
		}

		//The innermost container: the first label is the one the incremental pass edits.
		deepLeaf = b.Label();
		spine.AddChild( deepLeaf );
		spine.AddChild( b.FixedBox() );
		spine.AddChild( b.Label() );
		return root;
	}

	//The text edit the incremental pass makes on the deepest label.
	public static void EditLeaf( IFixtureNode leaf )
	{
		leaf.Context = (string)leaf.Context + " and a little more text";
		leaf.MarkDirty();
	}

	//The second constraint the resize pass lays out under.
	public static (float Width, float Height) Resized( float rootWidth ) => (float.IsNaN( rootWidth ) ? 640f : rootWidth - 137f, float.NaN);

	//Three passes on one tree (initial, after the deepest label changed, after the root
	//resized), one line per pass, "left,top,width,height" per node in document order.
	public static string DumpIncremental( IFixtureEngine engine, string scenario )
	{
		var all = new List<IFixtureNode>();
		var root = Build( engine, scenario, all, out var deepLeaf, out var width, out var height, out var direction );
		var sb = new StringBuilder();

		root.CalculateLayout( width, height, direction );
		AppendRects( sb, all );

		EditLeaf( deepLeaf );
		root.CalculateLayout( width, height, direction );
		sb.Append( '\n' );
		AppendRects( sb, all );

		var (resizedWidth, resizedHeight) = Resized( width );
		root.CalculateLayout( resizedWidth, resizedHeight, direction );
		sb.Append( '\n' );
		AppendRects( sb, all );

		return sb.ToString();
	}

	//The same three states, each from a freshly built tree, so no cache is involved.
	public static string DumpFresh( IFixtureEngine engine, string scenario )
	{
		var sb = new StringBuilder();
		for ( var pass = 0; pass < 3; pass++ )
		{
			var all = new List<IFixtureNode>();
			var root = Build( engine, scenario, all, out var deepLeaf, out var width, out var height, out var direction );
			if ( pass >= 1 ) deepLeaf.Context = (string)deepLeaf.Context + " and a little more text";
			if ( pass == 2 ) (width, height) = Resized( width );
			root.CalculateLayout( width, height, direction );
			if ( pass > 0 ) sb.Append( '\n' );
			AppendRects( sb, all );
		}

		return sb.ToString();
	}

	public static void AppendRects( StringBuilder sb, List<IFixtureNode> all )
	{
		var ci = CultureInfo.InvariantCulture;
		for ( var i = 0; i < all.Count; i++ )
		{
			if ( i > 0 ) sb.Append( ';' );
			var n = all[i];
			sb.Append( n.LayoutLeft.ToString( "R", ci ) ).Append( ',' )
				.Append( n.LayoutTop.ToString( "R", ci ) ).Append( ',' )
				.Append( n.LayoutWidth.ToString( "R", ci ) ).Append( ',' )
				.Append( n.LayoutHeight.ToString( "R", ci ) );
		}
	}
}
