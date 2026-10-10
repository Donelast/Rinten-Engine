using Rinten.UI;
using System;

namespace Editor.MovieMaker;

#nullable enable

//Adding sub-tracks one at a time means closing and reopening the menu for each one, so these tick in place. The delete button is for presets, which are the only options you can also throw away.
/// <summary>An option in a menu that stays open when it is ticked.</summary>
[StyleSheet( "TrackToggleOptionPanel.cs.scss" )]
public sealed class TrackToggleOptionPanel : Panel
{
	private readonly Rinten.UI.Label _label;
	private readonly Action<bool> _toggled;

	private bool _isActive;

	public string Title
	{
		get => _label.Text;
		set => _label.Text = value;
	}

	public bool IsActive
	{
		get => _isActive;
		set
		{
			_isActive = value;

			SetClass( "active", value );
		}
	}

	public TrackToggleOptionPanel( string title, bool active, Action<bool> toggled, Action? deleted = null )
	{
		_toggled = toggled;

		AddClass( "track-toggle-option" );

		AddChild<IconPanel>( "tick" ).Text = "done";

		_label = AddChild<Rinten.UI.Label>( "title" );
		_label.Text = title;

		IsActive = active;

		if ( deleted is null ) return;

		var remove = AddChild<IconPanel>( "remove" );

		remove.Text = "delete";
		remove.AddEventListener( "onclick", () =>
			Dialog.AskConfirm( deleted, $"Are you sure you want to delete {title}?", "Delete Confirmation" ) );
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		IsActive = !IsActive;

		_toggled( IsActive );

		e.StopPropagation();
	}
}
