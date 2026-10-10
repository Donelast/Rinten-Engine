using Rinten.UI;

namespace Editor.UiEditor;

/// <summary>Rules styling one panel in engine order, tracking which rule each value came from</summary>
public sealed class StyleCascade
{
	//The name the engine gives a sheet it read out of a <style> in a razor - see PanelRenderTreeBuilder.AddStyleDefinitions .
	internal const string StyleTagSheet = "__Razor.";

	/// <summary>One block that hits the panel, through one of its selectors.</summary>
	public sealed class Rule
	{
		public StyleBlock Block;
		public StyleSelector Selector;

		/// <summary>The sheet the block was read from.</summary>
		public StyleSheet Sheet;

		/// <summary>Where in the cascade: higher is applied later, and wins.</summary>
		public int Order;

		/// <summary>The declarations as written, in the order written.</summary>
		public List<IStyleBlock.StyleProperty> Properties;

		/// <summary>The selector as the engine strings it, nesting resolved.</summary>
		public string SelectorText => Selector?.AsString ?? Block?.SelectorStrings.FirstOrDefault() ?? "";

		/// <summary>The file the rule was written in, absolute, or null for one built in memory.</summary>
		public string Path => Block?.AbsolutePath ?? _resolved;

		public string FileName => Block?.FileName;

		/// <summary>True for rules from a razor &lt;style&gt; tag; shown but never written, since they are markup</summary>
		public bool IsInStyleTag
			=> ( Sheet?.FileName?.StartsWith( StyleTagSheet, StringComparison.Ordinal ) ?? false )
			|| ( FileName?.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) ?? false );

		/// <summary>Whether an edit of this rule has a stylesheet to go into.</summary>
		public bool CanWrite => Path is not null && !IsInStyleTag;

		string _resolved;

		//A block reloaded by the watcher can come without its absolute path; its mount-relative name
		//still says which file it is, and when that is the document's own sheet the path is that.
		internal void ResolveAgainst( string ownSheetPath )
		{
			if ( Block?.AbsolutePath is not null || FileName is null || ownSheetPath is null ) return;

			var tail = FileName.Replace( '\\', '/' ).TrimStart( '/' );
			var own = ownSheetPath.Replace( '\\', '/' );

			if ( own.EndsWith( tail, StringComparison.OrdinalIgnoreCase ) && ( own.Length == tail.Length || own[own.Length - tail.Length - 1] == '/' ) )
				_resolved = ownSheetPath;
		}
	}

	/// <summary>Every rule that applies, in cascade order: the first is applied first, the last wins.</summary>
	public List<Rule> Rules { get; } = new();

	/// <summary>Rules that would apply in another state - hovered, pressed, focused - with the state they wait for. What the States dock will switch on; for now shown dimmed under the live ones.</summary>
	public List<(Rule rule, PseudoClass state)> Inactive { get; } = new();

	//The states a rule can be waiting for, tried one at a time.
	static readonly PseudoClass[] ProbeStates = { PseudoClass.Hover, PseudoClass.Active, PseudoClass.Focus, PseudoClass.Intro, PseudoClass.Outro };

	/// <summary>The panel's inline style, as written by code.</summary>
	public List<IStyleBlock.StyleProperty> Inline { get; } = new();

	/// <summary>For each property, the rule whose value the computed style took; absent when inline style set it.</summary>
	public Dictionary<string, Rule> Winners { get; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The properties inline style set, which no rule can beat.</summary>
	public HashSet<string> InlineWins { get; } = new( StringComparer.OrdinalIgnoreCase );

	static readonly System.Reflection.FieldInfo RawValuesField = typeof( Styles )
		.GetField( "RawValues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );

	public static StyleCascade For( Panel panel, string ownSheetPath = null )
	{
		var cascade = new StyleCascade();

		if ( !panel.IsValid() ) return cascade;

		cascade.Gather( panel );

		if ( ownSheetPath is not null )
		{
			foreach ( var rule in cascade.Rules ) rule.ResolveAgainst( ownSheetPath );
			foreach ( var (rule, _) in cascade.Inactive ) rule.ResolveAgainst( ownSheetPath );
		}

		return cascade;
	}

	void Gather( Panel panel )
	{
		var order = 0;

		foreach ( var sheet in panel.AllStyleSheets )
		{
			if ( sheet?.Nodes is null ) continue;

			foreach ( var block in sheet.Nodes )
			{
				if ( block?.Selectors is null ) continue;

				var properties = block.GetRawValues();
				if ( properties is null || properties.Count == 0 ) continue;

				var selector = block.Test( panel );

				if ( selector is null )
				{
					ProbeInactive( panel, block, sheet, properties );
					continue;
				}

				Rules.Add( new Rule
				{
					Block = block,
					Selector = selector,
					Sheet = sheet,
					Order = order++,
					Properties = properties,
				} );
			}
		}

		//The engine's order: by score, and the gather order breaks the ties the way a stable sort would.
		Rules.Sort( ( a, b ) =>
		{
			var byScore = a.Selector.Score.CompareTo( b.Selector.Score );
			return byScore != 0 ? byScore : a.Order.CompareTo( b.Order );
		} );

		for ( var i = 0; i < Rules.Count; i++ )
		{
			Rules[i].Order = i;

			foreach ( var property in Rules[i].Properties )
				Winners[property.Name] = Rules[i];
		}

		//What code set on the panel directly. The dictionary is the engine's own and not public; the picker in the tools addon reads it the same way.
		if ( panel.Style is { } style && RawValuesField?.GetValue( style ) is Dictionary<string, IStyleBlock.StyleProperty> inline )
		{
			foreach ( var property in inline.Values )
			{
				Inline.Add( property );
				InlineWins.Add( property.Name );
			}
		}
	}

	//A block that misses the panel as it is: does it hit with one more state on?
	void ProbeInactive( Panel panel, StyleBlock block, StyleSheet sheet, List<IStyleBlock.StyleProperty> properties )
	{
		foreach ( var state in ProbeStates )
		{
			if ( ( panel.PseudoClass & state ) != 0 ) continue;

			var selector = block.Test( panel, state );
			if ( selector is null ) continue;

			Inactive.Add( ( new Rule { Block = block, Selector = selector, Sheet = sheet, Properties = properties }, state ) );
			return;
		}
	}

	/// <summary>Whether this rule's value for the property is the one in effect.</summary>
	public bool IsWinner( Rule rule, string property )
	{
		if ( InlineWins.Contains( property ) ) return false;

		return Winners.TryGetValue( property, out var winner ) && ReferenceEquals( winner, rule );
	}

	/// <summary>The rule an edit of this panel should go to: the winning rule from the panel's own sheet, the one written beside its razor, or the highest such rule when nothing yet says the property. Null when no rule of the panel's own hits it.</summary>
	public Rule EditTarget( string property, string ownSheetPath )
	{
		if ( string.IsNullOrEmpty( ownSheetPath ) ) return null;

		if ( property is not null && Winners.TryGetValue( property, out var winner ) && IsOwn( winner, ownSheetPath ) )
			return winner;

		for ( var i = Rules.Count - 1; i >= 0; i-- )
		{
			if ( IsOwn( Rules[i], ownSheetPath ) ) return Rules[i];
		}

		return null;
	}

	static bool IsOwn( Rule rule, string ownSheetPath )
	{
		return rule.Path is not null && string.Equals(
			System.IO.Path.GetFullPath( rule.Path ), System.IO.Path.GetFullPath( ownSheetPath ),
			StringComparison.OrdinalIgnoreCase );
	}
}
