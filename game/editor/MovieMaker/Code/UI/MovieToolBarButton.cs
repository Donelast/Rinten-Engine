using Rinten.UI;
using System;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>An icon in a toolbar group. What pressing it does is left to what derives from it.</summary>
public abstract class MovieToolBarButton : Panel
{
	private readonly IconPanel _icon;

	protected ToolBarItemDisplay Display { get; }

	/// <summary>The icon's colour while lit, where the stylesheet cannot know it - record is red.</summary>
	public Color? ActiveTint { get; set; }

	/// <summary>The plate behind the icon, where the button means something the stylesheet cannot know.</summary>
	public Color? Background
	{
		get => _background;
		set { _background = value; UpdateColors(); }
	}

	/// <summary>The plate behind the icon while lit.</summary>
	public Color? BackgroundActive
	{
		get => _backgroundActive;
		set { _backgroundActive = value; UpdateColors(); }
	}

	private Color? _background;
	private Color? _backgroundActive;

	private bool _active;
	private bool _enabled = true;

	protected MovieToolBarButton( ToolBarItemDisplay display )
	{
		Display = display;

		AddClass( "movie-toolbar-button" );
		SetClass( "plain", !display.Background );

		Tooltip = display.ToolTip;

		_icon = AddChild<IconPanel>();
		_icon.Text = display.Icon;
	}

	//Whether the thing this turns on is on.
	protected bool IsActive
	{
		get => _active;
		set
		{
			if ( _active == value ) return;

			_active = value;

			SetClass( "active", value );
			UpdateColors();
		}
	}

	//Whether it can be pressed at all.
	protected bool IsEnabled
	{
		get => _enabled;
		set
		{
			if ( _enabled == value ) return;

			_enabled = value;

			SetClass( "disabled", !value );
		}
	}

	//Written inline only where a call site names a colour; otherwise the sheet is left to say what the button looks like.
	private void UpdateColors()
	{
		_icon.Style.FontColor = _active && ActiveTint is { } tint ? tint : null;

		Style.BackgroundColor = _active ? _backgroundActive ?? _background : _background;
	}

	public override void Tick()
	{
		base.Tick();

		OnPoll();
	}

	//Reads whatever state this button shows, once a frame.
	protected virtual void OnPoll() { }

	//Pressed, and allowed to be.
	protected abstract void OnPress();

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		if ( !IsEnabled ) return;

		OnPress();
	}
}

/// <summary>A toolbar button that runs an action.</summary>
public sealed class MovieToolBarAction : MovieToolBarButton
{
	private readonly Action _action;
	private readonly Func<bool>? _isEnabled;

	public MovieToolBarAction( ToolBarItemDisplay display, Action action, Func<bool>? isEnabled = null )
		: base( display )
	{
		_action = action;
		_isEnabled = isEnabled;

		OnPoll();
	}

	protected override void OnPoll()
	{
		if ( _isEnabled is null ) return;

		IsEnabled = _isEnabled();
	}

	protected override void OnPress() => _action();
}

/// <summary>A toolbar button that shows and flips something that is either on or off.</summary>
public sealed class MovieToolBarToggle : MovieToolBarButton
{
	private readonly Func<bool> _getState;
	private readonly Action<bool> _setState;

	public MovieToolBarToggle( ToolBarItemDisplay display, Func<bool> getState, Action<bool> setState )
		: base( display )
	{
		_getState = getState;
		_setState = setState;

		OnPoll();
	}

	protected override void OnPoll() => IsActive = _getState();

	protected override void OnPress() => _setState( !_getState() );
}
