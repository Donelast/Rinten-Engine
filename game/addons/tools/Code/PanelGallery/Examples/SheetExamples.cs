using Rinten.UI;
using PanelSheet = Editor.Inspectors.ControlSheet;

namespace Editor.Gallery;

//The panel ControlSheet: rows, groups, feature tabs, collections, several objects at once.
static class SheetExamples
{
	const string Page = "Sheets";

	static PanelSheet Sheet( Panel into, object target )
	{
		var sheet = into.AddChild<PanelSheet>();
		sheet.Target = EditorTypeLibrary.GetSerializedObject( target );
		sheet.Rebuild();
		return sheet;
	}

	//A sheet over an object with one property of every type; each row must work
	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Every property type" ), Icon( "view_agenda" )]
	static Panel EveryType()
	{
		var example = Example.Column();
		Sheet( example, new SampleModel() );
		return example;
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Groups and feature tabs" ), Icon( "folder" )]
	[Description( "[Group] folds rows under a header; [Feature] moves them to a tab strip at the top of the sheet." )]
	static Panel Groups()
	{
		var example = Example.Column();
		Sheet( example, new SampleGroupedModel() );
		return example;
	}

	[PanelGallery, Category( Page ), Order( 2 )]
	[Title( "Row attributes" ), Icon( "rule" )]
	[Description( "What a row reads off its property: [Range], [ReadOnly], [Title], [Description], [TextArea], and a nullable." )]
	static Panel Attributes()
	{
		var example = Example.Column();
		Sheet( example, new SampleAttributesModel() );
		return example;
	}

	[PanelGallery, Category( Page ), Order( 3 )]
	[Title( "Collections" ), Icon( "data_array" )]
	[Description( "Lists, dictionaries, sets and arrays: add, remove, reorder, and a list of objects with inline sheets." )]
	static Panel Collections()
	{
		var example = Example.Column();
		Sheet( example, new SampleCollectionsModel() );
		return example;
	}

	[PanelGallery, Category( Page ), Order( 4 )]
	[Title( "Several objects at once" ), Icon( "dynamic_feed" )]
	[Description( "A MultiSerializedObject over two objects that disagree: rows that differ show the mixed state, and an edit writes to both." )]
	static Panel MultiEdit()
	{
		var example = Example.Column();

		var first = new SampleAttributesModel();
		var second = new SampleAttributesModel { Percent = 90, Titled = 9.5f, ReadOnlyText = "Differs" };

		var multi = new MultiSerializedObject();
		multi.Add( EditorTypeLibrary.GetSerializedObject( first ) );
		multi.Add( EditorTypeLibrary.GetSerializedObject( second ) );
		multi.Rebuild();

		var sheet = example.AddChild<PanelSheet>();
		sheet.Target = multi;
		sheet.Rebuild();

		Example.Note( example, "Percent and the titled float differ between the two; the rest agree." );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 5 )]
	[Title( "Rows from expressions" ), Icon( "functions" )]
	[Description( "Rows over static properties and over loose values, with no object behind them - what the preferences pages do." )]
	static Panel Expressions()
	{
		var example = Example.Column();

		var sheet = example.AddChild<PanelSheet>();
		sheet.AddProperty( () => SampleSettings.Amount );
		sheet.AddProperty( () => SampleSettings.Enabled );
		sheet.AddProperty( () => SampleSettings.Tint );
		sheet.AddPropertyRow( SampleProperty.Create( 3.5f ) );
		sheet.AddPropertyRow( SampleProperty.Create( new Vector3( 1, 2, 3 ) ) );
		sheet.AddPropertyRow( SampleProperty.Create( "Loose text" ) );

		return example;
	}
}
