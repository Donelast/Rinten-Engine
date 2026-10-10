namespace Editor;

//The camera helpers measure the drag against the thing the scene is drawn in and put a cursor on it. That used to be a widget and can now be a panel, so they ask for this instead of for either.
/// <summary>The picture a scene camera is flown around in.</summary>
public interface ISceneCanvas
{
	/// <summary>Whether there is still something here to draw in.</summary>
	bool IsCanvasValid { get; }

	/// <summary>How big the picture is, in editor pixels.</summary>
	Vector2 CanvasSize { get; }

	/// <summary>Where the picture is on the desktop, in editor pixels.</summary>
	Vector2 CanvasScreenPosition { get; }

	/// <summary>Gives the keyboard to the picture, so flying it keeps the keys.</summary>
	void FocusCanvas();

	/// <summary>The pointer's shape over the picture.</summary>
	void SetCanvasCursor( CursorShape shape );

	/// <summary>A picture of a pointer, for the shapes the window system has no name for.</summary>
	void SetCanvasCursor( Pixmap pixmap );
}
