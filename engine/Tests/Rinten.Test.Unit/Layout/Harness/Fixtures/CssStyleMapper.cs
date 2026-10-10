using System;
using System.Collections.Generic;
using System.Globalization;
using Rinten.Layout;

namespace LayoutTests.Harness.Fixtures;

//Maps the CSS subset the fixtures use onto IFixtureStyle. Unknown properties throw, so a
//fixture cannot silently ask the browser for something the engine was never told about.
internal static class CssStyleMapper
{
	public static void Apply( IFixtureStyle style, Dictionary<string, string> css )
	{
		foreach ( var (property, value) in css )
		{
			switch ( property )
			{
				case "width": style.Width = Length( value ); break;
				case "height": style.Height = Length( value ); break;
				case "min-width": style.MinWidth = Length( value ); break;
				case "min-height": style.MinHeight = Length( value ); break;
				case "max-width": style.MaxWidth = Length( value ); break;
				case "max-height": style.MaxHeight = Length( value ); break;

				case "margin": style.SetMargin( Edge.All, Length( value ) ); break;
				case "margin-left": style.SetMargin( Edge.Left, Length( value ) ); break;
				case "margin-right": style.SetMargin( Edge.Right, Length( value ) ); break;
				case "margin-top": style.SetMargin( Edge.Top, Length( value ) ); break;
				case "margin-bottom": style.SetMargin( Edge.Bottom, Length( value ) ); break;

				case "padding": style.SetPadding( Edge.All, Length( value ) ); break;
				case "padding-left": style.SetPadding( Edge.Left, Length( value ) ); break;
				case "padding-right": style.SetPadding( Edge.Right, Length( value ) ); break;
				case "padding-top": style.SetPadding( Edge.Top, Length( value ) ); break;
				case "padding-bottom": style.SetPadding( Edge.Bottom, Length( value ) ); break;

				case "border-width": style.SetBorder( Edge.All, Length( value ) ); break;
				case "border-left-width": style.SetBorder( Edge.Left, Length( value ) ); break;
				case "border-right-width": style.SetBorder( Edge.Right, Length( value ) ); break;
				case "border-top-width": style.SetBorder( Edge.Top, Length( value ) ); break;
				case "border-bottom-width": style.SetBorder( Edge.Bottom, Length( value ) ); break;

				case "left": style.SetPosition( Edge.Left, Length( value ) ); break;
				case "right": style.SetPosition( Edge.Right, Length( value ) ); break;
				case "top": style.SetPosition( Edge.Top, Length( value ) ); break;
				case "bottom": style.SetPosition( Edge.Bottom, Length( value ) ); break;

				case "position":
					style.PositionType = value switch
					{
						"static" => PositionType.Static,
						"relative" => PositionType.Relative,
						"absolute" => PositionType.Absolute,
						"fixed" => PositionType.Fixed,
						_ => throw Unknown( property, value ),
					};
					break;

				case "display":
					style.Display = value switch
					{
						"flex" => Display.Flex,
						"none" => Display.None,
						"contents" => Display.Contents,
						"block" => Display.Block,
						"grid" => Display.Grid,
						_ => throw Unknown( property, value ),
					};
					break;

				case "direction":
					style.Direction = value switch { "ltr" => Direction.LTR, "rtl" => Direction.RTL, _ => throw Unknown( property, value ) };
					break;

				case "flex-direction":
					style.FlexDirection = value switch
					{
						"row" => FlexDirection.Row,
						"row-reverse" => FlexDirection.RowReverse,
						"column" => FlexDirection.Column,
						"column-reverse" => FlexDirection.ColumnReverse,
						_ => throw Unknown( property, value ),
					};
					break;

				case "flex-wrap":
					style.FlexWrap = value switch { "nowrap" => Wrap.NoWrap, "wrap" => Wrap.Wrap, "wrap-reverse" => Wrap.WrapReverse, _ => throw Unknown( property, value ) };
					break;

				case "justify-content":
					style.JustifyContent = value switch
					{
						"flex-start" => Justify.FlexStart,
						"center" => Justify.Center,
						"flex-end" => Justify.FlexEnd,
						"space-between" => Justify.SpaceBetween,
						"space-around" => Justify.SpaceAround,
						"space-evenly" => Justify.SpaceEvenly,
						"start" => Justify.Start,
						"end" => Justify.End,
						"stretch" => Justify.Stretch,
						_ => throw Unknown( property, value ),
					};
					break;

				case "align-items": style.AlignItems = Align( property, value ); break;
				case "align-self": style.AlignSelf = Align( property, value ); break;
				case "align-content": style.AlignContent = Align( property, value ); break;

				case "grid-template-columns": style.GridTemplateColumns = value; break;
				case "grid-template-rows": style.GridTemplateRows = value; break;
				case "grid-auto-columns": style.GridAutoColumns = value; break;
				case "grid-auto-rows": style.GridAutoRows = value; break;
				case "grid-auto-flow": style.GridAutoFlow = value; break;
				case "grid-column-start": style.GridColumnStart = value; break;
				case "grid-column-end": style.GridColumnEnd = value; break;
				case "grid-row-start": style.GridRowStart = value; break;
				case "grid-row-end": style.GridRowEnd = value; break;
				case "grid-column": { var parts = value.Split( '/' ); style.GridColumnStart = parts[0].Trim(); style.GridColumnEnd = parts.Length > 1 ? parts[1].Trim() : "auto"; break; }
				case "grid-row": { var parts = value.Split( '/' ); style.GridRowStart = parts[0].Trim(); style.GridRowEnd = parts.Length > 1 ? parts[1].Trim() : "auto"; break; }
				case "justify-items": style.JustifyItems = GridAlign( property, value ); break;
				case "justify-self": style.JustifySelf = GridAlign( property, value ); break;

				case "flex-grow": style.FlexGrow = Number( value ); break;
				case "flex-shrink": style.FlexShrink = Number( value ); break;
				case "flex-basis": style.FlexBasis = Length( value ); break;

				case "gap": style.SetGap( Gutter.All, Length( value ) ); break;
				case "row-gap": style.SetGap( Gutter.Row, Length( value ) ); break;
				case "column-gap": style.SetGap( Gutter.Column, Length( value ) ); break;

				case "overflow":
					style.Overflow = value switch { "visible" => Overflow.Visible, "hidden" => Overflow.Hidden, "scroll" => Overflow.Scroll, _ => throw Unknown( property, value ) };
					break;

				case "aspect-ratio":
					{
						var parts = value.Split( '/', StringSplitOptions.TrimEntries );
						style.AspectRatio = parts.Length == 2 ? Number( parts[0] ) / Number( parts[1] ) : Number( parts[0] );
						break;
					}

				case "box-sizing":
					style.BoxSizing = value switch { "border-box" => BoxSizing.BorderBox, "content-box" => BoxSizing.ContentBox, _ => throw Unknown( property, value ) };
					break;

				default:
					throw new NotSupportedException( $"The fixture mapper does not know '{property}'." );
			}
		}
	}

	private static Rinten.Layout.Align Align( string property, string value ) => value switch
	{
		"start" => Rinten.Layout.Align.Start,
		"end" => Rinten.Layout.Align.End,
		"auto" => Rinten.Layout.Align.Auto,
		"flex-start" => Rinten.Layout.Align.FlexStart,
		"center" => Rinten.Layout.Align.Center,
		"flex-end" => Rinten.Layout.Align.FlexEnd,
		"stretch" => Rinten.Layout.Align.Stretch,
		"baseline" => Rinten.Layout.Align.Baseline,
		"space-between" => Rinten.Layout.Align.SpaceBetween,
		"space-around" => Rinten.Layout.Align.SpaceAround,
		"space-evenly" => Rinten.Layout.Align.SpaceEvenly,
		_ => throw Unknown( property, value ),
	};

	//The alignment keywords a grid item takes along either axis; start and end are the grid's own, the flex ones read the same.
	private static Rinten.Layout.Align GridAlign( string property, string value ) => value switch
	{
		"auto" => Rinten.Layout.Align.Auto,
		"start" or "self-start" => Rinten.Layout.Align.Start,
		"end" or "self-end" => Rinten.Layout.Align.End,
		"flex-start" => Rinten.Layout.Align.FlexStart,
		"flex-end" => Rinten.Layout.Align.FlexEnd,
		"center" => Rinten.Layout.Align.Center,
		"stretch" or "normal" => Rinten.Layout.Align.Stretch,
		_ => throw Unknown( property, value ),
	};

	private static NotSupportedException Unknown( string property, string value ) => new( $"The fixture mapper does not know '{property}: {value}'." );

	private static float Number( string value ) => float.Parse( value, CultureInfo.InvariantCulture );

	public static StyleLength Length( string value )
	{
		if ( value == "auto" ) return StyleLength.Auto;
		if ( value.EndsWith( "px", StringComparison.Ordinal ) ) return StyleLength.Points( Number( value[..^2] ) );
		if ( value.EndsWith( '%' ) ) return StyleLength.Percent( Number( value[..^1] ) );
		return StyleLength.Points( Number( value ) );
	}
}
