namespace Editor.MovieMaker;

#nullable enable

//Both lists are laid out from these - the names down the left and the blocks across the timeline - so a row is the same height in each and they scroll together. Out here rather than on either of them, because neither owns it.
/// <summary>The sizes the movie editor is built out of.</summary>
public static class MovieLayout
{
	/// <summary>How tall one track's row is.</summary>
	public const int TrackHeight = 32;

	/// <summary>How tall a block is, inside its row.</summary>
	public const int BlockHeight = 30;

	/// <summary>The gap between two top level tracks.</summary>
	public const int RootTrackSpacing = 8;
}

/// <summary>Colours the timeline uses that are not in its theme file.</summary>
public static class MovieColors
{
	public static Color ChannelBackground => Theme.ControlBackground;

	public static Color HandleSelected => Color.White;
}
