using Editor;
using Rinten;

namespace EngineTests;

//The real vector control out of the inspector: three fields in a row. The synthetic version of this shape lays out correctly, so this one is built out
//of the classes the inspector actually uses - a serialized property, the control the editor picks for it, and the layout that control makes for itself.
[TestClass]
[DoNotParallelize]
public class EditorVectorControlTests
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
		public Vector3 Position { get; set; }
	}

	[TestMethod]
	public void TheFieldsOfAVectorAreInOneRow()
	{
		var thing = new Thing();

		var serialized = thing.GetSerialized();

		Assert.IsNotNull( serialized, "no serialized object in a test host" );
		Assert.IsTrue( serialized.TryGetProperty( "Position", out var property ), "no Position on it" );

		//The control itself rather than whatever the type library would pick: a test host
		//has no editors registered, and what is being tested is how this one lays itself out.
		var control = new VectorControlWidget( property );

		System.Console.WriteLine( $"control is {control.GetType().Name}" );

		var holder = new Widget( null );
		holder.Size = new Vector2( 200, 40 );
		holder.Layout = Layout.Column();
		holder.Layout.Add( control );

		Arrange( holder );

		System.Console.WriteLine( $"control {control.Position} {control.Size}" );

		var fields = control.childWidgets.ToArray();

		foreach ( var field in fields )
			System.Console.WriteLine( $"  field {field.GetType().Name} {field.Position} {field.Size}" );

		Assert.IsTrue( fields.Length >= 3, $"a Vector3 has three fields, this has {fields.Length}" );

		//Side by side: each one starts further along than the last, and none of them starts below the first.
		for ( var i = 1; i < fields.Length; i++ )
		{
			Assert.IsTrue( fields[i].Position.x > fields[i - 1].Position.x,
				$"field {i} is at {fields[i].Position}, after {fields[i - 1].Position}" );

			Assert.AreEqual( fields[0].Position.y, fields[i].Position.y, 0.5f,
				$"field {i} is on a line of its own at y {fields[i].Position.y}" );
		}

		Assert.IsTrue( fields[^1].Position.x + fields[^1].Size.x <= control.Size.x + 0.5f,
			"the last field is off the end of the control" );
	}
}
