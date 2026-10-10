namespace Editor;

/// <summary>What one tree row shows: name, glyph, children, plus optional extras</summary>
public readonly record struct TreeItemInfo( string Label, string Icon, bool HasChildren )
{
	/// <summary>The glyph's colour, where the thing has one of its own - a prefab, a stage.</summary>
	public Color? IconColor { get; init; }

	/// <summary>The name's colour, where it says something - a prefab, a heading.</summary>
	public Color? TextColor { get; init; }

	/// <summary>A heading over things rather than a thing: its name in capitals, in its colour, with how many it holds.</summary>
	public bool IsHeading { get; init; }

	/// <summary>How many things the row holds, said after its name - a heading's, mostly.</summary>
	public int? Count { get; init; }

	/// <summary>A small glyph over the corner of the main one - added to a prefab, say.</summary>
	public string Overlay { get; init; }

	/// <summary>A quiet word or glyph after the name - its extension, who owns it.</summary>
	public string Suffix { get; init; }

	/// <summary>Whether <see cref="Suffix"/> is a glyph rather than a word.</summary>
	public bool SuffixIsIcon { get; init; }

	/// <summary>Switched off, or otherwise not taking part: drawn faint.</summary>
	public bool Dim { get; init; }

	/// <summary>What the pointer says over the row.</summary>
	public string Tooltip { get; init; }

	/// <summary>Further classes for the row, for what one tree marks that the others do not.</summary>
	public string Classes { get; init; }

	/// <summary>The small buttons at the row's far end - an eye, a lock.</summary>
	public TreeRowAction[] Actions { get; init; }
}

/// <summary>One small button at the far end of a tree row.</summary>
public sealed record TreeRowAction( string Icon, string OffIcon, string Tooltip, bool On, Action Pressed = null )
{
	/// <summary>The eye every tree means the same thing by: whether the thing takes part.</summary>
	public static TreeRowAction Visibility( bool on, Action pressed = null )
		=> new( "visibility", "visibility_off", on ? "Disable" : "Enable", on, pressed );

	/// <summary>The lock: whether the thing can be changed.</summary>
	public static TreeRowAction Lock( bool locked, Action pressed = null )
		=> new( "lock", "lock_open", locked ? "Unlock" : "Lock", locked, pressed );

	/// <summary>The eye alone, on and off, for a tree to hand every row without making one each time.</summary>
	public static readonly TreeRowAction[] EyeOn = [Visibility( true )];
	public static readonly TreeRowAction[] EyeOff = [Visibility( false )];
}
