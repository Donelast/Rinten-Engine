using System;
using Rinten.UI;

namespace Editor;

/// <summary>A widget that is markup and a stylesheet rather than a paint method.</summary>
public class PanelWidget : Widget
{
	/// <summary>What this widget is made of.</summary>
	public Panel Panel { get; private set; }

	/// <summary>Height follows the panel's content instead of the arranger's offer; off for dock slots</summary>
	public bool SizeToContent
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			VerticalSizeMode = value ? SizeMode.CanGrow : SizeMode.Flexible;

			Panel?.SetClass( "panel-widget-content-auto", value );

			AdjustSize();
		}
	}

	//The height the panel last worked out for itself.
	private float contentHeight;

	public PanelWidget( Widget parent = null ) : base( parent )
	{
		//Panel tree inside handles input; the surface is only geometry + chrome.
		TransparentForMouseEvents = true;
	}

	public PanelWidget( Widget parent, Panel panel ) : base( parent )
	{
		TransparentForMouseEvents = true;

		SetPanel( panel );
	}

	//Puts a panel in this widget, in place of whatever was in it. Separate from the constructor because a panel usually wants the widget
	//it is going into - a toolbar reads the view it belongs to - and a subclass has nothing to hand it until its own fields are set.
	protected void SetPanel( Panel panel )
	{
		if ( Panel == panel ) return;

		Panel?.Delete( true );
		Panel = panel;

		AttachPanel();
	}

	void AttachPanel()
	{
		if ( Panel is null || Surface is null || !Panel.IsValid() ) return;

		//The widget's box is the panel's whole world: outside it things are arranged by the editor's layouts, inside it by flexbox.
		Panel.AddClass( "panel-widget-content" );
		Panel.SetClass( "panel-widget-content-auto", SizeToContent );

		if ( Panel.Parent != Surface )
			Surface.AddChild( Panel );
	}

	protected override Vector2 SizeHint()
	{
		var hint = base.SizeHint();

		return SizeToContent ? hint.WithY( contentHeight ) : hint;
	}

	//Asks the panel how tall it turned out, and takes that height. Every frame rather than on a signal: a panel grows when a
	//list under it is filled, when a fold opens, when a label wraps, and none of those tell the widget around them anything.
	[EditorEvent.Frame]
	private void MeasureContent()
	{
		if ( !SizeToContent || !IsValid || Panel is not { IsValid: true } panel )
			return;

		var height = panel.Box.Rect.Height * panel.ScaleFromScreen;

		if ( height <= 0 || MathF.Abs( height - contentHeight ) < 0.5f )
			return;

		contentHeight = height;

		AdjustSize();
	}

	private protected override void OnSurfaceCreated() => AttachPanel();

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		Panel?.Delete( true );
		Panel = null;
	}
}
