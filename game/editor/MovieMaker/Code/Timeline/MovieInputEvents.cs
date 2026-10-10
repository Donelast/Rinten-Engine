using Rinten.UI;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>What the timeline hands an edit mode when the pointer or the keyboard does something.</summary>
public sealed class MovieMouseEvent
{
	/// <summary>Where the pointer is on the timeline.</summary>
	public required Vector2 ScenePosition { get; init; }

	/// <summary>Where the pointer is in the timeline panel.</summary>
	public required Vector2 LocalPosition { get; init; }

	/// <summary>The button this event is about, if any.</summary>
	public MouseButtons Button { get; init; }

	/// <summary>Every button currently held.</summary>
	public MouseButtons ButtonState { get; init; }

	public KeyboardModifiers Modifiers { get; init; }

	/// <summary>Set when something has dealt with this, so nothing else does.</summary>
	public bool Accepted { get; set; }

	public bool LeftMouseButton => Button == MouseButtons.Left;
	public bool RightMouseButton => Button == MouseButtons.Right;
	public bool MiddleMouseButton => Button == MouseButtons.Middle;

	public bool HasShift => (Modifiers & KeyboardModifiers.Shift) != 0;
	public bool HasCtrl => (Modifiers & KeyboardModifiers.Ctrl) != 0;
	public bool HasAlt => (Modifiers & KeyboardModifiers.Alt) != 0;
}

/// <summary>A turn of the wheel over the timeline.</summary>
public sealed class MovieWheelEvent
{
	/// <summary>Notches turned. Pushing the wheel away is negative, as in the panel system.</summary>
	public required float Delta { get; init; }

	public required Vector2 ScenePosition { get; init; }

	public KeyboardModifiers Modifiers { get; init; }

	public bool Accepted { get; private set; }

	public void Accept() => Accepted = true;

	public bool HasShift => (Modifiers & KeyboardModifiers.Shift) != 0;
	public bool HasCtrl => (Modifiers & KeyboardModifiers.Ctrl) != 0;
	public bool HasAlt => (Modifiers & KeyboardModifiers.Alt) != 0;
}

/// <summary>A key going down or coming up while the timeline has the keyboard.</summary>
public sealed class MovieKeyEvent
{
	/// <summary>The key, by the name the panel system gives it - "left", "escape".</summary>
	public required string Button { get; init; }

	public KeyboardModifiers Modifiers { get; init; }

	public bool Accepted { get; set; }

	public bool HasShift => (Modifiers & KeyboardModifiers.Shift) != 0;
	public bool HasCtrl => (Modifiers & KeyboardModifiers.Ctrl) != 0;
	public bool HasAlt => (Modifiers & KeyboardModifiers.Alt) != 0;
}
