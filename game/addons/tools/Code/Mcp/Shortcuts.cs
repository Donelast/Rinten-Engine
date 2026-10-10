using Rinten.UI;

namespace Editor.Mcp;

/// <summary>Why a keyboard shortcut does or does not fire, read off the live editor.</summary>
[McpToolset( "shortcuts", "Why the editor's keyboard shortcuts do or do not fire" )]
public static class ShortcutTools
{
	static readonly List<string> seen = new();
	static readonly HashSet<string> down = new();
	static string lastFocus;

	[EditorEvent.Frame]
	public static void Frame()
	{
		var focus = Application.FocusWidget;
		var name = focus is null ? "none" : Chain( focus );

		if ( name != lastFocus )
		{
			lastFocus = name;
			Note( $"focus -> {name}" );
		}

		foreach ( var entry in EditorShortcuts.Entries )
		{
			if ( entry.IsDown )
			{
				if ( down.Add( entry.Identifier ) ) Note( $"key down for {entry.Identifier} ({entry.Keys})" );
			}
			else
			{
				down.Remove( entry.Identifier );
			}
		}
	}

	static void Note( string what )
	{
		seen.Add( $"{RealTime.Now:F1}s {what}" );

		if ( seen.Count > 200 ) seen.RemoveAt( 0 );
	}

	static string Chain( Widget widget )
	{
		var parts = new List<string>();

		for ( var w = widget; w is not null && parts.Count < 16; w = w.Parent )
			parts.Add( w.GetType().Name );

		return string.Join( " < ", parts );
	}

	static string PanelChain( Panel panel )
	{
		var parts = new List<string>();

		for ( var p = panel; p is not null && parts.Count < 16; p = p.Parent )
		{
			var raw = p is IWantsRawKeys { WantsRawKeys: true } ? " raw" : "";
			var ime = p.AcceptsImeInput ? " ime" : "";
			parts.Add( $"{p.GetType().Name}{raw}{ime}" );
		}

		return string.Join( " < ", parts );
	}

	/// <summary>Whether shortcuts are allowed right now, what has the focus, whether any scene view contains it, and what keys and focus changes were seen lately.</summary>
	[McpTool.ReadOnly( "shortcut_status" )]
	public static object Status()
	{
		var focus = Application.FocusWidget;
		var panel = InputFocus.Current;

		var views = SceneViewPanel.All.Select( v => new
		{
			Valid = v.IsValid(),
			Visible = v.IsVisible,
			//A scene view is a panel here, so the focused widget is compared with the one hosting it rather than with the view itself.
			ContainsFocus = v.HasFocus
				|| ( EditorUtility.WidgetOf( v ) is { } host && ( focus?.IsDescendantOf( host ) ?? false ) ),
		} ).ToArray();

		return new
		{
			EditorShortcuts.AllowShortcuts,
			Entries = EditorShortcuts.Entries.Count,
			FocusWidget = focus is null ? "none" : Chain( focus ),
			FocusWidgetWantsRawKeys = focus?.WantsRawKeys ?? false,
			FocusedPanel = panel is null ? "none" : PanelChain( panel ),
			SceneViews = views,
			EditorToolManager.CurrentModeName,
			Tool = SceneViewPanel.Current?.Tools?.CurrentTool?.GetType().Name,
			SubTool = SceneViewPanel.Current?.Tools?.CurrentSubTool?.GetType().Name,
			Recent = seen.ToArray(),
		};
	}

	/// <summary>The registered shortcuts whose identifier or keys contain the query.</summary>
	[McpTool.ReadOnly( "shortcut_find" )]
	public static object Find( string query = "" )
	{
		return EditorShortcuts.Entries
			.Where( e => string.IsNullOrEmpty( query )
				|| e.Identifier.Contains( query, StringComparison.OrdinalIgnoreCase )
				|| (e.Keys?.Contains( query, StringComparison.OrdinalIgnoreCase ) ?? false) )
			.Select( e => new
			{
				e.Identifier,
				e.Keys,
				e.DisplayKeys,
				Type = e.Attribute.Type.ToString(),
				Target = e.Attribute.TargetOverride?.Name,
			} )
			.ToArray();
	}

	/// <summary>Fires a shortcut by identifier the way a key press would, and says whether it went. Pass force to skip the focus gate, which is what separates "the action does nothing" from "the window is not focused".</summary>
	[McpTool( "shortcut_invoke" )]
	public static object Invoke( string identifier, bool force = false )
	{
		var entry = EditorShortcuts.Entries.FirstOrDefault( e => e.Identifier == identifier );

		return new
		{
			Found = entry is not null,
			Fired = entry?.Invoke( force ) ?? false,
			EditorToolManager.CurrentModeName,
			Tool = SceneViewPanel.Current?.Tools?.CurrentTool?.GetType().Name,
			SubTool = SceneViewPanel.Current?.Tools?.CurrentSubTool?.GetType().Name,
		};
	}
}
