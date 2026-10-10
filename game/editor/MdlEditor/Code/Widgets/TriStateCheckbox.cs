using Rinten.UI;
using System;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Checkbox with a third "partial" state (CheckState), styled as design.scss's .checkbox
[StyleSheet( "TriStateCheckbox.cs.scss" )]
sealed class TriStateCheckbox : Panel
{
	readonly UILabel _mark;
	readonly UILabel _label;

	/// <summary>Called with the new state whenever it changes, by a click or from code.</summary>
	public Action<CheckState> StateChanged { get; set; }

	/// <summary>Which of the three states the box is in.</summary>
	public CheckState State
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			Read();
			StateChanged?.Invoke( field );
		}
	}

	/// <summary>The words beside the box. Nothing shows where there are none.</summary>
	public string LabelText
	{
		get => _label.Text;
		set
		{
			_label.Text = value ?? "";
			_label.SetClass( "hidden", string.IsNullOrEmpty( value ) );
		}
	}

	public TriStateCheckbox( string label = null )
	{
		AddClass( "checkbox tri-state" );

		_mark = AddChild<UILabel>( "material-icon checkmark" );
		_label = AddChild<UILabel>( "text" );

		LabelText = label;
		Read();
	}

	//Says which state it is in, on itself and on the glyph in the box. The glyph is always there and the sheet colours it away when the
	//box is empty - which is how .checkbox already works, so an empty box is the same size as a ticked one everywhere in the editor.
	void Read()
	{
		SetClass( "checked", State == CheckState.On );
		SetClass( "partial", State == CheckState.Partial );

		//A tick for all of it, a bar for some of it.
		_mark.Text = State == CheckState.Partial ? "remove" : "check";
	}

	//Off, some, all, off again - the cycle the widget had. Kept rather than skipping the middle stop: what reads the state decides
	//what "some" does when it is asked for, and a caller that treats anything but off as on gets the same two-step it always had.
	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		State = State switch
		{
			CheckState.Off => CheckState.Partial,
			CheckState.Partial => CheckState.On,
			_ => CheckState.Off,
		};

		e.StopPropagation();
	}
}
