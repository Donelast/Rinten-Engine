using Facepunch.ActionGraphs;
using Rinten;
using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using UILabel = Rinten.UI.Label;

namespace Editor.ActionGraphs;

/// <summary>The dock slot the error list is shown in.</summary>
public class ErrorList : PanelDockHost
{
	public ErrorListPanel List { get; }

	public ErrorList( Widget parent, MainWindow editor ) : base( parent )
	{
		Name = "ErrorList";
		WindowTitle = "Error List";
		MinimumSize = new Vector2( 100, 100 );

		SetWindowIcon( "error" );

		List = new ErrorListPanel( editor );

		SetContent( List );
	}
}

/// <summary>Everything the open graphs have to complain about, and three switches for which kinds of complaint to show.</summary>
[StyleSheet( "ErrorList.cs.scss" )]
public class ErrorListPanel : Panel
{
	internal static Color WarningColor => Theme.Yellow;
	internal static Color ErrorColor => Theme.Red;
	internal static Color InfoColor => Theme.Blue;

	public List<ValidationMessage> Messages { get; } = new();

	public MainWindow Editor { get; }

	private readonly LevelToggle _errors;
	private readonly LevelToggle _warnings;
	private readonly LevelToggle _info;
	private readonly Panel _list;

	private int _lastHash;

	public ErrorListPanel( MainWindow editor )
	{
		Editor = editor;

		AddClass( "error-list" );

		var toolbar = AddChild<Panel>( "toolbar" );

		_errors = toolbar.AddChild( new LevelToggle( "error", ErrorColor, "Toggle display of errors", Refresh ) );
		_warnings = toolbar.AddChild( new LevelToggle( "warning", WarningColor, "Toggle display of warnings", Refresh ) );
		_info = toolbar.AddChild( new LevelToggle( "info", InfoColor, "Toggle display of information", Refresh ) );

		toolbar.AddChild<Panel>( "spacer" );

		var clear = toolbar.AddChild<IconPanel>( "clear" );
		clear.Text = "delete";
		clear.Tooltip = "Clear error list";
		clear.AddEventListener( "onclick", () =>
		{
			Messages.Clear();
			Refresh();
		} );

		_list = AddChild<Panel>( "list" );
	}

	public override void Tick()
	{
		base.Tick();

		Collect();
	}

	//Gathers what the open graphs are complaining about, and rebuilds the list only when that has actually changed.
	private void Collect()
	{
		Messages.Clear();

		foreach ( var view in Editor.OpenViews )
		{
			Messages.AddRange( view.ActionGraph.Messages
				.Where( x => GetContextNode( x.Context ) is not { Parent: not null } ) );
		}

		var hash = 0;

		foreach ( var message in Messages )
		{
			hash = HashCode.Combine( hash, message.GetHashCode() );
		}

		hash = HashCode.Combine( hash, _errors.On, _warnings.On, _info.On );

		if ( _lastHash == hash ) return;

		_lastHash = hash;

		Refresh();
	}

	//Rebuilds the rows from what has been gathered.
	private void Refresh()
	{
		var q = Messages.AsEnumerable();

		_errors.Count = q.Count( x => x.Level == MessageLevel.Error );
		_warnings.Count = q.Count( x => x.Level == MessageLevel.Warning );
		_info.Count = q.Count( x => x.Level == MessageLevel.Info );

		if ( !_errors.On ) q = q.Where( x => x.Level != MessageLevel.Error );
		if ( !_warnings.On ) q = q.Where( x => x.Level != MessageLevel.Warning );
		if ( !_info.On ) q = q.Where( x => x.Level != MessageLevel.Info );

		_list.DeleteChildren( true );

		foreach ( var message in q.OrderByDescending( x => x.Level == MessageLevel.Error ) )
		{
			_list.AddChild( new ErrorRow( this, message ) );
		}
	}

	private static Node GetContextNode( IMessageContext ctx )
	{
		while ( ctx is not null )
		{
			if ( ctx is Node node ) return node;

			ctx = ctx.Parent;
		}

		return null;
	}

	//Shows what a message is about, and takes the view there.
	internal void SelectContext( IMessageContext context )
	{
		switch ( context )
		{
			case Node node:
				Editor.SelectNode( node );
				break;

			case Node.Property property:
				Editor.SelectNode( property.Node );
				break;

			case Node.Input input:
				if ( input.IsLinked ) Editor.SelectLinks( input.Links );
				else Editor.SelectNode( input.Node );
				break;

			case Node.Output output:
				Editor.SelectNode( output.Node );
				break;

			case Link link:
				Editor.SelectLink( link );
				break;
		}

		Editor.FocusedView?.CenterOnSelection();
	}

	internal static string FormatContext( IMessageContext context ) => context switch
	{
		Node node => node.GetDisplayInfo().Name,
		Node.Property property => $"{property.Display.Title} - {FormatContext( property.Node )}",
		Node.Input input => $"{input.Display.Title} - {FormatContext( input.Node )}",
		Node.Output output => $"{output.Display.Title} - {FormatContext( output.Node )}",
		Link link => FormatContext( link.Target ),
		ActionGraph actionGraph => actionGraph.DisplayInfo.Title,
		Variable variable => variable.Name,
		_ => context.ToString()
	};
}

/// <summary>One of the three switches over the list: how many, and whether shown.</summary>
public sealed class LevelToggle : Panel
{
	private readonly Color _colour;
	private readonly Action _changed;
	private readonly IconPanel _iconPanel;
	private readonly UILabel _text;

	private readonly string _noun;

	public bool On { get; private set; } = true;

	public int Count
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			_text.Text = $"{value} {_noun}";
		}
	}

	public LevelToggle( string icon, Color colour, string tooltip, Action changed )
	{
		_colour = colour;
		_changed = changed;
		_noun = icon switch { "error" => "Errors", "warning" => "Warnings", _ => "Messages" };

		AddClass( "level-toggle" );

		Tooltip = tooltip;

		_iconPanel = AddChild<IconPanel>( "icon" );
		_iconPanel.Text = icon;
		_iconPanel.Style.FontColor = colour;

		_text = AddChild<UILabel>( "text" );
		_text.Tokenize = false;
		_text.Text = $"0 {_noun}";

		Apply();
	}

	protected override void OnClick( MousePanelEvent e )
	{
		On = !On;

		Apply();

		_changed?.Invoke();

		e.StopPropagation();
	}

	private void Apply()
	{
		SetClass( "on", On );

		_iconPanel.Style.FontColor = _colour.WithAlpha( On ? 1.0f : 0.7f );
	}
}

/// <summary>One complaint: what it is, and what it is about.</summary>
public sealed class ErrorRow : Panel
{
	private readonly ErrorListPanel _list;
	private readonly ValidationMessage _message;

	public ErrorRow( ErrorListPanel list, ValidationMessage message )
	{
		_list = list;
		_message = message;

		var (colour, icon) = message.Level switch
		{
			MessageLevel.Error => (ErrorListPanel.ErrorColor, "error"),
			MessageLevel.Warning => (ErrorListPanel.WarningColor, "warning"),
			_ => (ErrorListPanel.InfoColor, "info"),
		};

		AddClass( "error-row" );

		Style.BackgroundColor = colour.WithAlpha( 0.03f );

		var mark = AddChild<IconPanel>( "mark" );
		mark.Text = icon;
		mark.Style.FontColor = colour.WithAlpha( 0.7f );

		var words = AddChild<Panel>( "words" );

		var value = words.AddChild<UILabel>( "message" );
		value.Tokenize = false;
		value.Text = message.Value;

		var context = words.AddChild<UILabel>( "context" );
		context.Tokenize = false;
		context.Text = ErrorListPanel.FormatContext( message.Context );
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		_list.SelectContext( _message.Context );

		e.StopPropagation();
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		e.StopPropagation();

		var menu = new ContextMenu( null );

		menu.AddOption( "Show Context", "file_open", () => _list.SelectContext( _message.Context ) );
		menu.AddOption( "Copy Error", "content_copy", () => EditorUtility.Clipboard.Copy( _message.Value ) );

		menu.OpenAtCursor();
	}
}
