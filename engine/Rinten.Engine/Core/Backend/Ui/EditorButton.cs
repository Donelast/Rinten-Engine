using Rinten.UI;
using System;

namespace Rinten;

//A button, as a panel.
internal sealed class EditorButton : Panel
{
	private readonly Label label;

	/// <summary>What happens when it is pressed.</summary>
	public Action Pressed { get; set; }

	public EditorButton()
	{
		AddClass( "button" );

		label = AddChild<Label>();
	}

	/// <summary>What it says.</summary>
	public string Text
	{
		get => label.Text;
		set => label.Text = value;
	}

	/// <summary>Whether it can be pressed. A disabled button still lays out and still says what it says - it simply does not answer.</summary>
	public bool Disabled
	{
		get => HasClass( "disabled" );
		set => SetClass( "disabled", value );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		if ( Disabled ) return;

		Pressed?.Invoke();
	}
}
