using Editor;
using Rinten;

namespace EngineTests;

//Drag targets are found by walking widgets, since the pressed widget holds the pointer
[TestClass]
[DoNotParallelize]
public class EditorDraggingTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;
	}

	//A widget of a given size, at a given place, inside another.
	private static Widget Box( Widget parent, float x, float y, float width, float height )
	{
		var widget = new Widget( parent );

		widget.Position = new Vector2( x, y );
		widget.Size = new Vector2( width, height );

		return widget;
	}

	[TestMethod]
	public void TheDeepestThingUnderThePointerIsTheOneFound()
	{
		var window = Box( null, 0, 0, 800, 600 );
		var panel = Box( window, 100, 100, 400, 300 );
		var row = Box( panel, 10, 10, 200, 40 );

		Assert.AreEqual( row, EditorDrag.Under( window, new Vector2( 120, 120 ) ) );
		Assert.AreEqual( panel, EditorDrag.Under( window, new Vector2( 120, 200 ) ) );
		Assert.AreEqual( window, EditorDrag.Under( window, new Vector2( 700, 500 ) ) );
	}

	[TestMethod]
	public void ThePointerOutsideEverythingFindsNothing()
	{
		var window = Box( null, 0, 0, 800, 600 );

		Assert.IsNull( EditorDrag.Under( window, new Vector2( 900, 100 ) ) );
		Assert.IsNull( EditorDrag.Under( window, new Vector2( -10, 100 ) ) );
	}

	[TestMethod]
	public void SomethingHiddenIsNotUnderThePointer()
	{
		var window = Box( null, 0, 0, 800, 600 );
		var panel = Box( window, 100, 100, 400, 300 );

		panel.Visible = false;

		Assert.AreEqual( window, EditorDrag.Under( window, new Vector2( 150, 150 ) ) );
	}

	[TestMethod]
	public void TheOneOnTopIsTheOneUnderThePointer()
	{
		//Last added is drawn last, which is to say on top - so that is the one the pointer is on where two of them overlap.
		var window = Box( null, 0, 0, 800, 600 );

		Box( window, 100, 100, 200, 200 );

		var above = Box( window, 150, 150, 200, 200 );

		Assert.AreEqual( above, EditorDrag.Under( window, new Vector2( 200, 200 ) ) );
	}

	[TestMethod]
	public void WhatIsBeingCarriedIsNamedByTheEndOfItsPath()
	{
		Assert.AreEqual( "crate.vmdl", EditorDrag.Name( new DragData { Text = "models/props/crate.vmdl" } ) );
		Assert.AreEqual( "crate.vmdl", EditorDrag.Name( new DragData { Text = @"C:\game\models\crate.vmdl" } ) );
	}

	[TestMethod]
	public void CarryingSeveralThingsSaysHowMany()
	{
		var data = new DragData { Text = "models/a.vmdl\nmodels/b.vmdl\nmodels/c.vmdl" };

		Assert.AreEqual( "a.vmdl +2", EditorDrag.Name( data ) );
	}

	[TestMethod]
	public void CarryingNothingInParticularStillHasAName()
	{
		Assert.AreEqual( "…", EditorDrag.Name( new DragData() ) );
		Assert.AreEqual( "…", EditorDrag.Name( null ) );
	}
}
