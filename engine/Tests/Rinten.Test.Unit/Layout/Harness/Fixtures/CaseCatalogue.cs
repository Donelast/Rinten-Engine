using System;
using System.Collections.Generic;
using static LayoutTests.Harness.Fixtures.Dsl;

namespace LayoutTests.Harness.Fixtures;

//Browser fixtures generated from parameter-grid families; must match committed browser-cases.json
internal static class CaseCatalogue
{
	private static readonly string[] Directions = { "row", "row-reverse", "column", "column-reverse" };
	private static readonly string[] Justifies = { "flex-start", "center", "flex-end", "space-between", "space-around", "space-evenly" };
	private static readonly string[] AlignItems = { "flex-start", "center", "flex-end", "stretch", "baseline" };
	private static readonly string[] AlignContents = { "flex-start", "center", "flex-end", "stretch", "space-between", "space-around", "space-evenly" };

	public static List<FixtureCase> All()
	{
		var cases = new List<FixtureCase>();
		foreach ( var family in Families() )
		{
			foreach ( var c in family ) cases.Add( c );
		}

		var names = new HashSet<string>( StringComparer.Ordinal );
		foreach ( var c in cases )
		{
			if ( !names.Add( c.Name ) ) throw new InvalidOperationException( $"Duplicate fixture name {c.Name}" );
		}

		return cases;
	}

	private static IEnumerable<IEnumerable<FixtureCase>> Families()
	{
		yield return Sizes();
		yield return Percentages();
		yield return Margins();
		yield return PaddingAndBorder();
		yield return DirectionAndJustify();
		yield return DirectionAndAlign();
		yield return AlignSelf();
		yield return WrapAndAlignContent();
		yield return GrowAndShrink();
		yield return Basis();
		yield return MinMax();
		yield return Gaps();
		yield return Absolute();
		yield return Fixed();
		yield return Relative();
		yield return OverflowCases();
		yield return DisplayCases();
		yield return Nested();
		yield return AspectRatio();
		yield return BoxSizingCases();
		yield return Block();
		yield return Grid();
	}

	//Block containers: children stack, fill the width unless told otherwise, and their vertical margins collapse as CSS 2 8.3.1
	//has them. The page renders a block root as flow-root, since the engine treats every root as starting a formatting context.
	private static IEnumerable<FixtureCase> Block()
	{
		foreach ( var c in BothDirections( "block_stack", () => Root( "display:block", Item(), Item( "height:40px" ), N( "height:20px" ) ) ) ) yield return c;
		yield return Case( "block_auto_width_fills", Root( "display:block;padding:10px", N( "height:20px" ), N( "height:20px;margin-left:15px;margin-right:15px" ), N( "height:20px;padding:5px;border-width:2px" ) ) );
		yield return Case( "block_auto_margins", Root( "display:block", N( "width:100px;height:20px;margin-left:auto;margin-right:auto" ), N( "width:100px;height:20px;margin-left:auto" ), N( "width:100px;height:20px;margin-right:auto" ) ) );
		foreach ( var c in BothDirections( "block_over_constrained", () => Root( "display:block", N( "width:200px;height:20px;margin-left:50px;margin-right:100px" ) ) ) ) yield return c;
		yield return Case( "block_margin_collapse_siblings", Root( "display:block", N( "height:20px;margin-bottom:30px" ), N( "height:20px;margin-top:20px" ), N( "height:20px;margin-top:-10px;margin-bottom:-5px" ), N( "height:20px;margin-top:-20px;margin-bottom:10px" ), N( "height:20px;margin-top:25px" ) ) );
		yield return Case( "block_margin_collapse_through_empty", Root( "display:block", N( "height:20px;margin-bottom:10px" ), N( "display:block;margin-top:30px;margin-bottom:5px" ), N( "height:20px;margin-top:15px" ) ) );
		yield return Case( "block_margin_collapse_parent_top", Root( "display:block", N( "display:block;margin-top:10px", N( "height:20px;margin-top:25px" ) ), N( "height:20px" ) ) );
		yield return Case( "block_parent_padding_stops_collapse", Root( "display:block", N( "display:block;padding-top:1px;margin-top:10px", N( "height:20px;margin-top:25px" ) ) ) );
		yield return Case( "block_bottom_margin_escapes", Root( "display:block", N( "display:block", N( "height:20px;margin-bottom:30px" ) ), N( "height:20px;margin-top:10px" ) ) );
		yield return Case( "block_nested_chain_escapes", Root( "display:block", N( "display:block", N( "display:block", N( "height:20px;margin-top:40px" ) ) ), N( "height:20px" ) ) );
		yield return Case( "block_in_flex_root_holds_margins", Root( "", N( "display:block;width:100px", N( "height:20px;margin-top:15px;margin-bottom:5px" ) ) ) );
		yield return Case( "block_overflow_hidden_holds_margins", Root( "display:block", N( "display:block;overflow:hidden", N( "height:20px;margin-top:15px" ) ), N( "height:20px" ) ) );
		yield return Case( "block_flex_child_stretches", Root( "display:block", N( "padding:5px", Item(), Item() ), N( "flex-direction:column", Item() ) ) );
		yield return Case( "block_percent_child", Root( "display:block;padding:10px", N( "width:50%;height:25%" ), N( "height:20px;margin-left:10%" ) ) );
		yield return Case( "block_percent_height_in_auto_parent", Root( "display:block", N( "display:block", N( "height:50%" ), N( "height:20px" ) ) ) );
		yield return Case( "block_absolute_static_position", Root( "display:block;padding:10px", N( "height:20px" ), Item( "position:absolute" ), N( "height:20px;margin-top:5px" ), Item( "position:absolute;right:0" ) ) );
		yield return Case( "block_shrink_to_fit_in_row", Root( "align-items:flex-start", N( "display:block", Item(), N( "width:80px;height:10px;margin-left:5px;margin-right:5px" ) ), Item() ) );
		yield return Case( "block_shrink_to_fit_in_column", Root( "flex-direction:column;align-items:flex-start", N( "display:block", Item( "margin-left:10px;margin-right:10px" ), N( "width:80px;height:10px" ) ) ) );
		yield return Case( "block_min_max_width_child", Root( "display:block", N( "height:20px;max-width:100px" ), N( "height:20px;min-width:350px" ), N( "width:50px;height:20px;min-width:80px" ) ) );
		yield return Case( "block_relative_child", Root( "display:block", N( "height:20px;position:relative;left:10px;top:5px" ), N( "height:20px" ) ) );
		yield return Case( "block_baseline_in_row", Root( "align-items:baseline", N( "display:block", N( "height:30px" ), N( "height:10px" ) ), Item( "height:50px" ) ) );
		yield return Case( "block_contents_child", Root( "display:block", N( "display:contents", N( "height:20px" ), N( "height:20px;margin-top:10px" ) ) ) );
		yield return Case( "block_gap_ignored", Root( "display:block;gap:10px", N( "height:20px" ), N( "height:20px" ) ) );
		yield return Case( "block_definite_height_overflows", Root( "display:block;height:50px", N( "height:40px" ), N( "height:40px" ) ) );
		yield return Case( "block_child_of_block_root_padding", Root( "display:block;padding:20px;border-width:3px", N( "display:block;margin:10px", N( "height:20px;margin-top:5px" ) ) ) );
	}

	//Grid containers: tracks of every kind, placement by line and by cursor, and alignment
	//in the areas. No line names and no text; min-content tracks wait for inline layout.
	private static IEnumerable<FixtureCase> Grid()
	{
		foreach ( var c in BothDirections( "grid_fixed_tracks", () => Root( "display:grid;grid-template-columns:100px 50px;grid-template-rows:40px 60px", N( "" ), N( "" ), N( "" ), N( "" ) ) ) ) yield return c;
		yield return Case( "grid_fr_tracks", Root( "display:grid;grid-template-columns:1fr 2fr", N( "" ), N( "" ) ) );
		yield return Case( "grid_fixed_fr_auto", Root( "display:grid;grid-template-columns:100px 1fr auto", N( "" ), N( "" ), Item() ) );
		yield return Case( "grid_percent_tracks", Root( "display:grid;grid-template-columns:50% 25%;grid-template-rows:50%", N( "" ), N( "" ) ) );
		yield return Case( "grid_gaps", Root( "display:grid;grid-template-columns:1fr 1fr;grid-template-rows:40px 40px;row-gap:10px;column-gap:20px", N( "" ), N( "" ), N( "" ), N( "" ) ) );
		yield return Case( "grid_repeat", Root( "display:grid;grid-template-columns:repeat(3, 1fr);grid-template-rows:50px", N( "" ), N( "" ), N( "" ) ) );
		yield return Case( "grid_auto_fill", Root( "display:grid;grid-template-columns:repeat(auto-fill, 80px)", Item(), Item(), Item(), Item(), Item() ) );
		yield return Case( "grid_auto_fit_collapses", Root( "display:grid;grid-template-columns:repeat(auto-fit, minmax(80px, 1fr));grid-template-rows:40px", N( "" ), N( "" ) ) );
		yield return Case( "grid_minmax", Root( "display:grid;grid-template-columns:minmax(50px, 1fr) 100px;grid-template-rows:minmax(20px, 50px)", N( "" ), N( "" ) ) );
		yield return Case( "grid_auto_track_from_content", Root( "display:grid;grid-template-columns:auto 1fr;grid-template-rows:auto", Item( "width:70px" ), N( "" ), Item( "height:45px" ), N( "" ) ) );
		yield return Case( "grid_max_content_track", Root( "display:grid;grid-template-columns:max-content 1fr;grid-template-rows:40px", N( "", Item(), Item() ), N( "" ) ) );
		yield return Case( "grid_explicit_placement", Root( "display:grid;grid-template-columns:100px 100px 100px;grid-template-rows:60px 60px 60px",
			N( "grid-column:2;grid-row:1" ), N( "grid-column:1 / 3;grid-row:2" ), N( "grid-column:3;grid-row:1 / 4" ), N( "grid-column:span 2;grid-row:3" ) ) );
		yield return Case( "grid_negative_lines", Root( "display:grid;grid-template-columns:100px 100px 100px;grid-template-rows:50px 50px", N( "grid-column:1 / -1" ), N( "grid-column:-2" ), N( "" ) ) );
		yield return Case( "grid_sparse_placement", Root( "display:grid;grid-template-columns:100px 100px 100px;grid-auto-rows:40px", N( "grid-column:span 2" ), N( "grid-column:span 2" ), N( "" ), N( "" ), N( "grid-column:span 3" ) ) );
		yield return Case( "grid_dense_placement", Root( "display:grid;grid-template-columns:100px 100px 100px;grid-auto-rows:40px;grid-auto-flow:row dense", N( "grid-column:span 2" ), N( "grid-column:span 2" ), N( "" ), N( "" ), N( "grid-column:span 3" ) ) );
		yield return Case( "grid_column_flow", Root( "display:grid;grid-template-rows:50px 50px;grid-auto-columns:60px;grid-auto-flow:column", N( "" ), N( "" ), N( "" ), N( "" ), N( "" ) ) );
		yield return Case( "grid_column_flow_dense", Root( "display:grid;grid-template-rows:50px 50px 50px;grid-auto-columns:60px;grid-auto-flow:column dense", N( "grid-row:span 2" ), N( "grid-row:span 2" ), N( "" ), N( "" ) ) );
		yield return Case( "grid_implicit_rows", Root( "display:grid;grid-template-columns:1fr 1fr;grid-auto-rows:40px", N( "" ), N( "" ), N( "" ), N( "" ), N( "" ) ) );
		yield return Case( "grid_implicit_rows_minmax", Root( "display:grid;grid-template-columns:1fr;grid-auto-rows:minmax(40px, auto)", N( "height:60px" ), N( "" ), N( "height:20px" ) ) );
		yield return Case( "grid_implicit_columns_from_placement", Root( "display:grid;grid-template-columns:100px;grid-template-rows:50px", N( "" ), Item( "grid-column:3" ) ) );
		yield return Case( "grid_locked_row_auto_column", Root( "display:grid;grid-template-columns:100px 100px 100px;grid-template-rows:50px 50px", N( "grid-row:2" ), N( "grid-row:2" ), N( "" ), N( "grid-row:2" ) ) );
		yield return Case( "grid_justify_items", Root( "display:grid;grid-template-columns:100px 100px;grid-template-rows:60px;justify-items:center", Item(), Item( "justify-self:end" ) ) );
		yield return Case( "grid_align_items", Root( "display:grid;grid-template-columns:100px 100px 100px;grid-template-rows:60px;align-items:start", Item(), Item( "align-self:center" ), Item( "align-self:end" ) ) );
		yield return Case( "grid_stretch_with_definite_size", Root( "display:grid;grid-template-columns:100px;grid-template-rows:60px", Item() ) );
		yield return Case( "grid_justify_content_center", Root( "display:grid;grid-template-columns:100px 100px;grid-template-rows:50px;justify-content:center", N( "" ), N( "" ) ) );
		yield return Case( "grid_justify_content_space_between", Root( "display:grid;grid-template-columns:100px 100px;grid-template-rows:50px;justify-content:space-between", N( "" ), N( "" ) ) );
		yield return Case( "grid_justify_content_space_around", Root( "display:grid;grid-template-columns:50px 50px 50px;grid-template-rows:50px;justify-content:space-around", N( "" ), N( "" ), N( "" ) ) );
		yield return Case( "grid_justify_content_end", Root( "display:grid;grid-template-columns:100px 100px;grid-template-rows:50px;justify-content:end", N( "" ), N( "" ) ) );
		yield return Case( "grid_justify_content_start_no_stretch", Root( "display:grid;grid-template-columns:100px auto;grid-template-rows:50px;justify-content:start", N( "" ), Item() ) );
		yield return Case( "grid_align_content_space_between", Root( "display:grid;grid-template-columns:1fr;grid-template-rows:50px 50px;align-content:space-between", N( "" ), N( "" ) ) );
		yield return Case( "grid_align_content_end", Root( "display:grid;grid-template-columns:1fr;grid-template-rows:50px 50px;align-content:flex-end", N( "" ), N( "" ) ) );
		yield return Case( "grid_item_margins", Root( "display:grid;grid-template-columns:100px 100px;grid-template-rows:60px", N( "margin:10px" ), N( "width:40px;margin-left:auto;margin-right:auto" ) ) );
		yield return Case( "grid_percent_child", Root( "display:grid;grid-template-columns:100px;grid-template-rows:60px", N( "width:50%;height:50%" ) ) );
		yield return Case( "grid_in_flex_row", Root( "align-items:flex-start", N( "display:grid;grid-template-columns:100px 1fr;grid-template-rows:20px", N( "" ), N( "width:30px" ) ), Item() ) );
		yield return Case( "grid_in_block", Root( "display:block", N( "display:grid;grid-template-columns:1fr 1fr;grid-template-rows:30px", N( "" ), N( "" ) ), N( "height:20px" ) ) );
		yield return Case( "grid_nested_grid", Root( "display:grid;grid-template-columns:1fr 1fr;grid-template-rows:100px", N( "display:grid;grid-template-columns:1fr 1fr;grid-template-rows:1fr", N( "" ), N( "" ) ), N( "" ) ) );
		yield return Case( "grid_absolute_child", Root( "display:grid;grid-template-columns:100px 100px;grid-template-rows:50px;padding:10px", N( "" ), Item( "position:absolute;right:0;bottom:0" ), Item( "position:absolute" ) ) );
		yield return Case( "grid_root_auto_height", N( "width:300px;display:grid;grid-template-columns:1fr 1fr;grid-auto-rows:40px", N( "" ), N( "" ), N( "" ) ) );
		yield return Case( "grid_padding_border", Root( "display:grid;grid-template-columns:1fr 1fr;grid-template-rows:1fr;padding:10px;border-width:5px", N( "" ), N( "" ) ) );
		yield return Case( "grid_span_both_axes", Root( "display:grid;grid-template-columns:repeat(3, 1fr);grid-template-rows:repeat(3, 50px)", N( "grid-column:1 / 3;grid-row:1 / 3" ), N( "" ), N( "" ), N( "" ), N( "" ), N( "" ) ) );
		yield return Case( "grid_fr_with_min_width_item", Root( "display:grid;grid-template-columns:1fr 1fr;grid-template-rows:40px", N( "min-width:250px" ), N( "" ) ) );
		yield return Case( "grid_overflowing_fixed_tracks", Root( "display:grid;grid-template-columns:200px 200px;grid-template-rows:50px", N( "" ), N( "" ) ) );
	}

	//-- helpers ---------------------------------------------------------------------------------

	private static FixtureCase Case( string name, CaseNode root ) => new() { Name = name, Root = root };

	//A 300 x 200 root; the owner is 400 x 400.
	private static CaseNode Root( string css = "", params CaseNode[] children ) => N( "width:300px;height:200px;" + css, children );

	private static CaseNode Item( string css = "" ) => N( "width:50px;height:30px;" + css );

	private static CaseNode[] Items( int count, string css = "" )
	{
		var items = new CaseNode[count];
		for ( var i = 0; i < count; i++ ) items[i] = Item( css );
		return items;
	}

	//The case, and the same case under right-to-left writing.
	private static IEnumerable<FixtureCase> BothDirections( string name, Func<CaseNode> build )
	{
		yield return Case( name, build() );
		yield return Case( name + "_rtl", build().With( "direction:rtl" ) );
	}

	//-- families --------------------------------------------------------------------------------

	private static IEnumerable<FixtureCase> Sizes()
	{
		yield return Case( "size_fixed_items", Root( "", Item(), Item( "width:80px;height:60px" ), Item( "width:20px;height:10px" ) ) );
		yield return Case( "size_auto_width_item_in_row", Root( "", N( "height:30px" ), Item() ) );
		yield return Case( "size_auto_height_item_in_row", Root( "", N( "width:40px" ), Item() ) );
		yield return Case( "size_auto_item_in_column", Root( "flex-direction:column", N( "" ), Item() ) );
		yield return Case( "size_auto_item_in_column_with_align_start", Root( "flex-direction:column;align-items:flex-start", N( "" ), Item() ) );
		yield return Case( "size_root_auto_height", N( "width:300px", Item(), Item( "height:70px" ) ) );
		yield return Case( "size_root_auto_width_column", N( "height:200px;flex-direction:column", Item(), Item( "width:90px" ) ) );
		yield return Case( "size_root_percent_of_owner", N( "width:50%;height:25%", Item() ) );
		yield return Case( "size_zero_items", Root( "", N( "width:0;height:0" ), Item() ) );
		yield return Case( "size_fractional", Root( "width:300.5px;height:200.25px", Item( "width:33.3px;height:10.7px" ), Item( "width:0.5px;height:0.5px" ) ) );
		yield return Case( "size_container_smaller_than_items", Root( "width:60px;height:20px", Item( "flex-shrink:0" ), Item( "flex-shrink:0" ) ) );
	}

	private static IEnumerable<FixtureCase> Percentages()
	{
		yield return Case( "percent_width_height", Root( "", Item( "width:50%;height:50%" ), Item( "width:25%" ) ) );
		yield return Case( "percent_in_column", Root( "flex-direction:column", Item( "width:50%;height:50%" ), Item( "height:25%" ) ) );
		yield return Case( "percent_of_padded_parent", Root( "padding:20px", Item( "width:50%;height:50%" ) ) );
		yield return Case( "percent_of_bordered_parent", Root( "border-width:10px", Item( "width:50%;height:50%" ) ) );
		yield return Case( "percent_nested", Root( "", N( "width:50%;height:50%", N( "width:50%;height:50%", N( "width:50%;height:50%" ) ) ) ) );
		yield return Case( "percent_margin", Root( "", Item( "margin-left:10%;margin-top:10%" ), Item( "margin:5%" ) ) );
		yield return Case( "percent_padding", Root( "", N( "padding:10%;width:50%;height:50%", Item() ) ) );
		yield return Case( "percent_inside_auto_parent", Root( "", N( "padding:5px", Item( "width:50%" ), Item() ) ) );
		yield return Case( "percent_height_inside_auto_height_parent", Root( "flex-direction:column;align-items:flex-start", N( "width:100px", Item( "height:50%" ) ) ) );
		yield return Case( "percent_min_max", Root( "", Item( "width:90%;max-width:50%" ), Item( "width:5%;min-width:20%" ) ) );
		yield return Case( "percent_flex_basis", Root( "", Item( "flex-basis:50%" ), Item( "flex-basis:25%;flex-grow:1" ) ) );
		yield return Case( "percent_over_100", Root( "", Item( "width:150%;flex-shrink:0" ) ) );
	}

	private static IEnumerable<FixtureCase> Margins()
	{
		yield return Case( "margin_each_side", Root( "", Item( "margin-left:10px;margin-top:20px;margin-right:30px;margin-bottom:5px" ), Item() ) );
		yield return Case( "margin_shorthand", Root( "", Item( "margin:12px" ), Item( "margin:3px" ) ) );
		yield return Case( "margin_negative", Root( "", Item( "margin-left:-10px;margin-top:-5px" ), Item( "margin-left:-20px" ) ) );
		yield return Case( "margin_in_column", Root( "flex-direction:column", Item( "margin:10px" ), Item( "margin-top:25px" ) ) );
		foreach ( var c in BothDirections( "margin_auto_main_start", () => Root( "", Item( "margin-left:auto" ), Item() ) ) ) yield return c;
		foreach ( var c in BothDirections( "margin_auto_main_both", () => Root( "", Item( "margin-left:auto;margin-right:auto" ), Item() ) ) ) yield return c;
		foreach ( var c in BothDirections( "margin_auto_on_two_items", () => Root( "", Item( "margin-right:auto" ), Item( "margin-left:auto" ), Item() ) ) ) yield return c;
		yield return Case( "margin_auto_cross", Root( "", Item( "margin-top:auto" ), Item( "margin-top:auto;margin-bottom:auto" ), Item() ) );
		yield return Case( "margin_auto_cross_overrides_stretch", Root( "", N( "width:50px;margin-top:auto" ), Item() ) );
		yield return Case( "margin_auto_column", Root( "flex-direction:column", Item( "margin-top:auto" ), Item( "margin-left:auto" ) ) );
		yield return Case( "margin_auto_with_justify_center", Root( "justify-content:center", Item( "margin-left:auto" ), Item() ) );
		yield return Case( "margin_auto_no_free_space", Root( "width:100px", Item( "margin-left:auto;flex-shrink:0" ), Item( "flex-shrink:0" ) ) );
		yield return Case( "margin_with_grow", Root( "", Item( "margin:10px;flex-grow:1" ), Item( "flex-grow:1" ) ) );
	}

	private static IEnumerable<FixtureCase> PaddingAndBorder()
	{
		yield return Case( "padding_container", Root( "padding:15px", Item(), Item() ) );
		yield return Case( "padding_container_sides", Root( "padding-left:5px;padding-top:10px;padding-right:15px;padding-bottom:20px", Item(), Item() ) );
		yield return Case( "border_container", Root( "border-width:8px", Item(), Item() ) );
		yield return Case( "border_container_sides", Root( "border-left-width:2px;border-top-width:4px;border-right-width:6px;border-bottom-width:8px", Item(), Item() ) );
		yield return Case( "padding_and_border_container", Root( "padding:10px;border-width:5px", Item( "flex-grow:1" ), Item() ) );
		yield return Case( "padding_item_with_grow", Root( "", Item( "padding:10px;flex-grow:1" ), Item( "border-width:3px;flex-grow:1" ) ) );
		yield return Case( "padding_larger_than_item", Root( "", N( "width:20px;height:20px;padding:30px" ), Item() ) );
		yield return Case( "padding_auto_sized_container", Root( "", N( "padding:12px;border-width:2px", Item(), Item() ) ) );
		yield return Case( "padding_column_container", Root( "flex-direction:column;padding:20px;border-width:5px", Item( "flex-grow:1" ), Item() ) );
		foreach ( var c in BothDirections( "padding_asymmetric_rtl", () => Root( "padding-left:30px;padding-right:5px;border-left-width:4px", Item(), Item() ) ) ) yield return c;
	}

	private static IEnumerable<FixtureCase> DirectionAndJustify()
	{
		foreach ( var direction in Directions )
		{
			foreach ( var justify in Justifies )
			{
				var name = $"justify_{direction}_{justify}";
				foreach ( var c in BothDirections( name, () => Root( $"flex-direction:{direction};justify-content:{justify}", Items( 3 ) ) ) ) yield return c;
			}

			yield return Case( $"justify_{direction}_overflow_space_between", Root( $"flex-direction:{direction};justify-content:space-between", Items( 3, "width:150px;height:120px;flex-shrink:0" ) ) );
			yield return Case( $"justify_{direction}_overflow_space_around", Root( $"flex-direction:{direction};justify-content:space-around", Items( 3, "width:150px;height:120px;flex-shrink:0" ) ) );
			yield return Case( $"justify_{direction}_overflow_center", Root( $"flex-direction:{direction};justify-content:center", Items( 3, "width:150px;height:120px;flex-shrink:0" ) ) );
			yield return Case( $"justify_{direction}_single_item", Root( $"flex-direction:{direction};justify-content:space-around", Item() ) );
		}
	}

	private static IEnumerable<FixtureCase> DirectionAndAlign()
	{
		foreach ( var direction in Directions )
		{
			foreach ( var align in AlignItems )
			{
				var name = $"align_items_{direction}_{align}";
				foreach ( var c in BothDirections( name, () => Root( $"flex-direction:{direction};align-items:{align}", Item(), Item( "width:70px;height:50px" ), N( "width:30px" ), N( "height:20px" ) ) ) ) yield return c;
			}

			yield return Case( $"align_items_{direction}_stretch_with_margins", Root( $"flex-direction:{direction};align-items:stretch", N( "width:40px;margin:10px" ), N( "height:40px;margin-top:5px;margin-bottom:5px" ) ) );
		}

		yield return Case( "align_items_baseline_with_margins", Root( "align-items:baseline", Item( "margin-top:20px" ), Item( "height:60px;margin-top:5px" ), Item( "height:15px" ) ) );
		yield return Case( "align_items_baseline_nested", Root( "align-items:baseline", N( "padding:10px", Item( "height:40px" ) ), Item(), N( "flex-direction:column", Item( "height:10px" ), Item( "height:50px" ) ) ) );
		yield return Case( "align_items_baseline_wrap", Root( "align-items:baseline;flex-wrap:wrap", Items( 5, "width:100px;height:40px" ) ) );
	}

	private static IEnumerable<FixtureCase> AlignSelf()
	{
		foreach ( var direction in new[] { "row", "column" } )
		{
			yield return Case( $"align_self_{direction}", Root( $"flex-direction:{direction};align-items:center",
				Item( "align-self:flex-start" ), Item( "align-self:center" ), Item( "align-self:flex-end" ), N( "align-self:stretch;width:20px;height:20px" ), Item( "align-self:auto" ), Item( "align-self:baseline" ) ) );
			yield return Case( $"align_self_stretch_auto_size_{direction}", Root( $"flex-direction:{direction};align-items:flex-start", N( "align-self:stretch" ), Item() ) );
		}

		yield return Case( "align_self_with_auto_margin", Root( "align-items:flex-end", Item( "align-self:center;margin-top:auto" ), Item( "align-self:flex-start" ) ) );
	}

	private static IEnumerable<FixtureCase> WrapAndAlignContent()
	{
		foreach ( var wrap in new[] { "wrap", "wrap-reverse" } )
		{
			foreach ( var alignContent in AlignContents )
			{
				var name = $"wrap_{wrap}_align_content_{alignContent}";
				yield return Case( name, Root( $"flex-wrap:{wrap};align-content:{alignContent}", Items( 5, "width:100px;height:40px" ) ) );
				yield return Case( name + "_column", Root( $"flex-direction:column;flex-wrap:{wrap};align-content:{alignContent}", Items( 7, "width:40px;height:60px" ) ) );
			}

			yield return Case( $"wrap_{wrap}_stretch_items", Root( $"flex-wrap:{wrap}", N( "width:100px" ), N( "width:120px" ), N( "width:150px" ), N( "width:60px" ) ) );
			yield return Case( $"wrap_{wrap}_with_gap", Root( $"flex-wrap:{wrap};gap:10px", Items( 6, "width:90px;height:40px" ) ) );
			yield return Case( $"wrap_{wrap}_with_margins", Root( $"flex-wrap:{wrap}", Items( 5, "width:90px;height:40px;margin:5px" ) ) );
			yield return Case( $"wrap_{wrap}_grow_per_line", Root( $"flex-wrap:{wrap}", Item( "width:100px;flex-grow:1" ), Item( "width:100px;flex-grow:1" ), Item( "width:150px;flex-grow:1" ), Item( "width:100px;flex-grow:2" ) ) );
			yield return Case( $"wrap_{wrap}_auto_height_root", N( $"width:300px;flex-wrap:{wrap}", Items( 5, "width:100px;height:40px" ) ) );
			yield return Case( $"wrap_{wrap}_rtl", Root( $"flex-wrap:{wrap};direction:rtl", Items( 5, "width:100px;height:40px" ) ) );
		}

		yield return Case( "wrap_single_item_wider_than_line", Root( "flex-wrap:wrap", Item( "width:400px;height:40px" ), Item() ) );
		yield return Case( "wrap_min_height_lines", Root( "flex-wrap:wrap;align-content:flex-start", Items( 5, "width:100px;height:40px;min-height:60px" ) ) );
		yield return Case( "nowrap_overflow", Root( "flex-wrap:nowrap", Items( 5, "width:100px;height:40px;flex-shrink:0" ) ) );
	}

	private static IEnumerable<FixtureCase> GrowAndShrink()
	{
		yield return Case( "grow_single", Root( "", Item( "flex-grow:1" ) ) );
		yield return Case( "grow_equal", Root( "", Item( "flex-grow:1" ), Item( "flex-grow:1" ), Item( "flex-grow:1" ) ) );
		yield return Case( "grow_weighted", Root( "", Item( "flex-grow:1" ), Item( "flex-grow:2" ), Item( "flex-grow:3" ) ) );
		yield return Case( "grow_fractional", Root( "", Item( "flex-grow:0.25" ), Item( "flex-grow:0.5" ) ) );
		yield return Case( "grow_with_fixed_sibling", Root( "", Item( "flex-grow:1" ), Item(), Item( "flex-grow:1" ) ) );
		yield return Case( "grow_column", Root( "flex-direction:column", Item( "flex-grow:1" ), Item(), Item( "flex-grow:2" ) ) );
		yield return Case( "grow_with_max_width", Root( "", Item( "flex-grow:1;max-width:80px" ), Item( "flex-grow:1" ) ) );
		yield return Case( "grow_with_min_width", Root( "", Item( "flex-grow:1;min-width:200px" ), Item( "flex-grow:1" ) ) );
		yield return Case( "grow_zero_basis", Root( "", Item( "flex-grow:1;flex-basis:0" ), Item( "flex-grow:1;flex-basis:0;width:100px" ) ) );
		yield return Case( "shrink_equal", Root( "width:100px", Item( "width:100px" ), Item( "width:100px" ) ) );
		yield return Case( "shrink_weighted", Root( "width:100px", Item( "width:100px;flex-shrink:1" ), Item( "width:100px;flex-shrink:3" ) ) );
		yield return Case( "shrink_scaled_by_basis", Root( "width:150px", Item( "width:200px" ), Item( "width:50px" ) ) );
		yield return Case( "shrink_disabled_on_one", Root( "width:100px", Item( "width:100px;flex-shrink:0" ), Item( "width:100px" ) ) );
		yield return Case( "shrink_with_min_width", Root( "width:100px", Item( "width:100px;min-width:80px" ), Item( "width:100px" ) ) );
		yield return Case( "shrink_column", Root( "height:50px;flex-direction:column", Item( "height:60px" ), Item( "height:60px" ) ) );
		yield return Case( "shrink_below_zero_free_space_with_padding", Root( "width:100px", Item( "width:100px;padding:20px" ), Item( "width:100px" ) ) );
		yield return Case( "grow_shrink_mixed", Root( "width:250px", Item( "width:100px;flex-grow:1" ), Item( "width:200px;flex-shrink:2" ), Item( "flex-grow:1;flex-shrink:0" ) ) );
		yield return Case( "grow_nested", Root( "", N( "flex-grow:1", Item( "flex-grow:1" ), Item() ), N( "flex-grow:1;flex-direction:column", Item( "flex-grow:1" ) ) ) );
		yield return Case( "grow_auto_height_root", N( "width:300px;flex-direction:column", Item( "flex-grow:1" ), Item() ) );
	}

	private static IEnumerable<FixtureCase> Basis()
	{
		yield return Case( "basis_px", Root( "", Item( "flex-basis:100px" ), Item( "flex-basis:80px;width:20px" ) ) );
		yield return Case( "basis_auto_uses_width", Root( "", Item( "flex-basis:auto;width:120px" ), Item( "flex-basis:auto" ) ) );
		yield return Case( "basis_percent", Root( "", Item( "flex-basis:40%" ), Item( "flex-basis:30%" ) ) );
		yield return Case( "basis_zero_grow", Root( "", Item( "flex-basis:0;flex-grow:1" ), Item( "flex-basis:0;flex-grow:1" ), Item( "flex-basis:0;flex-grow:2" ) ) );
		yield return Case( "basis_column", Root( "flex-direction:column", Item( "flex-basis:90px" ), Item( "flex-basis:25%" ) ) );
		yield return Case( "basis_with_min_max", Root( "", Item( "flex-basis:300px;max-width:100px" ), Item( "flex-basis:10px;min-width:60px" ) ) );
		yield return Case( "basis_larger_than_container_shrinks", Root( "", Item( "flex-basis:250px" ), Item( "flex-basis:250px" ) ) );
		yield return Case( "basis_content_of_auto_container", Root( "", N( "flex-basis:auto;padding:5px", Item(), Item() ), Item() ) );
		yield return Case( "basis_row_reverse", Root( "flex-direction:row-reverse", Item( "flex-basis:100px" ), Item( "flex-basis:50%" ) ) );
	}

	private static IEnumerable<FixtureCase> MinMax()
	{
		yield return Case( "minmax_min_width_wins_over_width", Root( "", Item( "width:20px;min-width:80px" ), Item( "height:10px;min-height:60px" ) ) );
		yield return Case( "minmax_max_width_wins_over_width", Root( "", Item( "width:200px;max-width:80px" ), Item( "height:100px;max-height:20px" ) ) );
		yield return Case( "minmax_min_over_max", Root( "", Item( "min-width:120px;max-width:60px" ), Item( "min-height:50px;max-height:20px" ) ) );
		yield return Case( "minmax_on_stretch", Root( "", N( "width:40px;max-height:100px" ), N( "width:40px;min-height:150px" ), N( "width:40px;max-height:50px;min-height:80px" ) ) );
		yield return Case( "minmax_on_grow", Root( "", Item( "flex-grow:1;max-width:60px" ), Item( "flex-grow:1;min-width:170px" ), Item( "flex-grow:1" ) ) );
		yield return Case( "minmax_on_shrink", Root( "width:200px", Item( "width:150px;min-width:120px" ), Item( "width:150px" ), Item( "width:150px;max-width:20px" ) ) );
		yield return Case( "minmax_on_container", N( "min-width:100px;max-width:150px;min-height:50px;max-height:80px", Item( "width:200px;height:100px;flex-shrink:0" ) ) );
		yield return Case( "minmax_percent_on_container", N( "width:50%;max-width:120px;height:80%;min-height:350px", Item() ) );
		yield return Case( "minmax_column_grow", Root( "flex-direction:column", Item( "flex-grow:1;max-height:40px" ), Item( "flex-grow:1;min-height:120px" ), Item( "flex-grow:1" ) ) );
		yield return Case( "minmax_auto_container_min_width", Root( "", N( "min-width:120px;padding:4px", Item() ), Item() ) );
		yield return Case( "minmax_auto_container_max_width", Root( "", N( "max-width:60px;padding:4px", Item(), Item() ), Item() ) );
		yield return Case( "minmax_max_width_zero", Root( "", Item( "max-width:0" ), Item() ) );
	}

	private static IEnumerable<FixtureCase> Gaps()
	{
		yield return Case( "gap_row", Root( "column-gap:10px", Items( 3 ) ) );
		yield return Case( "gap_row_ignores_row_gap", Root( "row-gap:10px", Items( 3 ) ) );
		yield return Case( "gap_column", Root( "flex-direction:column;row-gap:12px", Items( 3 ) ) );
		yield return Case( "gap_shorthand_wrap", Root( "flex-wrap:wrap;gap:8px", Items( 6, "width:90px;height:40px" ) ) );
		yield return Case( "gap_with_grow", Root( "column-gap:20px", Item( "flex-grow:1" ), Item( "flex-grow:1" ), Item( "flex-grow:1" ) ) );
		yield return Case( "gap_with_shrink", Root( "width:120px;column-gap:20px", Item( "width:100px" ), Item( "width:100px" ) ) );
		yield return Case( "gap_with_justify", Root( "column-gap:10px;justify-content:space-between", Items( 3 ) ) );
		yield return Case( "gap_percent", Root( "column-gap:10%;flex-wrap:wrap;row-gap:5%", Items( 5, "width:100px;height:40px" ) ) );
		yield return Case( "gap_single_item", Root( "gap:30px", Item() ) );
		yield return Case( "gap_with_none_child", Root( "column-gap:10px", Item(), Item( "display:none" ), Item() ) );
		yield return Case( "gap_reverse_directions", Root( "flex-direction:row-reverse;flex-wrap:wrap-reverse;gap:10px", Items( 5, "width:100px;height:40px" ) ) );
		yield return Case( "gap_auto_sized_container", Root( "", N( "gap:10px;padding:5px", Items( 3 ) ) ) );
	}

	private static IEnumerable<FixtureCase> Absolute()
	{
		foreach ( var c in BothDirections( "absolute_left_top", () => Root( "", Item( "position:absolute;left:10px;top:20px" ), Item() ) ) ) yield return c;
		foreach ( var c in BothDirections( "absolute_right_bottom", () => Root( "", Item( "position:absolute;right:10px;bottom:20px" ), Item() ) ) ) yield return c;
		foreach ( var c in BothDirections( "absolute_all_insets_stretch", () => Root( "", N( "position:absolute;left:10px;top:10px;right:20px;bottom:30px" ), Item() ) ) ) yield return c;
		foreach ( var c in BothDirections( "absolute_left_and_right_with_width", () => Root( "", Item( "position:absolute;left:10px;right:10px" ), Item() ) ) ) yield return c;
		yield return Case( "absolute_percent_insets", Root( "", Item( "position:absolute;left:10%;top:25%" ), Item( "position:absolute;right:10%;bottom:25%" ) ) );
		yield return Case( "absolute_percent_size", Root( "", N( "position:absolute;width:50%;height:25%;left:0;top:0" ) ) );
		yield return Case( "absolute_with_margins", Root( "", Item( "position:absolute;left:10px;top:10px;margin:15px" ), Item( "position:absolute;right:0;bottom:0;margin-right:5px;margin-bottom:8px" ) ) );
		yield return Case( "absolute_in_padded_parent", Root( "padding:20px", Item( "position:absolute;left:0;top:0" ), Item( "position:absolute;right:0;bottom:0" ), Item() ) );
		yield return Case( "absolute_in_bordered_parent", Root( "border-width:10px", Item( "position:absolute;left:0;top:0" ), Item( "position:absolute;right:0;bottom:0" ) ) );
		yield return Case( "absolute_no_insets_default", Root( "", Item(), Item( "position:absolute" ) ) );
		foreach ( var justify in Justifies )
		{
			foreach ( var c in BothDirections( $"absolute_no_insets_justify_{justify}", () => Root( $"justify-content:{justify}", Item( "position:absolute" ) ) ) ) yield return c;
		}

		foreach ( var align in new[] { "flex-start", "center", "flex-end", "stretch" } )
			yield return Case( $"absolute_no_insets_align_{align}", Root( $"align-items:{align}", Item( "position:absolute" ) ) );

		foreach ( var direction in Directions )
			yield return Case( $"absolute_no_insets_{direction}_center", Root( $"flex-direction:{direction};justify-content:center;align-items:center", Item( "position:absolute" ), Item() ) );

		yield return Case( "absolute_no_insets_in_padded_parent", Root( "padding:20px;justify-content:flex-end;align-items:flex-end", Item( "position:absolute" ) ) );
		yield return Case( "absolute_does_not_take_space", Root( "", Item( "position:absolute;left:0;top:0;flex-grow:1" ), Item( "flex-grow:1" ) ) );
		yield return Case( "absolute_inside_relative_child", Root( "", N( "position:relative;width:100px;height:100px;left:20px", Item( "position:absolute;right:0;bottom:0" ) ) ) );
		yield return Case( "absolute_containing_block_is_static_parent", Root( "padding:10px", N( "width:100px;height:100px;padding:5px", Item( "position:absolute;left:0;top:0" ) ) ) );
		yield return Case( "absolute_in_column_wrap", Root( "flex-direction:column;flex-wrap:wrap", Item( "position:absolute;right:0" ), Item( "height:80px" ), Item( "height:80px" ), Item( "height:80px" ), Item( "height:80px" ) ) );
		yield return Case( "absolute_auto_size_from_insets", Root( "", N( "position:absolute;left:20px;right:20px;top:30px;bottom:30px;min-width:100px;max-height:50px" ) ) );
		yield return Case( "absolute_aspect_ratio", Root( "", N( "position:absolute;left:0;top:0;width:100px;aspect-ratio:2/1" ) ) );
	}

	//Fixed boxes are placed against the root, whatever lies between. The page gives the
	//root a transform so a browser contains them there too, rather than in the viewport.
	private static IEnumerable<FixtureCase> Fixed()
	{
		foreach ( var c in BothDirections( "fixed_left_top", () => Root( "", Item( "position:fixed;left:10px;top:20px" ), Item() ) ) ) yield return c;
		foreach ( var c in BothDirections( "fixed_right_bottom", () => Root( "", Item( "position:fixed;right:10px;bottom:20px" ), Item() ) ) ) yield return c;
		yield return Case( "fixed_all_insets_stretch", Root( "", N( "position:fixed;left:10px;top:10px;right:20px;bottom:30px" ), Item() ) );
		yield return Case( "fixed_inside_relative_parent", Root( "padding:20px", N( "position:relative;margin:10px;padding:5px;width:100px;height:100px", Item( "position:fixed;left:10px;top:20px" ) ) ) );
		yield return Case( "fixed_inside_absolute_parent", Root( "", N( "position:absolute;left:100px;top:50px;width:100px;height:100px", Item( "position:fixed;left:0;top:0" ) ) ) );
		yield return Case( "fixed_inside_fixed_parent", Root( "", N( "position:fixed;left:40px;top:30px;width:100px;height:100px", Item( "position:fixed;right:0;bottom:0" ) ) ) );
		yield return Case( "fixed_no_insets_justified", Root( "", N( "padding:20px;justify-content:flex-end;align-items:center;width:200px;height:100px", Item( "position:fixed" ), Item() ) ) );
		yield return Case( "fixed_percent_size", Root( "", N( "position:relative;width:100px;height:100px", N( "position:fixed;width:50%;height:25%;left:0;top:0" ) ) ) );
		yield return Case( "fixed_with_margins", Root( "", Item( "position:fixed;left:10px;top:10px;margin:15px" ), Item( "position:fixed;right:0;bottom:0;margin-right:5px;margin-bottom:8px" ) ) );
		yield return Case( "fixed_deep_in_static_chain", Root( "padding:10px", N( "padding:10px", N( "padding:10px", Item( "position:fixed;left:5px;top:5px" ), Item() ) ) ) );
	}

	private static IEnumerable<FixtureCase> Relative()
	{
		foreach ( var c in BothDirections( "relative_left_top", () => Root( "", Item( "position:relative;left:10px;top:20px" ), Item() ) ) ) yield return c;
		foreach ( var c in BothDirections( "relative_right_bottom", () => Root( "", Item( "position:relative;right:10px;bottom:20px" ), Item() ) ) ) yield return c;
		foreach ( var c in BothDirections( "relative_left_and_right", () => Root( "", Item( "position:relative;left:10px;right:30px" ), Item() ) ) ) yield return c;
		yield return Case( "relative_percent", Root( "", Item( "position:relative;left:10%;top:10%" ), Item() ) );
		yield return Case( "relative_negative", Root( "", Item( "position:relative;left:-10px;top:-5px" ), Item() ) );
		yield return Case( "relative_keeps_flow", Root( "", Item( "position:relative;left:100px" ), Item(), Item() ) );
		yield return Case( "relative_in_column", Root( "flex-direction:column", Item( "position:relative;top:15px" ), Item() ) );
		yield return Case( "static_ignores_insets", Root( "", Item( "position:static;left:100px;top:100px" ), Item() ) );
	}

	private static IEnumerable<FixtureCase> OverflowCases()
	{
		foreach ( var overflow in new[] { "visible", "hidden", "scroll" } )
		{
			yield return Case( $"overflow_{overflow}_larger_child", Root( $"overflow:{overflow}", Item( "width:400px;height:300px;flex-shrink:0" ) ) );
			yield return Case( $"overflow_{overflow}_auto_container", Root( "", N( $"overflow:{overflow};padding:5px", Item(), Item() ) ) );
			yield return Case( $"overflow_{overflow}_shrinking_children", Root( $"overflow:{overflow};width:120px", Item( "width:100px" ), Item( "width:100px" ) ) );
			yield return Case( $"overflow_{overflow}_column_min_height", Root( $"overflow:{overflow};flex-direction:column;height:60px", Item( "min-height:50px" ), Item( "min-height:50px" ) ) );
			yield return Case( $"overflow_{overflow}_nested_stretch", Root( $"overflow:{overflow};flex-direction:column", N( $"overflow:{overflow};flex-grow:1", N( "width:40px", Item( "width:400px" ) ) ) ) );
		}
	}

	private static IEnumerable<FixtureCase> DisplayCases()
	{
		yield return Case( "display_none_first", Root( "", Item( "display:none" ), Item(), Item() ) );
		yield return Case( "display_none_middle_with_grow", Root( "", Item( "flex-grow:1" ), Item( "display:none;flex-grow:1" ), Item( "flex-grow:1" ) ) );
		yield return Case( "display_none_with_children", Root( "", N( "display:none", Item(), Item() ), Item() ) );
		yield return Case( "display_none_absolute", Root( "", Item( "display:none;position:absolute;left:10px" ), Item() ) );
		yield return Case( "display_none_in_wrap", Root( "flex-wrap:wrap", Item( "width:100px" ), Item( "width:100px;display:none" ), Item( "width:100px" ), Item( "width:100px" ) ) );
		yield return Case( "display_contents_flattens", Root( "", Item(), N( "display:contents", Item( "width:70px" ), Item() ), Item() ) );
		yield return Case( "display_contents_with_grow_children", Root( "", N( "display:contents", Item( "flex-grow:1" ), Item( "flex-grow:1" ) ), Item() ) );
		yield return Case( "display_contents_nested", Root( "", N( "display:contents", N( "display:contents", Item( "width:70px" ) ), Item() ), Item() ) );
		yield return Case( "display_contents_style_ignored", Root( "", N( "display:contents;width:200px;padding:20px;margin:20px", Item() ), Item() ) );
		yield return Case( "display_contents_in_column_stretch", Root( "flex-direction:column", N( "display:contents", N( "height:30px" ) ), Item() ) );
		yield return Case( "display_contents_empty", Root( "", Item(), N( "display:contents" ), Item() ) );
	}

	private static IEnumerable<FixtureCase> Nested()
	{
		yield return Case( "nested_auto_row_in_row", Root( "", N( "padding:5px", Item(), Item() ), Item() ) );
		yield return Case( "nested_auto_column_in_row", Root( "", N( "flex-direction:column;padding:5px", Item(), Item( "width:90px" ) ), Item() ) );
		yield return Case( "nested_auto_row_in_column", Root( "flex-direction:column", N( "padding:5px", Item(), Item() ), Item() ) );
		yield return Case( "nested_three_deep", Root( "", N( "padding:4px", N( "padding:4px;flex-direction:column", N( "padding:4px", Item(), Item() ), Item() ) ), Item() ) );
		yield return Case( "nested_auto_with_grow_inside", Root( "", N( "padding:5px", Item( "flex-grow:1" ), Item() ), Item() ) );
		yield return Case( "nested_grow_chain", Root( "", N( "flex-grow:1", N( "flex-grow:1;flex-direction:column", N( "flex-grow:1", Item( "flex-grow:1" ) ) ) ) ) );
		yield return Case( "nested_auto_container_shrinks", Root( "width:120px", N( "padding:5px", Item(), Item() ), N( "padding:5px", Item(), Item() ) ) );
		yield return Case( "nested_auto_container_shrink_disabled", Root( "width:120px", N( "padding:5px;flex-shrink:0", Item(), Item() ), N( "padding:5px", Item(), Item() ) ) );
		yield return Case( "nested_wrap_inside_auto", Root( "", N( "flex-wrap:wrap;width:120px;padding:5px", Items( 4 ) ), Item() ) );
		yield return Case( "nested_auto_height_root_column", N( "width:300px;flex-direction:column", N( "padding:5px", Item(), Item( "height:70px" ) ), N( "flex-direction:column", Item(), Item() ) ) );
		yield return Case( "nested_auto_width_root_row", N( "height:200px", N( "padding:5px", Item(), Item( "width:70px" ) ), N( "flex-direction:column", Item(), Item() ) ) );
		yield return Case( "nested_stretch_propagates", Root( "", N( "padding:5px", N( "width:20px", N( "width:10px" ) ) ) ) );
		yield return Case( "nested_percent_in_auto_chain", Root( "", N( "padding:5px", N( "width:50%;padding:5px", Item( "width:50%" ) ) ) ) );
		yield return Case( "nested_align_center_chain", Root( "align-items:center;justify-content:center", N( "align-items:center;justify-content:center;width:150px;height:100px", N( "align-items:center;justify-content:center;width:80px;height:60px", Item( "width:20px;height:20px" ) ) ) ) );
	}

	private static IEnumerable<FixtureCase> AspectRatio()
	{
		yield return Case( "aspect_width_given", Root( "align-items:flex-start", N( "width:100px;aspect-ratio:2/1" ), N( "width:60px;aspect-ratio:1/2" ) ) );
		yield return Case( "aspect_height_given", Root( "align-items:flex-start", N( "height:100px;aspect-ratio:2/1" ), N( "height:60px;aspect-ratio:1" ) ) );
		yield return Case( "aspect_from_stretch", Root( "", N( "aspect-ratio:2/1" ) ) );
		yield return Case( "aspect_column_stretch", Root( "flex-direction:column", N( "aspect-ratio:3/1" ) ) );
		yield return Case( "aspect_with_grow", Root( "align-items:flex-start", N( "flex-grow:1;aspect-ratio:2/1" ), Item() ) );
		yield return Case( "aspect_with_min_max", Root( "align-items:flex-start", N( "width:100px;aspect-ratio:1;max-height:40px" ), N( "width:20px;aspect-ratio:1;min-height:70px" ) ) );
		yield return Case( "aspect_percent_width", Root( "align-items:flex-start", N( "width:50%;aspect-ratio:3/2" ) ) );
		yield return Case( "aspect_both_given_wins", Root( "align-items:flex-start", N( "width:100px;height:20px;aspect-ratio:2/1" ) ) );
	}

	private static IEnumerable<FixtureCase> BoxSizingCases()
	{
		yield return Case( "boxsizing_content_box_item", Root( "align-items:flex-start", N( "box-sizing:content-box;width:50px;height:30px;padding:10px;border-width:5px" ), Item() ) );
		yield return Case( "boxsizing_content_box_container", Root( "box-sizing:content-box;padding:10px;border-width:5px", Item( "flex-grow:1" ) ) );
		yield return Case( "boxsizing_content_box_percent", Root( "", N( "box-sizing:content-box;width:50%;height:50%;padding:10px" ) ) );
		yield return Case( "boxsizing_content_box_min_max", Root( "align-items:flex-start", N( "box-sizing:content-box;min-width:50px;max-height:30px;padding:10px", Item() ) ) );
		yield return Case( "boxsizing_content_box_basis", Root( "", N( "box-sizing:content-box;flex-basis:50px;height:30px;padding:10px" ), Item() ) );
		yield return Case( "boxsizing_mixed_shrink", Root( "width:150px", N( "box-sizing:content-box;width:100px;height:30px;padding:10px" ), N( "width:100px;height:30px;padding:10px" ) ) );
	}
}
