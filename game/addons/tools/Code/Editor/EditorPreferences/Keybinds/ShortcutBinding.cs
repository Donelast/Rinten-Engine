namespace Editor.Preferences;

//The keys are a property rather than a pair of methods so the page can hand this straight to KeyBindControl - the control every other bind in the editor is caught by. It takes a serialized property, and a property is what this is.
/// <summary>One editor shortcut, as the keybinds page reads and writes it.</summary>
public sealed class ShortcutBinding
{
	public ShortcutBinding( EditorShortcuts.Entry entry )
	{
		Identifier = entry.Identifier;
		Name = entry.Name;
		Group = entry.Group;
	}

	public string Identifier { get; }
	public string Name { get; }
	public string Group { get; }

	string keys;

	/// <summary>Press to set the keyboard code for this shortcut.</summary>
	[KeyBind]
	[Title( "Shortcut" )]
	public string Keys
	{
		get => keys ??= EditorShortcuts.GetKeys( Identifier );
		set => Override( value );
	}

	/// <summary>Reads the keys again, because the shortcuts were registered afresh.</summary>
	public void Reread() => keys = null;

	/// <summary>The keys it was declared with.</summary>
	public string DefaultKeys => EditorShortcuts.GetDefaultKeys( Identifier );

	/// <summary>Whether the keys are the ones set here rather than the ones declared.</summary>
	public bool IsOverridden => EditorPreferences.ShortcutOverrides.ContainsKey( Identifier );

	/// <summary>Whether there is a binding to take away.</summary>
	public bool CanClear => !string.IsNullOrEmpty( Keys );

	/// <summary>Back to the keys it was declared with.</summary>
	public void Revert()
	{
		var overrides = EditorPreferences.ShortcutOverrides;

		if ( !overrides.Remove( Identifier ) ) return;

		EditorPreferences.ShortcutOverrides = overrides;

		Changed();
	}

	/// <summary>Bound to nothing at all, which is not the same as back to its default.</summary>
	public void Clear() => Override( "" );

	/// <summary>The other shortcuts listening for the same keys.</summary>
	public IReadOnlyList<string> Conflicts()
	{
		var bound = Keys;

		if ( string.IsNullOrEmpty( bound ) ) return [];

		return EditorShortcuts.Entries
			.Where( x => x.Keys == bound && x.Identifier != Identifier )
			.Select( x => x.Identifier )
			.Distinct()
			.ToList();
	}

	/// <summary>Every shortcut back to the keys it was declared with.</summary>
	public static void ResetAll()
	{
		EditorPreferences.ShortcutOverrides = new();

		Notify();
	}

	void Override( string value )
	{
		var overrides = EditorPreferences.ShortcutOverrides;
		overrides[Identifier] = value ?? "";
		EditorPreferences.ShortcutOverrides = overrides;

		Changed();
	}

	//This one moved, so what it read is stale and everything rebinds.
	void Changed()
	{
		keys = null;

		Notify();
	}

	//Everything that registered a shortcut binds it again.
	static void Notify() => EditorEvent.Run( "keybinds.update" );
}
