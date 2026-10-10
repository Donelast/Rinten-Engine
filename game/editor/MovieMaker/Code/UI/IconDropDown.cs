using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

//A frame rate, an interpolation curve and a player component have nothing in common but the shape of the control, so what the chosen value looks like is a panel the subclass builds - a label, an icon, a drawn curve.
/// <summary>A value picked from a short list, shown as a picture of itself rather than its name.</summary>
public abstract class IconDropDown<T> : Panel
{
	private readonly Func<T> _getValue;
	private readonly Action<T> _setValue;
	private readonly Panel _preview;

	private T _shown = default!;
	private bool _hasShown;

	private MovieDropDownPopup? _popup;

	protected IconDropDown( string? toolTip, Func<T> getValue, Action<T> setValue )
	{
		_getValue = getValue;
		_setValue = setValue;

		AddClass( "movie-icon-dropdown" );

		if ( !string.IsNullOrEmpty( toolTip ) ) Tooltip = toolTip;

		_preview = AddChild<Panel>( "preview" );

		AddChild<IconPanel>( "arrow" ).Text = "expand_more";
	}

	public T Value
	{
		get => _getValue();
		set => _setValue( value );
	}

	public override void Tick()
	{
		base.Tick();

		var value = _getValue();

		if ( _hasShown && Equals( _shown, value ) ) return;

		_shown = value;
		_hasShown = true;

		_preview.DeleteChildren( true );

		OnBuildPreview( _preview, value );
	}

	//Everything that can be chosen, in the order it is offered.
	protected abstract IEnumerable<T> OnGetOptions();

	//What one option is called in the list.
	protected abstract string OnGetOptionTitle( T option );

	//Builds what the chosen value looks like, into an empty panel.
	protected abstract void OnBuildPreview( Panel host, T value );

	//Fills the open list. Adds every option by default.
	protected virtual void OnBuildPopup( MovieDropDownPopup popup )
	{
		foreach ( var option in OnGetOptions() )
		{
			var value = option;
			var entry = popup.AddOption( OnGetOptionTitle( value ), () => Value = value );

			entry.SetClass( "active", Equals( Value, value ) );
		}
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		Open();

		e.StopPropagation();
	}

	private void Open()
	{
		if ( _popup.IsValid() )
		{
			_popup.Delete();
			_popup = null;
			return;
		}

		var popup = new MovieDropDownPopup();

		popup.SetPositioning( this, Popup.PositionMode.BelowLeft, 2f );
		popup.CloseWhenParentIsHidden = true;

		OnBuildPopup( popup );

		_popup = popup;
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		_popup?.Delete();
		_popup = null;
	}
}

/// <summary>An <see cref="IconDropDown{T}"/> over the members of an enum, shown by their icons.</summary>
public class EnumIconDropDown<T> : IconDropDown<T>
	where T : struct, Enum
{
	public EnumIconDropDown( string? toolTip, Func<T> getValue, Action<T> setValue )
		: base( toolTip, getValue, setValue )
	{
	}

	protected static EnumDescription.Entry GetEntry( T option ) =>
		EditorTypeLibrary.GetEnumDescription( typeof( T ) ).GetEntry( option );

	protected override IEnumerable<T> OnGetOptions() =>
		Enum.GetValues<T>().Where( x => Convert.ToInt64( x ) >= 0 );

	protected override string OnGetOptionTitle( T option )
	{
		var entry = GetEntry( option );

		return entry.Title ?? entry.Name.ToTitleCase();
	}

	protected override void OnBuildPreview( Panel host, T value )
	{
		host.AddChild<IconPanel>().Text = GetEntry( value ).Icon;
	}
}
