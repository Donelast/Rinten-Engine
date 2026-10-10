using Rinten.UI;
using System.Collections.Generic;
using System.Linq;

using UILabel = Rinten.UI.Label;

namespace Editor.Inspectors;

/// <summary>A non-component inspector card: header bar plus body, foldable with state saved per project</summary>
public class InspectorCard : Panel
{
	readonly Panel _header;
	readonly UILabel _chevron;
	readonly UILabel _glyph;
	readonly UILabel _title;
	readonly BoolControl _switch;
	readonly SerializedProperty _toggle;

	//What the switch was when the card was last folded or opened.
	bool _shownOn;

	/// <summary>Everything under the bar.</summary>
	public Panel Content { get; }

	/// <summary>What the card holds that is built again on its own - inside <see cref="Content"/>, after whatever else is there.</summary>
	public Panel Body { get; }

	readonly string _cookie;

	bool Expanded
	{
		get => _cookie is null || ProjectCookie.Get( _cookie, true );
		set
		{
			if ( _cookie is null ) return;

			if ( value ) ProjectCookie.Remove( _cookie );
			else ProjectCookie.Set( _cookie, value );
		}
	}

	/// <summary>What the bar says.</summary>
	public string Title
	{
		get => _title.Text;
		set => _title.Text = value;
	}

	/// <summary>The glyph in the bar's first cell.</summary>
	public string Icon
	{
		get => _glyph.Text;
		set => _glyph.Text = value;
	}

	public InspectorCard( string title, string icon, SerializedProperty toggle = null, string foldKey = null )
	{
		AddClass( "inspector-card" );

		_cookie = foldKey is null ? null : $"inspector.card.{foldKey}.expanded";

		_header = AddChild<Panel>( "inspector-card-header" );
		_header.SetClass( "fixed", foldKey is null );

		_chevron = _header.AddChild<UILabel>( "chevron material-icon" );

		_glyph = _header.AddChild<UILabel>( "icon material-icon" );
		_glyph.Text = icon;

		_toggle = toggle;

		if ( toggle is not null )
		{
			_switch = _header.AddChild<BoolControl>( "enabled" );
			_switch.Property = toggle;
		}

		_title = _header.AddChild<UILabel>( "title" );
		_title.Text = title;
		_title.Tokenize = false;

		Content = AddChild<Panel>( "content" );
		Body = Content.AddChild<Panel>( "body" );

		_header.AddEventListener( "onmousedown", OnHeaderDown );

		Refold();
	}

	void OnHeaderDown( PanelEvent e )
	{
		if ( _cookie is null ) return;
		if ( e is not MousePanelEvent mouse || mouse.MouseButton != MouseButtons.Left ) return;

		//The switch is a control of its own; pressing it is not folding the card.
		if ( _switch is not null && (e.Target as Panel)?.IsAncestor( _switch ) == true ) return;

		Expanded = !Expanded;
		Refold();
		e.StopPropagation();
	}

	//Whether the switch in the bar is on, or true where there is none.
	bool On => _toggle is null || _toggle.GetValue<bool>();

	public override void Tick()
	{
		base.Tick();

		//The switch is the card's to show: off, what it holds is nothing the thing reads,
		//and the card is its bar - the way a component that is switched off reads as one.
		if ( On != _shownOn ) Refold();
	}

	/// <summary>Opens or folds the card, as pressing its bar does - for the inspector's bar to do to every card at once. A card that does not fold stays as it is.</summary>
	public void SetExpanded( bool open )
	{
		if ( _cookie is null || Expanded == open ) return;

		Expanded = open;
		Refold();
	}

	/// <summary>Show or hide what the card holds, after it was folded or what it holds changed.</summary>
	public void Refold()
	{
		var filled = Body.ChildrenCount > 0 || Content.ChildrenCount > 1;

		_shownOn = On;

		var open = filled && Expanded && _shownOn;

		Content.SetClass( "hidden", !open );
		_header.SetClass( "collapsed", !open );
		_chevron.SetClass( "hidden", !filled || _cookie is null );
		_chevron.Text = Expanded ? "expand_more" : "chevron_right";
	}

	/// <summary>Lays properties out GameObject-style: ungrouped rows, then a card per group ([ToggleGroup] in bar)</summary>
	public static void Lay( Panel loose, Panel cards, IEnumerable<SerializedProperty> properties, string foldKey )
	{
		var all = properties
			.Where( x => !x.HasAttribute<HideAttribute>() )
			.Where( x => !x.IsMethod || x.HasAttribute<ButtonAttribute>() )
			.OrderBy( x => x.Order )
			.ThenBy( x => x.SourceFile )
			.ThenBy( x => x.SourceLine )
			.ToList();

		if ( all.Count == 0 ) return;

		if ( all.Any( x => x.HasAttribute<FeatureAttribute>() ) )
		{
			var tabs = new ControlSheet();
			tabs.BuildProperties( all );

			var card = cards.AddChild( new InspectorCard( "Properties", "tune", null, $"{foldKey}.properties" ) );
			card.Body.AddChild( tabs );
			card.Refold();
			return;
		}

		var groups = all.GroupBy( x => x.GroupName )
			.OrderBy( x => x.Key is not null )
			.ThenBy( x => x.Max( y => y.Order ) )
			.ThenBy( x => x.Key );

		foreach ( var group in groups )
		{
			var rows = group.ToList();

			if ( string.IsNullOrWhiteSpace( group.Key ) )
			{
				Rows( loose, rows );
				continue;
			}

			var toggle = rows.FirstOrDefault( x => x.Name == group.Key && x.HasAttribute<ToggleGroupAttribute>() );
			var title = group.Key;

			if ( toggle is not null )
			{
				rows.Remove( toggle );

				if ( toggle.TryGetAttribute<ToggleGroupAttribute>( out var attribute ) && !string.IsNullOrWhiteSpace( attribute.Label ) )
					title = attribute.Label;
			}

			var groupCard = cards.AddChild( new InspectorCard( title, "tune", toggle, $"{foldKey}.{group.Key}" ) );
			Rows( groupCard.Body, rows );
			groupCard.Refold();
		}
	}

	//Rows straight into a panel, under no heading of their own.
	static void Rows( Panel into, List<SerializedProperty> rows )
	{
		if ( rows.Count == 0 ) return;

		var sheet = into.AddChild( new ControlSheet { ShowPropertyNameInTooltip = true } );

		foreach ( var property in rows )
			sheet.AddPropertyRow( property );
	}
}
