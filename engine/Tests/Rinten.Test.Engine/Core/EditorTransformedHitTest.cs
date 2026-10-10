using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

//Panels under a transformed parent are hit where drawn, e.g. zoomed node graph nodes
[TestClass]
[DoNotParallelize]
public class EditorTransformedHitTests
{
	private sealed class FakePointer : PanelInput
	{
		public InputData Data;

		internal override InputData GetInputData() => Data;

		public override void SetCursor( string name ) { }

		public void Point( Vector2 at, bool down )
		{
			Data.MousePos = at;
			Data.Mouse0 = down;
		}
	}

	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		Editor.EditorBackend.Panels = true;

		Rinten.Engine.GlobalContext.Current.UISystem ??= new UISystem();
	}

	private static void Frame( RootPanel root, FakePointer pointer )
	{
		root.TickInternal();

		root.PreLayout( new Rect( 0, 0, 400, 300 ) );
		root.CalculateLayout();
		root.PostLayout();

		root.TickInputInternal();
		pointer.Tick( new[] { root }, mouseIsActive: true );
	}

	//A box placed by hand, the way a graph places its nodes.
	private static Panel Box( Panel parent, string name, float left, float top, float width, float height )
	{
		var panel = parent.AddChild<Panel>( name );

		panel.Style.Position = PositionMode.Absolute;
		panel.Style.Left = Length.Pixels( left );
		panel.Style.Top = Length.Pixels( top );
		panel.Style.Width = Length.Pixels( width );
		panel.Style.Height = Length.Pixels( height );
		panel.Style.PointerEvents = PointerEvents.All;

		return panel;
	}

	//The world is slid by (20, 10) and halved about its corner, as a zoomed-out graph is. A node laid out at (200, 100)-(300, 160) is
	//therefore drawn at (120, 60)-(170, 90) - nowhere near where it was laid out, so a walk that ignores the transform cannot find it by luck.
	[TestMethod]
	public void APanelUnderATransformIsHitWhereItIsDrawn()
	{
		var root = new UiWindowRoot();
		var pointer = new FakePointer();

		var world = Box( root, "world", 0, 0, 400, 300 );
		world.Style.TransformOriginX = Length.Pixels( 0 );
		world.Style.TransformOriginY = Length.Pixels( 0 );

		var transform = new PanelTransform();
		transform.AddTranslate( Length.Pixels( 20 ), Length.Pixels( 10 ) );
		transform.AddScale( 0.5f );
		world.Style.Transform = transform;

		var node = Box( world, "node", 200, 100, 100, 60 );

		try
		{
			for ( var i = 0; i < 3; i++ ) Frame( root, pointer );

			Assert.AreEqual( new Rect( 200, 100, 100, 60 ), node.Box.Rect, "the node is laid out where it was put" );

			//Inside the node as drawn.
			pointer.Point( new Vector2( 145, 75 ), down: false );
			Frame( root, pointer );

			Assert.AreEqual( node, pointer.Hovered, "the pointer over the drawn node should find the node" );
			Assert.AreEqual( new Vector2( 50, 30 ), node.MousePosition, "and the node should read the pointer in its own laid-out space" );

			//Where the node was laid out but is no longer drawn.
			pointer.Point( new Vector2( 280, 150 ), down: false );
			Frame( root, pointer );

			Assert.AreNotEqual( node, pointer.Hovered, "the pointer over the node's laid-out place, now empty, should not find the node" );

			//And the two conversions agree with the drawing.
			Assert.AreEqual( new Vector2( 120, 60 ), node.PanelPositionToScreenPosition( Vector2.Zero ) );
			Assert.AreEqual( new Vector2( 50, 30 ), node.ScreenPositionToPanelPosition( new Vector2( 145, 75 ) ) );
		}
		finally
		{
			root.Delete();
		}
	}
}
