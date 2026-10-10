using Editor.Preferences;
using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//Keys and the pointer: what the editor hears, and the list of what it is bound to.
static class InputExamples
{
	const string Page = "Input";

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Key echo" ), Icon( "keyboard" )]
	[Description( "Click the box, then press keys: the last button event, with its modifiers." )]
	static Panel KeyEcho()
	{
		var example = Example.Column();

		example.AddChild<KeyEchoPanel>();

		return example;
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Pointer echo" ), Icon( "mouse" )]
	[Description( "Where the pointer is over the box, in the box's own pixels, and which button is down." )]
	static Panel PointerEcho()
	{
		var example = Example.Column();

		example.AddChild<PointerEchoPanel>();

		return example;
	}

	[PanelGallery, Category( Page ), Order( 2 )]
	[Title( "Keybinds" ), Icon( "keyboard_command_key" )]
	[Description( "KeybindListPanel, the list from the preferences window: every shortcut the editor has, grouped, with its KeyBindControl." )]
	static Panel Keybinds()
	{
		var example = Example.Column();

		var list = example.AddChild<KeybindListPanel>();
		list.AddClass( "tall-height" );

		return example;
	}

	//A box that takes focus and writes down the last key it heard.
	sealed class KeyEchoPanel : Panel
	{
		readonly Label label;

		public KeyEchoPanel()
		{
			AddClass( "key-echo" );

			AcceptsFocus = true;

			label = Example.Text( this, "Click here, then press a key" );
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );

			Focus();
		}

		public override void OnButtonEvent( ButtonEvent e )
		{
			base.OnButtonEvent( e );

			var modifiers = new List<string>();

			if ( e.HasCtrl ) modifiers.Add( "Ctrl" );
			if ( e.HasShift ) modifiers.Add( "Shift" );
			if ( e.HasAlt ) modifiers.Add( "Alt" );

			modifiers.Add( e.Button );

			label.Text = $"{string.Join( "+", modifiers )} {(e.Pressed ? "down" : "up")}";
		}
	}

	//A box that writes down where the pointer is over it.
	sealed class PointerEchoPanel : Panel
	{
		readonly Label label;

		public PointerEchoPanel()
		{
			AddClass( "key-echo" );

			label = Example.Text( this, "Move the pointer over here" );
		}

		protected override void OnMouseMove( MousePanelEvent e )
		{
			base.OnMouseMove( e );

			label.Text = $"{e.LocalPosition.x:F0}, {e.LocalPosition.y:F0}";
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );

			label.Text = $"{e.MouseButton} down at {e.LocalPosition.x:F0}, {e.LocalPosition.y:F0}";
		}

		protected override void OnMouseOut( MousePanelEvent e )
		{
			base.OnMouseOut( e );

			label.Text = "Move the pointer over here";
		}
	}
}
