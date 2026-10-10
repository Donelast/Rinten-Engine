using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Rinten.UI;

namespace Editor;

//Formats matched CSS rules for a panel, like DevTools with inactive and no-effect prefixes.
internal static class PanelStyleDumper
{
	static readonly Rinten.Diagnostics.Logger log = new( "StylePicker" );

	static readonly string[] InactiveProbeClasses =
	[
		"hovered",
		"selected",
		"active",
		"disabled",
		"hidden",
		"focus",
		"dragging",
		"open",
		"checked",
		"pressed",
		"expanded",
		"collapsed",
	];

	public static void Log( Panel panel )
	{
		if ( !panel.IsValid() )
			return;

		var header = DescribePanel( panel );
		log.Info( header );
		log.Info( Dump( panel ) );
	}

	/// <summary>Where the rule that styles this panel most is written: the most specific matching rule that sits in a file, or the panel's own source where no stylesheet rule does.</summary>
	public static (string Path, int Line)? FindSource( Panel panel )
	{
		if ( !panel.IsValid() )
			return null;

		var rules = CollectRules( panel );

		//Sorted by specificity, lowest first - so the last one that applies and has a line in a file on disk is the one that wins.
		for ( var i = rules.Count - 1; i >= 0; i-- )
		{
			var block = rules[i].Block;

			if ( !rules[i].Active ) continue;
			if ( block.FileLine <= 0 ) continue;

			var path = block.AbsolutePath;
			if ( string.IsNullOrEmpty( path ) || !System.IO.File.Exists( path ) ) continue;

			return (path, block.FileLine);
		}

		for ( var p = panel; p is not null; p = p.Parent )
		{
			if ( !string.IsNullOrWhiteSpace( p.SourceFile ) )
				return (p.SourceFile, p.SourceLine);
		}

		return null;
	}

	public static string Dump( Panel panel )
	{
		var rules = CollectRules( panel );
		var inline = CollectInline( panel );

		if ( rules.Count == 0 && inline.Count == 0 )
			return "(no matched styles)";

		var activeRules = new List<RuleEntry>();
		var activeIndexForRule = new int[rules.Count];

		for ( var i = 0; i < rules.Count; i++ )
		{
			if ( !rules[i].Active )
			{
				activeIndexForRule[i] = -1;
				continue;
			}

			activeIndexForRule[i] = activeRules.Count;
			activeRules.Add( rules[i] );
		}

		var winners = BuildWinners( activeRules, inline );

		var sb = new StringBuilder();
		var inlineIndex = activeRules.Count;
		var printed = new HashSet<string>( System.StringComparer.Ordinal );

		if ( inline.Count > 0 )
			AppendBlock( sb, "inline", inline, winners, inlineIndex, inactive: false );

		for ( var i = rules.Count - 1; i >= 0; i-- )
		{
			var rule = rules[i];
			var selector = FormatSelector( rule.Selector, panel );

			var displayKey = DisplayKey( selector, rule.Block.GetRawValues() );

			if ( !printed.Add( displayKey ) )
				continue;

			AppendBlock(
				sb,
				selector,
				rule.Block.GetRawValues(),
				winners,
				activeIndexForRule[i],
				inactive: !rule.Active );
		}

		return sb.ToString().TrimEnd();
	}

	static string DescribePanel( Panel panel )
	{
		var bits = new List<string>();

		if ( !string.IsNullOrWhiteSpace( panel.ElementName ) )
			bits.Add( panel.ElementName );

		if ( !string.IsNullOrWhiteSpace( panel.Classes ) )
			bits.Add( panel.Classes );

		if ( !string.IsNullOrWhiteSpace( panel.Id ) )
			bits.Add( $"#{panel.Id}" );

		if ( !string.IsNullOrWhiteSpace( panel.SourceFile ) )
			bits.Add( $"{panel.SourceFile}:{panel.SourceLine}" );

		return bits.Count > 0 ? string.Join( " ", bits ) : "panel";
	}

	struct RuleEntry
	{
		public StyleSelector Selector;
		public StyleBlock Block;
		public bool Active;
	}

	static List<RuleEntry> CollectRules( Panel panel )
	{
		var allowedClasses = BuildAllowedClasses( panel );
		var entries = new List<RuleEntry>();
		var seen = new HashSet<string>( System.StringComparer.Ordinal );

		foreach ( var sheet in panel.AllStyleSheets )
		{
			foreach ( var block in sheet.Nodes )
			{
				if ( block?.Selectors is null || block.GetRawValues().Count == 0 )
					continue;

				foreach ( var selector in block.Selectors )
				{
					if ( selector is null )
						continue;

					if ( !IsRelevantSelector( selector, panel, allowedClasses ) )
						continue;

					var key = DisplayKey( FormatSelector( selector, panel ), block.GetRawValues() );
					if ( !seen.Add( key ) )
						continue;

					entries.Add( new RuleEntry
					{
						Selector = selector,
						Block = block,
						Active = selector.Test( panel ),
					} );
				}
			}
		}

		entries.Sort( ( a, b ) => a.Selector.Score.CompareTo( b.Selector.Score ) );
		return entries;
	}

	static HashSet<string> BuildAllowedClasses( Panel panel )
	{
		var allowed = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var c in panel.Class )
			allowed.Add( c );

		foreach ( var c in InactiveProbeClasses )
			allowed.Add( c );

		return allowed;
	}

	static bool IsRelevantSelector( StyleSelector selector, Panel panel, HashSet<string> allowedClasses )
	{
		if ( selector is null || panel is null )
			return false;

		if ( selector.AnyOf is not null )
		{
			foreach ( var branch in selector.AnyOf )
			{
				if ( IsRelevantSelector( branch, panel, allowedClasses ) )
					return true;
			}

			return false;
		}

		if ( selector.Test( panel ) )
			return true;

		if ( selector.UniversalSelector )
			return false;

		if ( !SubjectTargetsPanel( selector, panel, allowedClasses ) )
			return false;

		if ( !ParentChainMatches( selector, panel ) )
			return false;

		return true;
	}

	static bool SubjectTargetsPanel( StyleSelector selector, Panel panel, HashSet<string> allowedClasses )
	{
		if ( selector.Element is not null
			&& !string.Equals( selector.Element, panel.ElementName, System.StringComparison.OrdinalIgnoreCase ) )
			return false;

		if ( selector.Id is not null )
		{
			if ( panel.Id is null )
				return false;

			if ( !string.Equals( selector.Id, panel.Id, System.StringComparison.OrdinalIgnoreCase ) )
				return false;
		}

		if ( selector.Classes is not null )
		{
			foreach ( var c in selector.Classes )
			{
				if ( !allowedClasses.Contains( c ) )
					return false;
			}
		}

		if ( selector.Element is not null
			&& string.Equals( selector.Element, panel.ElementName, System.StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( selector.Id is not null )
			return true;

		if ( selector.Classes is not null )
		{
			foreach ( var c in selector.Classes )
			{
				if ( panel.HasClass( c ) )
					return true;
			}
		}

		return selector.Classes is null or { Length: 0 }
			&& selector.Element is null
			&& selector.Id is null;
	}

	static bool ParentChainMatches( StyleSelector selector, Panel panel )
	{
		if ( selector.Parent is null )
			return true;

		if ( panel.Parent is null )
			return false;

		if ( selector.AdjacentSibling || selector.GeneralSibling )
			return selector.Test( panel );

		return selector.Parent.TestParent( panel.Parent, !selector.ImmediateParent );
	}

	static string FormatSelector( StyleSelector selector, Panel panel )
	{
		if ( !string.IsNullOrWhiteSpace( selector?.AsString ) )
			return selector.AsString;

		return selector?.Block?.SelectorStrings.FirstOrDefault()
			?? panel.Classes.Split( ' ' ).FirstOrDefault()
			?? "panel";
	}

	static string DisplayKey( string selector, List<IStyleBlock.StyleProperty> properties )
	{
		var sb = new StringBuilder();
		sb.Append( selector ?? "" );

		foreach ( var prop in properties.OrderBy( x => x.Name, System.StringComparer.OrdinalIgnoreCase ) )
		{
			sb.Append( '|' );
			sb.Append( prop.Name );
			sb.Append( ':' );
			sb.Append( prop.Value ?? prop.OriginalValue );
		}

		return sb.ToString();
	}

	static List<IStyleBlock.StyleProperty> CollectInline( Panel panel )
	{
		if ( panel.Style is null )
			return new List<IStyleBlock.StyleProperty>();

		var rawValues = typeof( Styles )
			.GetField( "RawValues", BindingFlags.Instance | BindingFlags.NonPublic )
			?.GetValue( panel.Style ) as Dictionary<string, IStyleBlock.StyleProperty>;

		return rawValues is null || rawValues.Count == 0
			? new List<IStyleBlock.StyleProperty>()
			: rawValues.Values.ToList();
	}

	static Dictionary<string, int> BuildWinners(
		List<RuleEntry> rules,
		List<IStyleBlock.StyleProperty> inline )
	{
		var winners = new Dictionary<string, int>( System.StringComparer.OrdinalIgnoreCase );

		for ( var i = rules.Count - 1; i >= 0; i-- )
		{
			foreach ( var prop in rules[i].Block.GetRawValues() )
			{
				if ( !winners.ContainsKey( prop.Name ) )
					winners[prop.Name] = i;
			}
		}

		var inlineIndex = rules.Count;

		foreach ( var prop in inline )
			winners[prop.Name] = inlineIndex;

		return winners;
	}

	static void AppendBlock(
		StringBuilder sb,
		string selector,
		List<IStyleBlock.StyleProperty> properties,
		Dictionary<string, int> winners,
		int ruleIndex,
		bool inactive )
	{
		if ( properties.Count == 0 )
			return;

		var header = inactive ? $"<inactive> {selector}" : selector;
		sb.AppendLine( $"{header} {{" );

		foreach ( var prop in properties
			.GroupBy( x => x.Name, System.StringComparer.OrdinalIgnoreCase )
			.Select( g => g.First() )
			.OrderBy( x => x.Name, System.StringComparer.OrdinalIgnoreCase ) )
		{
			var value = string.IsNullOrWhiteSpace( prop.Value ) ? prop.OriginalValue : prop.Value;
			var prefix = "";

			if ( !inactive && ruleIndex >= 0 && winners.GetValueOrDefault( prop.Name, ruleIndex ) != ruleIndex )
				prefix = "<no-effect> ";

			sb.AppendLine( $"  {prefix}{prop.Name}: {value};" );
		}

		sb.AppendLine( "}" );
	}
}
