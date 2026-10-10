using Editor;
using Rinten;
using Rinten.Internal;

namespace EngineTests;

//Enum dropdowns used from controls inside a menu.
[TestClass]
[DoNotParallelize]
public class EditorEnumControlMenuTests
{
	public enum SampleAxis
	{
		[Title( "X" ), Icon( "close" )]
		X,
		[Title( "Y" ), Icon( "expand_more" )]
		Y,
		[Title( "Z" ), Icon( "north" )]
		Z,
		[Title( "W" ), Icon( "west" )]
		W,
	}

	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;

		GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( typeof( EditorBackend ).Assembly, true );
		GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( typeof( EditorEnumControlMenuTests ).Assembly, true );
	}

	[TestMethod]
	public void ANarrowEnumFieldStillBuildsADropdownEditor()
	{
		var current = SampleAxis.X;

		var property = GlobalToolsNamespace.EditorTypeLibrary.CreateProperty<SampleAxis>( "Axis",
			() => current,
			v => current = v );

		var control = new EnumControlWidget( property );
		control.Size = new Vector2( 80, Theme.RowHeight );

		Assert.IsTrue( control.IsValid() );
		Assert.IsTrue( control.IsControlButton );

		control.Destroy();
	}

	public enum ViewLabel
	{
		[Title( "3D" ), Icon( "view_in_ar" )]
		ThreeD,
		[Title( "Top 2D" ), Icon( "roofing" )]
		Top2d,
	}

	public enum TrioMode
	{
		A,
		B,
		C,
	}

	sealed class TrioHolder
	{
		public TrioMode Mode { get; set; } = TrioMode.A;
	}

	sealed class DropdownTrioHolder
	{
		[EnumDropdown]
		public TrioMode Mode { get; set; } = TrioMode.A;
	}

	//The widget draws a dropdown whatever the enum's size - a small enum is laid out as a row of buttons by
	//the panel editor, Editor.Inspectors.EnumControl, which a widget sheet reaches through PanelControlWidget.
	[TestMethod]
	public void ASmallEnumBuildsTheSameDropdownEditor()
	{
		var target = new TrioHolder();
		var property = target.GetSerialized().GetProperty( nameof( TrioHolder.Mode ) );
		var control = new EnumControlWidget( property );

		Assert.IsNotNull( control.GetDescendants<Label>().FirstOrDefault( x => x.Text == "\u25BE" ) );

		control.Destroy();
	}

	[TestMethod]
	public void AFourValueEnumBuildsDropdownEditor()
	{
		var current = SampleAxis.X;

		var property = GlobalToolsNamespace.EditorTypeLibrary.CreateProperty<SampleAxis>( "Axis",
			() => current,
			v => current = v );

		var control = new EnumControlWidget( property );

		Assert.IsNotNull( control.GetDescendants<Label>().FirstOrDefault( x => x.Text == "\u25BE" ) );

		control.Destroy();
	}

	[TestMethod]
	public void EnumDropdownAttributeBuildsDropdownOnSmallEnum()
	{
		var target = new DropdownTrioHolder();
		var property = target.GetSerialized().GetProperty( nameof( DropdownTrioHolder.Mode ) );
		var control = new EnumControlWidget( property );

		Assert.IsNotNull( control.GetDescendants<Label>().FirstOrDefault( x => x.Text == "\u25BE" ) );

		control.Destroy();
	}

	[TestMethod]
	public void AClosedEnumFieldShowsItsTitleInALabel()
	{
		var current = ViewLabel.ThreeD;

		var property = GlobalToolsNamespace.EditorTypeLibrary.CreateProperty<ViewLabel>( "View",
			() => current,
			v => current = v );

		var control = new EnumControlWidget( property );
		control.Size = new Vector2( 160, Theme.RowHeight );
		control.ArrangeIfNeeded();

		var label = control.GetDescendants<Label>().FirstOrDefault( x => x.Text == "3D" );

		Assert.IsNotNull( label, "the closed field shows the enum title in a label, not a material icon" );

		var icon = control.GetDescendants<IconButton>().FirstOrDefault( x => x.Icon == "view_in_ar" );

		Assert.IsNotNull( icon, "the closed field shows the enum icon when one is set" );

		control.Destroy();
	}

	[TestMethod]
	public void ANarrowEnumFieldLeavesRoomForItsTitleNotJustTheIcon()
	{
		var current = ViewLabel.ThreeD;

		var property = GlobalToolsNamespace.EditorTypeLibrary.CreateProperty<ViewLabel>( "View",
			() => current,
			v => current = v );

		var control = new EnumControlWidget( property );

		var entry = GlobalToolsNamespace.EditorTypeLibrary.GetEnumDescription( typeof( ViewLabel ) ).GetEntry( (long)ViewLabel.ThreeD );
		var title = entry.Title ?? "Unset";

		const float arrow = 17.0f;
		const float pad = 16.0f;
		var wanted = Paint.MeasureText( title ).x + arrow + pad;

		Assert.IsTrue( control.MinimumWanted( false ) >= wanted,
			"the field asks for enough width to show the full title" );

		control.Destroy();
	}
}
