using System;
using Rinten.Layout;

namespace LayoutTests.Harness;

//What a measure function is asked and what it answers, independent of which engine is asking.
internal delegate LayoutSize FixtureMeasureFunc( IFixtureNode node, float width, MeasureMode widthMode, float height, MeasureMode heightMode );

//A layout engine the fixtures can be run against. The fixtures and the differential tests are written against IFixtureNode
//only, so the same tree can be built in the production engine and in the frozen reference and the two compared.
internal interface IFixtureEngine
{
	string Name { get; }

	//A node with the library defaults the conformance fixtures were generated against.
	IFixtureNode CreateNode();

	//A node configured the way the panels configure theirs: web defaults, no pixel snapping.
	IFixtureNode CreateUiNode();
}

//The style inputs a fixture can set. Every setter marks the node dirty.
internal interface IFixtureStyle
{
	Direction Direction { get; set; }
	FlexDirection FlexDirection { get; set; }
	Justify JustifyContent { get; set; }
	Align AlignContent { get; set; }
	Align AlignItems { get; set; }
	Align AlignSelf { get; set; }
	PositionType PositionType { get; set; }
	Wrap FlexWrap { get; set; }
	Overflow Overflow { get; set; }
	Display Display { get; set; }
	BoxSizing BoxSizing { get; set; }

	//Grid, as strings where the engine parses them: track lists, lines and the flow.
	string GridTemplateColumns { get; set; }
	string GridTemplateRows { get; set; }
	string GridAutoColumns { get; set; }
	string GridAutoRows { get; set; }
	string GridAutoFlow { get; set; }
	string GridColumnStart { get; set; }
	string GridColumnEnd { get; set; }
	string GridRowStart { get; set; }
	string GridRowEnd { get; set; }
	Align JustifyItems { get; set; }
	Align JustifySelf { get; set; }

	float Flex { get; set; }
	float FlexGrow { get; set; }
	float FlexShrink { get; set; }
	float AspectRatio { get; set; }
	StyleLength FlexBasis { get; set; }

	StyleLength Width { get; set; }
	StyleLength Height { get; set; }
	StyleLength MinWidth { get; set; }
	StyleLength MinHeight { get; set; }
	StyleLength MaxWidth { get; set; }
	StyleLength MaxHeight { get; set; }

	void SetMargin( Edge edge, StyleLength value );
	void SetPadding( Edge edge, StyleLength value );
	void SetBorder( Edge edge, StyleLength value );
	void SetPosition( Edge edge, StyleLength value );
	void SetGap( Gutter gutter, StyleLength value );
}

//One node of a layout tree, as the fixtures see it.
internal interface IFixtureNode
{
	IFixtureEngine Engine { get; }
	IFixtureStyle Style { get; }

	//Whatever the fixture wants to hang off the node; text fixtures keep their string here.
	object Context { get; set; }

	FixtureMeasureFunc MeasureFunc { get; set; }
	bool HasMeasureFunc { get; }

	IFixtureNode Owner { get; }
	int ChildCount { get; }
	IFixtureNode GetChild( int index );
	void AddChild( IFixtureNode child );
	void InsertChild( IFixtureNode child, int index );
	bool RemoveChild( IFixtureNode child );
	void RemoveAllChildren();

	void CalculateLayout( float ownerWidth = float.NaN, float ownerHeight = float.NaN, Direction ownerDirection = Direction.LTR );

	//Marks the node dirty by hand, as a control does when the text it measures changed.
	void MarkDirty();

	bool IsDirty { get; }
	bool HasNewLayout { get; set; }

	float LayoutLeft { get; }
	float LayoutTop { get; }
	float LayoutWidth { get; }
	float LayoutHeight { get; }
	bool LayoutHadOverflow { get; }
	float LayoutMargin( PhysicalEdge edge );
	float LayoutPadding( PhysicalEdge edge );
	float LayoutBorder( PhysicalEdge edge );
}

//Counters the harness keeps while a layout runs. Per thread, because the unit
//tests run in parallel and a layout never leaves the thread that asked for it.
internal static class FixtureCounters
{
	[ThreadStatic] private static int measureCalls;

	//How many times any measure function was invoked on this thread since the last reset.
	public static int MeasureCalls
	{
		get => measureCalls;
		set => measureCalls = value;
	}

	public static void CountMeasure() => measureCalls++;
}

//The engines a test can pick.
internal static class FixtureEngines
{
	public static readonly IFixtureEngine Production = new EngineNode.Factory();
	public static readonly IFixtureEngine Reference = new ReferenceNode.Factory();

	public static readonly IFixtureEngine[] All = { Production, Reference };
}
