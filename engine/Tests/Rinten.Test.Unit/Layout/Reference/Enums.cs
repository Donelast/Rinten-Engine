//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
namespace Rinten.Layout.Reference;

//Flexbox enums numbered as in Yoga to keep the CoreYoga mapping; unused values kept for switches
internal enum Align
{
	Auto,
	FlexStart,
	Center,
	FlexEnd,
	Stretch,
	Baseline,
	SpaceBetween,
	SpaceAround,
	SpaceEvenly,
	Start,
	End,
}

internal enum Justify
{
	FlexStart,
	Center,
	FlexEnd,
	SpaceBetween,
	SpaceAround,
	SpaceEvenly,
	Stretch,
	Start,
	End,
}

internal enum FlexDirection
{
	Column,
	ColumnReverse,
	Row,
	RowReverse,
}

internal enum Direction
{
	Inherit,
	LTR,
	RTL,
}

internal enum PositionType
{
	Static,
	Relative,
	Absolute,

	/// <summary>Out of the flow like absolute, but placed against the root of the tree rather than the nearest positioned ancestor.</summary>
	Fixed,
}

internal enum Wrap
{
	NoWrap,
	Wrap,
	WrapReverse,
}

internal enum Overflow
{
	Visible,
	Hidden,
	Scroll,
}

internal enum Display
{
	Flex,
	None,
	Contents,

	/// <summary>A box whose in-flow children stack down it; see BlockLayout.</summary>
	Block,
	Grid,
}

internal enum BoxSizing
{
	BorderBox,
	ContentBox,
}

internal enum Dimension
{
	Width,
	Height,
}

//The nine an author can name, including the shorthands.
internal enum Edge
{
	Left,
	Top,
	Right,
	Bottom,
	Start,
	End,
	Horizontal,
	Vertical,
	All,
}

//The four an edge resolves to once the writing direction is known.
internal enum PhysicalEdge
{
	Left = Edge.Left,
	Top = Edge.Top,
	Right = Edge.Right,
	Bottom = Edge.Bottom,
}

internal enum Gutter
{
	Column,
	Row,
	All,
}

internal enum Unit
{
	Undefined,
	Point,
	Percent,
	Auto,
	MaxContent,
	FitContent,
	Stretch,
}

//What a measure function is told about the space it has.
internal enum MeasureMode
{
	Undefined,
	Exactly,
	AtMost,
}

//The CSS automatic box sizes, which is what the algorithm reasons in; MeasureMode is the same three as a measure function sees them.
internal enum SizingMode
{
	/// <summary>Fill the available space, if that space is definite.</summary>
	StretchFit,

	/// <summary>The size the box would take with infinite space to take.</summary>
	MaxContent,

	/// <summary>Clamped between the two, when the space is definite.</summary>
	FitContent,
}

internal enum NodeType
{
	Default,
	Text,
}
