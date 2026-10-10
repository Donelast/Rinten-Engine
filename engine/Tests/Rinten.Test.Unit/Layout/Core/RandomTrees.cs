using System;
using System.Collections.Generic;
using LayoutTests.Harness;
using Rinten.Layout;

namespace LayoutTests.Core;

//Random flex trees for the differential tests: the same seed builds the same tree in any engine. Uses only what the frozen reference
//implements, so a production feature the reference lacks (contents, content-box sizing, fixed positioning) never enters a comparison.
internal static class RandomTrees
{
	public const int MaxDepth = 7;

	private static StyleLength RandomLength( Rng rng, int min, int max, int percentChance = 15, int autoChance = 0 )
	{
		var roll = rng.Between( 0, 99 );
		if ( roll < autoChance ) return StyleLength.Auto;
		if ( roll < autoChance + percentChance ) return StyleLength.Percent( rng.Between( 10, 100 ) );
		return StyleLength.Points( rng.Between( min, max ) );
	}

	public static IFixtureNode Build( IFixtureEngine engine, ulong seed, List<IFixtureNode> all, List<IFixtureNode> texts )
	{
		var rng = new Rng( seed );
		var root = engine.CreateUiNode();
		root.Style.Width = StyleLength.Points( 800 );
		root.Style.Height = StyleLength.Points( 600 );
		root.Style.FlexDirection = rng.Roll( 50 ) ? FlexDirection.Row : FlexDirection.Column;
		root.Style.SetPadding( Edge.All, StyleLength.Points( rng.Between( 0, 8 ) ) );
		all.Add( root );

		Fill( engine, rng, root, 1, all, texts );
		return root;
	}

	private static void Fill( IFixtureEngine engine, Rng rng, IFixtureNode parent, int depth, List<IFixtureNode> all, List<IFixtureNode> texts )
	{
		var count = rng.Between( 1, depth < 3 ? 5 : 3 );
		for ( var i = 0; i < count; i++ )
		{
			var node = engine.CreateUiNode();
			all.Add( node );
			var style = node.Style;

			var leaf = depth >= MaxDepth || rng.Roll( 35 );
			if ( leaf && rng.Roll( 65 ) )
			{
				node.Context = TextMeasure.Words( rng, rng.Between( 1, 6 ) );
				node.MeasureFunc = TextMeasure.Measure;
				texts.Add( node );
			}
			else if ( leaf )
			{
				style.Width = RandomLength( rng, 5, 120 );
				style.Height = RandomLength( rng, 5, 80 );
			}
			else
			{
				style.FlexDirection = (FlexDirection)rng.Between( 0, 3 );
				if ( rng.Roll( 40 ) ) style.Width = RandomLength( rng, 40, 400, 20, 20 );
				if ( rng.Roll( 40 ) ) style.Height = RandomLength( rng, 40, 300, 20, 20 );
				if ( rng.Roll( 30 ) ) style.FlexWrap = rng.Roll( 80 ) ? Wrap.Wrap : Wrap.WrapReverse;
				if ( rng.Roll( 30 ) ) style.SetGap( rng.Roll( 50 ) ? Gutter.All : Gutter.Column, StyleLength.Points( rng.Between( 1, 8 ) ) );
				if ( rng.Roll( 50 ) ) style.JustifyContent = (Justify)rng.Between( 0, 5 );
				if ( rng.Roll( 50 ) ) style.AlignItems = (Align)rng.Between( 1, 5 );
				if ( rng.Roll( 30 ) ) style.AlignContent = (Align)rng.Between( 1, 8 );
				if ( rng.Roll( 20 ) ) style.Overflow = (Overflow)rng.Between( 0, 2 );
			}

			if ( rng.Roll( 40 ) ) style.FlexGrow = rng.Between( 0, 3 );
			if ( rng.Roll( 30 ) ) style.FlexShrink = rng.Between( 0, 2 );
			if ( rng.Roll( 15 ) ) style.FlexBasis = RandomLength( rng, 10, 200, 30, 10 );
			if ( rng.Roll( 20 ) ) style.AlignSelf = (Align)rng.Between( 0, 5 );
			if ( rng.Roll( 30 ) ) style.SetMargin( (Edge)rng.Between( 0, 8 ), RandomLength( rng, 0, 12, 10, 15 ) );
			if ( rng.Roll( 30 ) ) style.SetPadding( (Edge)rng.Between( 0, 8 ), RandomLength( rng, 0, 12, 10 ) );
			if ( rng.Roll( 15 ) ) style.SetBorder( (Edge)rng.Between( 0, 8 ), StyleLength.Points( rng.Between( 1, 4 ) ) );
			if ( rng.Roll( 15 ) ) style.MinWidth = RandomLength( rng, 10, 150 );
			if ( rng.Roll( 15 ) ) style.MaxWidth = RandomLength( rng, 60, 500 );
			if ( rng.Roll( 15 ) ) style.MinHeight = RandomLength( rng, 10, 100 );
			if ( rng.Roll( 15 ) ) style.MaxHeight = RandomLength( rng, 40, 400 );
			if ( rng.Roll( 8 ) ) style.AspectRatio = rng.Between( 1, 4 ) / 2f;
			if ( rng.Roll( 12 ) ) style.Display = Display.Block;
			if ( rng.Roll( 8 ) )
			{
				style.Display = Display.Grid;
				style.GridTemplateColumns = rng.Pick( "1fr 1fr", "100px auto", "repeat(2, 1fr) 40px", "auto 1fr", "minmax(30px, 1fr) 2fr", "repeat(auto-fill, 60px)" );
				if ( rng.Roll( 30 ) ) style.GridTemplateRows = rng.Pick( "40px auto", "1fr 1fr", "minmax(20px, auto)" );
				if ( rng.Roll( 25 ) ) style.GridAutoFlow = rng.Pick( "column", "row dense", "column dense" );
				if ( rng.Roll( 30 ) ) style.GridAutoRows = rng.Pick( "30px", "minmax(20px, auto)" );
			}

			if ( rng.Roll( 10 ) ) style.GridColumnStart = rng.Pick( "span 2", "1", "2", "-1" );
			if ( rng.Roll( 8 ) ) style.GridRowStart = rng.Pick( "span 2", "1", "2" );
			if ( rng.Roll( 10 ) ) style.JustifySelf = rng.Pick( Align.Start, Align.Center, Align.End, Align.Stretch );
			if ( rng.Roll( 5 ) ) style.Display = Display.None;

			if ( rng.Roll( 12 ) )
			{
				style.PositionType = PositionType.Absolute;
				if ( rng.Roll( 60 ) ) style.SetPosition( Edge.Left, RandomLength( rng, 0, 100 ) );
				if ( rng.Roll( 60 ) ) style.SetPosition( Edge.Top, RandomLength( rng, 0, 100 ) );
				if ( rng.Roll( 30 ) ) style.SetPosition( Edge.Right, RandomLength( rng, 0, 100 ) );
				if ( rng.Roll( 30 ) ) style.SetPosition( Edge.Bottom, RandomLength( rng, 0, 100 ) );
			}
			else if ( rng.Roll( 10 ) )
			{
				style.PositionType = PositionType.Relative;
				style.SetPosition( rng.Roll( 50 ) ? Edge.Left : Edge.Top, StyleLength.Points( rng.Between( -20, 20 ) ) );
			}

			parent.AddChild( node );
			if ( !leaf ) Fill( engine, rng, node, depth + 1, all, texts );
		}
	}
}
