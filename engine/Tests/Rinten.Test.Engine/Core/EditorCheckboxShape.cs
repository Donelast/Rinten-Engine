using Editor;
using Rinten;

namespace EngineTests;

//Inspector checkboxes stay square despite the sheet letting controls stretch across the column
[TestClass]
[DoNotParallelize]
public class EditorCheckboxShapeTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	private static void Arrange( Widget widget )
	{
		widget.ArrangeIfNeeded();

		foreach ( var child in widget.childWidgets ) Arrange( child );
	}

	private class Thing
	{
		public bool Flag { get; set; }

		public string Words { get; set; } = "";
	}

	[TestMethod]
	public void ACheckboxStaysSquareInAWideRow()
	{
		var serialized = new Thing().GetSerialized();

		Assert.IsNotNull( serialized, "no serialized object in a test host" );
		Assert.IsTrue( serialized.TryGetProperty( "Flag", out var property ), "no Flag on it" );

		var control = new BoolControlWidget( property );

		//What a sheet row does to every control it holds.
		control.HorizontalSizeMode = SizeMode.Flexible;

		var row = new Widget( null );
		row.Size = new Vector2( 300, 40 );

		var grid = Layout.Grid();
		grid.HorizontalSpacing = 0;
		row.Layout = grid;

		grid.AddCell( 0, 0, control, alignment: TextFlag.LeftTop );

		Arrange( row );

		System.Console.WriteLine( $"checkbox {control.Position} {control.Size}" );

		Assert.AreEqual( Theme.RowHeight, control.Size.x, 0.5f,
			$"the checkbox is {control.Size.x} wide in a {row.Size.x} row - it is supposed to be a square" );

		Assert.AreEqual( Theme.RowHeight, control.Size.y, 0.5f,
			$"the checkbox is {control.Size.y} tall" );
	}

	//And the other way round: a field in the same cell fills it. Here so that the fix above cannot be "nothing stretches
	//any more", which would leave every text field in the inspector at whatever width its contents happened to want.
	[TestMethod]
	public void AFieldStillFillsTheRow()
	{
		var serialized = new Thing().GetSerialized();

		Assert.IsTrue( serialized.TryGetProperty( "Words", out var property ) );

		var control = ControlWidget.Create( property );

		if ( !control.IsValid() )
		{
			Assert.Inconclusive( "no editor for a string in a test host" );
			return;
		}

		control.HorizontalSizeMode = SizeMode.Flexible;

		var row = new Widget( null );
		row.Size = new Vector2( 300, 40 );

		var grid = Layout.Grid();
		grid.HorizontalSpacing = 0;
		row.Layout = grid;

		grid.AddCell( 0, 0, control, alignment: TextFlag.LeftTop );

		Arrange( row );

		System.Console.WriteLine( $"field {control.Position} {control.Size}" );

		Assert.IsTrue( control.Size.x > 200.0f,
			$"a text field only took {control.Size.x} of a {row.Size.x} row" );
	}
}
