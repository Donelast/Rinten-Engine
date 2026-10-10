using System;

namespace Editor;

//Only the four things GameMode does to it: find the window it is drawn in, take and give back the keyboard, and hear when either happens without being asked.
/// <summary>The surface a game is played in while the editor is open.</summary>
public interface IPlaySurface
{
	/// <summary>Whether the surface is on screen. A game behind another tab is not played into.</summary>
	bool IsShowing { get; }

	/// <summary>The panel the native window handle is found from.</summary>
	Rinten.UI.Panel InputSurface { get; }

	/// <summary>Takes the keyboard.</summary>
	void TakeFocus();

	/// <summary>Gives the keyboard up.</summary>
	void ReleaseFocus();

	/// <summary>Asks for the surface to be drawn again.</summary>
	void Redraw();

	/// <summary>Raised with true when the surface takes the keyboard and false when it loses it.</summary>
	event Action<bool> FocusChanged;
}
