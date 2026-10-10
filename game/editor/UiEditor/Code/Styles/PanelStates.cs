using Rinten.UI;

namespace Editor.UiEditor;

//Forced pseudo-classes and sheet classes on a canvas panel, so their rules can be seen and edited
internal sealed class PanelStates
{
	public static readonly (PseudoClass flag, string name)[] Pseudo =
	{
		( PseudoClass.Hover, "hover" ),
		( PseudoClass.Active, "active" ),
		( PseudoClass.Focus, "focus" ),
		( PseudoClass.Intro, "intro" ),
		( PseudoClass.Outro, "outro" ),
	};

	readonly Panel _panel;

	PseudoClass _forced;
	readonly HashSet<string> _forcedClasses = new( StringComparer.OrdinalIgnoreCase );

	public PanelStates( Panel panel )
	{
		_panel = panel;
	}

	public Panel Panel => _panel;

	public bool IsForced( PseudoClass flag ) => ( _forced & flag ) != 0;

	public bool IsForced( string cls ) => _forcedClasses.Contains( cls );

	public void Force( PseudoClass flag, bool on )
	{
		if ( on ) _forced |= flag;
		else
		{
			_forced &= ~flag;
			if ( _panel.IsValid() ) _panel.Switch( flag, false );
		}

		Hold();
	}

	public void Force( string cls, bool on )
	{
		if ( on ) _forcedClasses.Add( cls );
		else _forcedClasses.Remove( cls );

		if ( _panel.IsValid() ) _panel.SetClass( cls, on );
	}

	/// <summary>Put the forced states back on the panel; every frame, since the pointer takes them off.</summary>
	public void Hold()
	{
		if ( !_panel.IsValid() ) return;

		foreach ( var (flag, _) in Pseudo )
		{
			if ( ( _forced & flag ) != 0 ) _panel.Switch( flag, true );
		}
	}

	/// <summary>Let go of everything forced.</summary>
	public void Release()
	{
		if ( _panel.IsValid() )
		{
			foreach ( var (flag, _) in Pseudo )
			{
				if ( ( _forced & flag ) != 0 ) _panel.Switch( flag, false );
			}

			foreach ( var cls in _forcedClasses )
				_panel.SetClass( cls, false );
		}

		_forced = PseudoClass.None;
		_forcedClasses.Clear();
	}

	/// <summary>The classes the panel's sheets have rules for that the panel could wear: a selector whose subject asks for one class more than the panel has, everything else about it matching.</summary>
	public static IEnumerable<string> StateClasses( Panel panel )
	{
		var found = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
		if ( !panel.IsValid() ) return found;

		var has = panel.Class.ToHashSet( StringComparer.OrdinalIgnoreCase );

		foreach ( var sheet in panel.AllStyleSheets )
		{
			if ( sheet?.Nodes is null ) continue;

			foreach ( var block in sheet.Nodes )
			{
				if ( block?.Selectors is null ) continue;

				foreach ( var selector in block.Selectors )
					Probe( selector, panel, has, found );
			}
		}

		return found;
	}

	static void Probe( StyleSelector selector, Panel panel, HashSet<string> has, SortedSet<string> found )
	{
		if ( selector is null ) return;

		if ( selector.AnyOf is not null )
		{
			foreach ( var branch in selector.AnyOf ) Probe( branch, panel, has, found );
			return;
		}

		if ( selector.Classes is null || selector.Classes.Length == 0 ) return;

		if ( selector.Element is not null && !string.Equals( selector.Element, panel.ElementName, StringComparison.OrdinalIgnoreCase ) ) return;

		//One class the panel lacks, beside at least one it has: `.button.active` is a state of a button; `.bar` alone is another panel's rule.
		string missing = null;
		var shared = false;

		foreach ( var c in selector.Classes )
		{
			if ( has.Contains( c ) ) { shared = true; continue; }
			if ( missing is not null ) return;

			missing = c;
		}

		if ( missing is null || !shared ) return;

		//The rest of the chain has to fit the panel's ancestors.
		if ( selector.Parent is not null && panel.Parent is not null && !selector.Parent.TestParent( panel.Parent, !selector.ImmediateParent ) ) return;

		found.Add( missing );
	}
}
