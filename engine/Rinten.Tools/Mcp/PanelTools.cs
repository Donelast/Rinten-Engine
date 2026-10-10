using Rinten.UI;
using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace Editor.Mcp;

/// <summary>The panel trees the editor is made of, read the way an agent reads a web page: every window of the editor is a tree of panels - the editor's own widgets wear one each - and so is the game's HUD. Each panel gets a handle that the other tools take: a screenshot cropped to it, a click on it, its computed style.</summary>
[McpToolset( "panel", "The editor's and the game's UI as a tree of panels - find one, read its box and style, hand its id to a screenshot or a click" )]
internal static partial class PanelTools
{
	[McpTool.ReadOnly( "panel_tree" )]
	[Description( "The tree of panels in a window of the editor, or in the game's own UI, one line per panel: its id, what it is (the widget it fronts, or its element and classes), its text, its box in window pixels as 'x,y wxh', and whether it's hidden or focused. Hand an id to panel_inspect, window_screenshot or ui_click. Start at the window's root, or at a panel; go as deep as 'depth'. Big windows have thousands of panels - use panel_find to go straight to one by text or type." )]
	public static string PanelTree(
		[Description( "Which window: a title substring, or its index from window_list. Empty is the main editor window. 'game' is the game's UI - the HUD and world panels of the scene being played or edited." )] string window = "",
		[Description( "A panel id to start from instead of the window's root." )] string root = "",
		[Description( "How many levels down to show. Default 6, max 40." ), Range( 1, 40 )] int depth = 6,
		[Description( "Whether to include panels that aren't visible. Default false." )] bool includeHidden = false,
		[Description( "The most panels to print. Default 300, max 3000." ), Range( 10, 3000 )] int limit = 300 )
	{
		var roots = string.IsNullOrWhiteSpace( root )
			? Roots( window )
			: [PanelHandles.Resolve( root )];

		var builder = new StringBuilder();
		var printed = 0;
		var skipped = 0;

		foreach ( var top in roots )
		{
			Print( top, 0 );
		}

		if ( skipped > 0 )
			builder.AppendLine( $"... {skipped} more panels past the limit of {limit}. Start lower with root, or go straight to one with panel_find." );

		if ( printed == 0 )
			builder.AppendLine( "No panels." );

		return builder.ToString().TrimEnd();

		void Print( Panel panel, int level )
		{
			if ( panel is null || !panel.IsValid ) return;
			if ( !includeHidden && !panel.IsVisible ) return;

			if ( printed >= limit )
			{
				skipped++;
				return;
			}

			printed++;
			builder.Append( ' ', level * 2 ).AppendLine( Line( panel ) );

			if ( level + 1 >= depth )
			{
				var below = panel.ChildrenCount;

				if ( below > 0 )
					builder.Append( ' ', (level + 1) * 2 ).AppendLine( $"... {below} children below depth {depth}" );

				return;
			}

			foreach ( var child in panel.Children )
			{
				Print( child, level + 1 );
			}
		}
	}

	[McpTool.ReadOnly( "panel_find" )]
	[Description( "Find panels by what they say or what they are: a text substring (a button's label, a field's value, a tooltip; with a type or class given too, text inside the panel counts, so 'the row that says Player' is type HierarchyTreeRow, text Player), a widget or element type (a base type counts: Button finds an IconButton), a class. Every given filter must match. Returns each match's line as panel_tree prints it plus where it sits - the chain of its parents - so the right 'Delete' button can be told from the other three. Empty filters match everything, which is a lot; give at least one." )]
	public static string PanelFind(
		[Description( "Case insensitive substring of the panel's text, name or tooltip." )] string text = "",
		[Description( "The widget type (Button, LineEdit, TreeView...) or panel element/type name, case insensitive." )] string type = "",
		[Description( "A class the panel has, without the dot." )] string @class = "",
		[Description( "Which window, as panel_tree takes it. Empty searches every editor window; 'game' the game's UI." )] string window = "",
		[Description( "Whether hidden panels count. Default false." )] bool includeHidden = false,
		[Description( "The most matches to return. Default 30, max 200." ), Range( 1, 200 )] int limit = 30 )
	{
		if ( string.IsNullOrWhiteSpace( text ) && string.IsNullOrWhiteSpace( type ) && string.IsNullOrWhiteSpace( @class ) )
			throw new Exception( "Give at least one of text, type or class to look for." );

		var roots = string.IsNullOrWhiteSpace( window ) ? AllRoots() : Roots( window );
		var matches = new List<Panel>();
		var total = 0;

		foreach ( var top in roots )
		{
			Walk( top );
		}

		if ( matches.Count == 0 )
			return "Nothing matched. panel_tree shows what's there; text matches the label, name or tooltip, type the widget type or element name.";

		var builder = new StringBuilder();
		builder.AppendLine( total > matches.Count ? $"{matches.Count} of {total} matches:" : $"{matches.Count} match{(matches.Count == 1 ? "" : "es")}:" );

		foreach ( var panel in matches )
		{
			builder.AppendLine( Line( panel ) );
			builder.AppendLine( $"    in {Chain( panel )}" );
		}

		return builder.ToString().TrimEnd();

		void Walk( Panel panel )
		{
			if ( panel is null || !panel.IsValid ) return;
			if ( !includeHidden && !panel.IsVisible ) return;

			if ( Matches( panel ) )
			{
				total++;
				if ( matches.Count < limit ) matches.Add( panel );
			}

			foreach ( var child in panel.Children )
			{
				Walk( child );
			}
		}

		bool Matches( Panel panel )
		{
			if ( !string.IsNullOrWhiteSpace( type ) && !IsOfType( panel, type ) )
				return false;

			if ( !string.IsNullOrWhiteSpace( @class ) && !panel.Class.Any( x => string.Equals( x, @class.TrimStart( '.' ), StringComparison.OrdinalIgnoreCase ) ) )
				return false;

			//Text matches the panel's own, or its descendants' when a type or class is also given
			var within = !string.IsNullOrWhiteSpace( type ) || !string.IsNullOrWhiteSpace( @class );

			if ( !string.IsNullOrWhiteSpace( text ) && !Says( panel, text, within ? 4 : 0 ) )
				return false;

			return true;
		}
	}

	//Whether a panel says the text - itself, or in what's inside it up to so many levels down.
	static bool Says( Panel panel, string text, int levels )
	{
		var said = $"{TextOf( panel )} {NameOf( panel )} {TooltipOf( panel )}";

		if ( said.Contains( text, StringComparison.OrdinalIgnoreCase ) ) return true;
		if ( levels <= 0 ) return false;

		foreach ( var child in panel.Children )
		{
			if ( Says( child, text, levels - 1 ) ) return true;
		}

		return false;
	}

	[McpTool.ReadOnly( "panel_inspect" )]
	[Description( "Everything about one panel, by the id panel_tree or panel_find gave: its box (outer, inner, padding, margin), the widget behind it and that widget's state, its classes and pseudo classes, the style rules that matched it with the file and line each came from, the computed style values that matter for layout, and where in the tree it sits. For 'why is this not where I expect' and 'why is this invisible'." )]
	public static PanelDetail PanelInspect( [Description( "The panel's id, from panel_tree or panel_find." )] string id )
	{
		var panel = PanelHandles.Resolve( id );
		var style = panel.ComputedStyle;

		return new PanelDetail
		{
			Id = PanelHandles.Id( panel ),
			Type = panel.GetType().Name,
			Widget = WidgetOf( panel ) is { } widget ? new WidgetDetail
			{
				Type = widget.GetType().Name,
				Name = widget.Name,
				Text = TextOf( panel ),
				ToolTip = widget.ToolTip,
				Enabled = widget.Enabled,
				Visible = widget.Visible,
				Focused = widget.IsFocused,
				Hovered = widget.IsUnderMouse,
			} : null,
			Element = panel.ElementName,
			Classes = panel.Class.ToArray(),
			PseudoClasses = panel.PseudoClass.ToString(),
			Text = TextOf( panel ),
			Rect = RectText( panel.Box.Rect ),
			RectOuter = RectText( panel.Box.RectOuter ),
			RectInner = RectText( panel.Box.RectInner ),
			Padding = panel.Box.Padding.ToString(),
			Margin = panel.Box.Margin.ToString(),
			ClipRect = RectText( panel.Box.ClipRect ),
			Visible = panel.IsVisible,
			VisibleSelf = panel.IsVisibleSelf,
			HasFocus = panel.HasFocus,
			Opacity = panel.Opacity,
			Children = panel.ChildrenCount,
			Source = panel.SourceFile is null ? null : $"{panel.SourceFile}:{panel.SourceLine}",
			Chain = Chain( panel ),
			Window = CoreUiWindow.Holding( panel )?.Title,
			Style = style is null ? null : new StyleDetail
			{
				Display = style.Display?.ToString(),
				Position = style.Position?.ToString(),
				FlexDirection = style.FlexDirection?.ToString(),
				Width = style.Width?.ToString(),
				Height = style.Height?.ToString(),
				Left = style.Left?.ToString(),
				Top = style.Top?.ToString(),
				Right = style.Right?.ToString(),
				Bottom = style.Bottom?.ToString(),
				FlexGrow = style.FlexGrow,
				FlexShrink = style.FlexShrink,
				Overflow = style.Overflow?.ToString(),
				Opacity = style.Opacity,
				PointerEvents = style.PointerEvents?.ToString(),
				BackgroundColor = style.BackgroundColor?.ToString(),
				FontSize = style.FontSize?.ToString(),
				FontFamily = style.FontFamily,
				Color = style.FontColor?.ToString(),
			},
			Rules = panel.ActiveStyleBlocks
				.Select( block => $"{string.Join( ", ", block.SelectorStrings )} ({block.FileName}:{block.FileLine})" )
				.ToArray()
		};
	}

	//── Where the panels are ─────────────────────────────────────────────────

	//The roots the window name means: one window's, the game's, or the main window's.
	internal static IEnumerable<Panel> Roots( string window )
	{
		if ( string.Equals( window?.Trim(), "game", StringComparison.OrdinalIgnoreCase ) )
		{
			var game = GameRoots().ToArray();

			if ( game.Length == 0 )
				throw new Exception( "The game has no UI panels right now - no ScreenPanel or WorldPanel in the active scene, or nothing is playing." );

			return game;
		}

		var shell = WindowTools.Resolve( window );

		return shell.Root is { IsValid: true } root
			? [root]
			: throw new Exception( $"Window '{shell.Title}' has no panels." );
	}

	//Every root there is - each editor window's and the game's.
	static IEnumerable<Panel> AllRoots()
	{
		foreach ( var window in CoreUiWindow.AllOpen )
		{
			if ( window.Root is { IsValid: true } root ) yield return root;
		}

		foreach ( var root in GameRoots() )
		{
			yield return root;
		}
	}

	//The game's roots: everything the UI system ticks that isn't an editor window's.
	static IEnumerable<Panel> GameRoots()
	{
		var system = Rinten.Engine.GlobalContext.Current?.UISystem;

		if ( system is null ) yield break;

		foreach ( var root in system.RootPanels.ToArray() )
		{
			if ( root is { IsValid: true, IsEditorUi: false } ) yield return root;
		}
	}

	//── Describing a panel ───────────────────────────────────────────────────

	//One panel on one line, the way panel_tree and panel_find print it.
	internal static string Line( Panel panel )
	{
		var builder = new StringBuilder();

		builder.Append( PanelHandles.Id( panel ) ).Append( ' ' );
		builder.Append( string.Join( '/', TypeNames( panel ) ) );

		if ( NameOf( panel ) is { Length: > 0 } name )
			builder.Append( " #" ).Append( name );

		var classes = panel.Class.Where( x => !IsWidgetClass( panel, x ) ).ToArray();

		if ( classes.Length > 0 )
			builder.Append( " [" ).Append( string.Join( ' ', classes ) ).Append( ']' );

		if ( TextOf( panel ) is { Length: > 0 } text )
			builder.Append( " \"" ).Append( Shorten( text, 60 ) ).Append( '"' );

		builder.Append( ' ' ).Append( RectText( panel.Box.Rect ) );

		if ( !panel.IsVisible ) builder.Append( " (hidden)" );
		else if ( IsOffscreen( panel ) ) builder.Append( " (offscreen)" );
		if ( panel.HasFocus ) builder.Append( " (focus)" );
		if ( panel.HasHovered ) builder.Append( " (hover)" );
		if ( WidgetOf( panel ) is { Enabled: false } ) builder.Append( " (disabled)" );

		return builder.ToString();
	}

	//The panel's parents from the root down, as types and names.
	static string Chain( Panel panel )
	{
		var parts = new List<string>();

		for ( var up = panel.Parent; up is not null; up = up.Parent )
		{
			var name = NameOf( up );
			parts.Add( string.IsNullOrEmpty( name ) ? TypeNames( up ).First() : $"{TypeNames( up ).First()}#{name}" );
		}

		parts.Reverse();

		return parts.Count == 0 ? "(root)" : string.Join( " > ", parts );
	}

	//What a panel is called, most specific first: the widget it fronts, then the panel's element name, then its class.
	static IEnumerable<string> TypeNames( Panel panel )
	{
		if ( WidgetOf( panel ) is { } widget )
			yield return widget.GetType().Name;

		if ( !string.IsNullOrEmpty( panel.ElementName ) && !string.Equals( panel.ElementName, panel.GetType().Name, StringComparison.OrdinalIgnoreCase ) )
			yield return panel.ElementName;

		yield return panel.GetType().Name;
	}

	//Whether a panel is of a type, by any name it goes by - the widget's own type
	//or any it derives from, so 'Button' finds an IconButton too - or the panel's.
	static bool IsOfType( Panel panel, string type )
	{
		if ( TypeNames( panel ).Any( x => string.Equals( x, type, StringComparison.OrdinalIgnoreCase ) ) )
			return true;

		for ( var widgetType = WidgetOf( panel )?.GetType(); widgetType is not null && widgetType != typeof( object ); widgetType = widgetType.BaseType )
		{
			if ( string.Equals( widgetType.Name, type, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		for ( var panelType = panel.GetType(); panelType is not null && panelType != typeof( object ); panelType = panelType.BaseType )
		{
			if ( string.Equals( panelType.Name, type, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	//Whether a visible panel is nowhere to be seen: scrolled out of its window, or clipped
	//away by a parent. Its box says where it would be; a click there reaches nothing.
	internal static bool IsOffscreen( Panel panel )
	{
		var rect = panel.Box.Rect;
		var centre = new Vector2( rect.Left + rect.Width / 2, rect.Top + rect.Height / 2 );

		if ( CoreUiWindow.Holding( panel ) is { } window )
		{
			var (width, height) = window.Size;
			if ( centre.x < 0 || centre.y < 0 || centre.x >= width || centre.y >= height ) return true;
		}

		var clip = panel.Box.ClipRect;

		return clip.Width > 0 && clip.Height > 0 && !clip.IsInside( centre );
	}

	//The editor widget a panel is the front of, or null for a panel of the game's.
	internal static Widget WidgetOf( Panel panel ) => (panel as WidgetSurface)?.Widget;

	//The editor's panels wear their widget's type as a class, for the stylesheet - it is not information to a reader who has the type already.
	static bool IsWidgetClass( Panel panel, string @class )
	{
		var widget = WidgetOf( panel );

		if ( widget is null ) return false;

		for ( var type = widget.GetType(); type is not null && type != typeof( object ); type = type.BaseType )
		{
			if ( string.Equals( type.Name, @class, StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	//What a panel says: a label's text, a widget's text or title. Widgets don't share a base for it, so the usual property names are asked for by name.
	internal static string TextOf( Panel panel )
	{
		if ( panel is Rinten.UI.Label label )
			return label.Text;

		if ( WidgetOf( panel ) is { } widget )
		{
			foreach ( var property in TextPropertiesOf( widget.GetType() ) )
			{
				if ( property.GetValue( widget ) is string text && text.Length > 0 )
					return text;
			}
		}

		return null;
	}

	static readonly string[] TextProperties = ["Text", "Title", "Value", "PlaceholderText"];

	//The string properties a widget type has among the usual names, looked up once per type: a tree of thousands of panels asks for every one of them.
	static System.Reflection.PropertyInfo[] TextPropertiesOf( Type type )
	{
		return textProperties.GetOrAdd( type, static t => TextProperties
			.Select( name => t.GetProperty( name ) )
			.Where( p => p is not null && p.PropertyType == typeof( string ) && p.GetIndexParameters().Length == 0 )
			.ToArray() );
	}

	static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, System.Reflection.PropertyInfo[]> textProperties = new();

	static string NameOf( Panel panel ) => WidgetOf( panel )?.Name is { Length: > 0 } name ? name : panel.Id;

	static string TooltipOf( Panel panel ) => WidgetOf( panel )?.ToolTip;

	internal static string RectText( Rect rect ) => $"{rect.Left:0},{rect.Top:0} {rect.Width:0}x{rect.Height:0}";

	static string Shorten( string text, int max )
	{
		text = text.Replace( '\n', ' ' ).Replace( '\r', ' ' );
		return text.Length <= max ? text : text[..(max - 1)] + "…";
	}
}

//Ids for panels, handed out as they are seen and good for as long as the panel lives. A panel has no id
//of its own that survives a rebuild, and an agent needs one to hand back - to click, to crop, to inspect.
internal static class PanelHandles
{
	static readonly ConditionalWeakTable<Panel, Handle> byPanel = new();
	static readonly Dictionary<int, WeakReference<Panel>> byNumber = new();
	static int next;

	sealed class Handle
	{
		public int Number;
	}

	public static string Id( Panel panel )
	{
		lock ( byNumber )
		{
			if ( !byPanel.TryGetValue( panel, out var handle ) )
			{
				handle = new Handle { Number = ++next };
				byPanel.Add( panel, handle );
				byNumber[handle.Number] = new WeakReference<Panel>( panel );

				//Every so often, let go of the ones whose panels have gone
				if ( byNumber.Count % 512 == 0 ) Sweep();
			}

			return $"p{handle.Number}";
		}
	}

	public static Panel Resolve( string id )
	{
		var trimmed = id?.Trim().TrimStart( 'p', 'P' );

		if ( !int.TryParse( trimmed, out var number ) )
			throw new Exception( $"'{id}' isn't a panel id - they look like 'p12', from panel_tree or panel_find." );

		lock ( byNumber )
		{
			if ( byNumber.TryGetValue( number, out var reference ) && reference.TryGetTarget( out var panel ) && panel.IsValid )
				return panel;
		}

		throw new Exception( $"Panel {id} is gone - it was deleted or its window closed. Find it again with panel_find." );
	}

	static void Sweep()
	{
		foreach ( var number in byNumber.Where( x => !x.Value.TryGetTarget( out var panel ) || !panel.IsValid ).Select( x => x.Key ).ToArray() )
		{
			byNumber.Remove( number );
		}
	}
}

//What panel_inspect reports.
internal class PanelDetail
{
	public string Id { get; set; }
	public string Type { get; set; }
	public WidgetDetail Widget { get; set; }
	public string Element { get; set; }
	public string[] Classes { get; set; }
	public string PseudoClasses { get; set; }
	public string Text { get; set; }

	[Description( "The panel's box in window pixels, 'x,y wxh'. Click at its centre." )]
	public string Rect { get; set; }
	public string RectOuter { get; set; }
	public string RectInner { get; set; }
	public string Padding { get; set; }
	public string Margin { get; set; }
	public string ClipRect { get; set; }
	public bool Visible { get; set; }
	public bool VisibleSelf { get; set; }
	public bool HasFocus { get; set; }
	public float Opacity { get; set; }
	public int Children { get; set; }

	[Description( "The .razor file and line this panel came from, when it did." )]
	public string Source { get; set; }

	[Description( "Its parents, root first." )]
	public string Chain { get; set; }
	public string Window { get; set; }
	public StyleDetail Style { get; set; }

	[Description( "The style rules that matched, each with its file and line." )]
	public string[] Rules { get; set; }
}

//The editor widget a panel fronts, as panel_inspect reports it.
internal class WidgetDetail
{
	public string Type { get; set; }
	public string Name { get; set; }
	public string Text { get; set; }
	public string ToolTip { get; set; }
	public bool Enabled { get; set; }
	public bool Visible { get; set; }
	public bool Focused { get; set; }
	public bool Hovered { get; set; }
}

//The computed style values that decide where a panel is and whether it shows.
internal class StyleDetail
{
	public string Display { get; set; }
	public string Position { get; set; }
	public string FlexDirection { get; set; }
	public string Width { get; set; }
	public string Height { get; set; }
	public string Left { get; set; }
	public string Top { get; set; }
	public string Right { get; set; }
	public string Bottom { get; set; }
	public float? FlexGrow { get; set; }
	public float? FlexShrink { get; set; }
	public string Overflow { get; set; }
	public float? Opacity { get; set; }
	public string PointerEvents { get; set; }
	public string BackgroundColor { get; set; }
	public string FontSize { get; set; }
	public string FontFamily { get; set; }
	public string Color { get; set; }
}
