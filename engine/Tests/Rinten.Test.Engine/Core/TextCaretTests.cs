using Editor;
using Rinten;
using Rinten.Rendering;
using Rinten.UI;
using Rinten.UI.Construct;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class TextCaretTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;
	}

	private static RootPanel LayoutRoot( Panel child, int width = 400, int height = 120 )
	{
		var root = new UiWindowRoot();
		child.Parent = root;

		root.TickInternal();
		root.PreLayout( new Rect( 0, 0, width, height ) );
		root.CalculateLayout();
		root.PostLayout();

		return root;
	}

	private static Rinten.UI.Label TextLabel( TextField field ) =>
		field.Children.FirstOrDefault( c => c is Rinten.UI.Label l && l.HasClass( "text" ) ) as Rinten.UI.Label;

	[TestMethod]
	public void TextCaretDrawReadsHostCaretIndex()
	{
		var host = new CaretHostPanel();
		LayoutRoot( host );

		Assert.IsTrue( TextCaretDraw.TryGet( host.Label, out var index, out var color ) );
		Assert.AreEqual( 2, index );
		Assert.AreEqual( Color.Red, color );
	}

	[TestMethod]
	public void TextCaretDrawRectMatchesLabelCaretGeometry()
	{
		var host = new CaretHostPanel();
		LayoutRoot( host );

		var fromDraw = TextCaretDraw.GetRect( host.Label, 2 );
		var fromLabel = host.Label.GetCaretRect( 2 );
		fromLabel.Left = MathX.FloorToInt( fromLabel.Left );
		fromLabel.Width = 1;

		Assert.AreEqual( fromLabel, fromDraw );
	}

	private static void Relayout( RootPanel root, int width = 400, int height = 120 )
	{
		root.TickInternal();
		root.PreLayout( new Rect( 0, 0, width, height ) );
		root.CalculateLayout();
		root.PostLayout();
	}

	private static void Type( RootPanel root, TextField field, string text )
	{
		foreach ( var letter in text )
			field.OnKeyTyped( letter );

		Relayout( root );
	}

	[TestMethod]
	public void GetLetterAtPointerFindsStartOfText()
	{
		var field = new TextField();
		var root = LayoutRoot( field );

		Type( root, field, "abcde" );

		var label = TextLabel( field );
		Assert.IsNotNull( label );

		var start = label.GetCaretRect( 0 );
		var pointer = new Vector2( start.Left + 1, start.Top + start.Height * 0.5f );

		Assert.AreEqual( 0, label.GetLetterAtPointer( pointer ) );
	}

	[TestMethod]
	public void GetLetterAtPointerFindsEndOfText()
	{
		var field = new TextField();
		var root = LayoutRoot( field );

		Type( root, field, "abcde" );

		var label = TextLabel( field );
		Assert.IsNotNull( label );

		var afterLast = label.GetCaretRect( label.TextLength - 1 );
		var pointer = new Vector2( afterLast.Right + 4, afterLast.Top + afterLast.Height * 0.5f );

		Assert.AreEqual( label.TextLength, label.GetLetterAtPointer( pointer ) );
	}

	[TestMethod]
	public void MouseDownUsesEventPositionNotStaleRootMousePos()
	{
		var field = new TextField();
		var root = LayoutRoot( field );

		Type( root, field, "abcde" );

		var label = TextLabel( field );
		Assert.IsNotNull( label );

		var afterLast = label.GetCaretRect( label.TextLength - 1 );
		var pointer = new Vector2( afterLast.Right + 4, afterLast.Top + afterLast.Height * 0.5f );
		var clickInField = field.ScreenPositionToPanelPosition( pointer );

		field.CreateEvent( new MousePanelEvent( "onmousedown", field, "mouseleft" )
		{
			LocalPosition = clickInField,
			PointerPosition = pointer,
		} );

		root.MousePos = Vector2.Zero;

		root.TickInternal();

		Assert.AreEqual( label.TextLength, label.CaretPosition );
	}

	[TestMethod]
	public void GetLetterAtPointerWorksWhenLabelIsNotAtOrigin()
	{
		var wrapper = new Panel();
		wrapper.Style.Margin = Length.Pixels( 48 );

		var root = LayoutRoot( wrapper );
		var field = new TextField();
		field.Parent = wrapper;

		Relayout( root );

		Type( root, field, "abcde" );

		var label = TextLabel( field );
		Assert.IsNotNull( label );

		var caret = label.GetCaretRect( 2 );
		var pointer = new Vector2( caret.Left + 1, caret.Top + caret.Height * 0.5f );

		Assert.AreEqual( 2, label.GetLetterAtPointer( pointer ) );
	}

	[TestMethod]
	public void GetLetterAtPointerFindsMiddleOfText()
	{
		var field = new TextField();
		var root = LayoutRoot( field );

		Type( root, field, "abcde" );

		var label = TextLabel( field );
		Assert.IsNotNull( label );

		var caret = label.GetCaretRect( 2 );
		var pointer = new Vector2( caret.Left + 1, caret.Top + caret.Height * 0.5f );

		Assert.AreEqual( 2, label.GetLetterAtPointer( pointer ) );
	}

	[TestMethod]
	public void GetLetterAtPointerWorksInsideScrolledParent()
	{
		var scroll = new Panel();
		scroll.Style.Width = Length.Pixels( 400 );
		scroll.Style.Height = Length.Pixels( 48 );
		scroll.Style.OverflowY = OverflowMode.Scroll;

		var root = LayoutRoot( scroll );

		var spacer = new Panel { Parent = scroll };
		spacer.Style.Height = Length.Pixels( 100 );

		var field = new TextField { Parent = scroll };

		Relayout( root );

		scroll.ScrollOffset = new Vector2( 0, 80 );
		Relayout( root );

		Type( root, field, "abcde" );

		var label = TextLabel( field );
		Assert.IsNotNull( label );

		var caret = label.GetCaretRect( 3 );
		var pointer = new Vector2( caret.Left + 1, caret.Top + caret.Height * 0.5f );

		Assert.AreEqual( 3, label.GetLetterAtPointer( pointer ) );
	}

	[TestMethod]
	public void RunPendingEventsDeepHandlesMouseDownWithoutTick()
	{
		var field = new TextField();
		var root = LayoutRoot( field );

		Type( root, field, "abcde" );

		var label = TextLabel( field );
		Assert.IsNotNull( label );

		label.CaretPosition = 0;

		var afterLast = label.GetCaretRect( label.TextLength - 1 );
		var pointer = new Vector2( afterLast.Right + 4, afterLast.Top + afterLast.Height * 0.5f );

		field.CreateEvent( new MousePanelEvent( "onmousedown", field, "mouseleft" )
		{
			LocalPosition = field.ScreenPositionToPanelPosition( pointer ),
			PointerPosition = pointer,
		} );

		root.RunPendingEventsDeep();

		Assert.AreEqual( label.TextLength, label.CaretPosition );
	}

	[TestMethod]
	public void MousePanelEventCapturesPointerPositionAtConstruction()
	{
		var field = new TextField();
		var root = LayoutRoot( field );

		var pointer = new Vector2( 222, 88 );
		root.MousePos = pointer;

		var evt = new MousePanelEvent( "onmousedown", field, "mouseleft" );

		Assert.AreEqual( pointer, evt.PointerPosition );
	}

	[TestMethod]
	public void MousePanelEventLocalPositionIncludesScrollOffset()
	{
		var field = new TextField();
		var root = LayoutRoot( field );

		field.ScrollOffset = new Vector2( 12, 18 );

		var pointer = new Vector2( field.Box.Rect.Left + 20, field.Box.Rect.Top + 10 );
		root.MousePos = pointer;

		var evt = new MousePanelEvent( "onmousedown", field, "mouseleft" );

		Assert.AreEqual( field.MousePosition + field.ScrollOffset, evt.LocalPosition );
	}

	private sealed class CaretHostPanel : Panel, ITextCaretHost
	{
		public Rinten.UI.Label Label { get; }

		public CaretHostPanel()
		{
			Label = Add.Label( "abc", "content-label" );
		}

		public bool ShouldDrawCaret( Rinten.UI.Label label ) => label == Label;

		public int GetCaretIndex( Rinten.UI.Label label ) => 2;

		public Color GetCaretColor( Rinten.UI.Label label ) => Color.Red;
	}
}
