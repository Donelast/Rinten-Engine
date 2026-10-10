using Editor.Inspectors;

namespace Editor.Preferences;

//One shortcut in the list: what it does, what it is bound to, and the ways back.
sealed class KeybindRowPanel : Panel
{
	readonly ShortcutBinding binding;
	readonly Rinten.UI.Label warning;
	readonly ToolbarButton revert;
	readonly ToolbarButton clear;

	public KeybindRowPanel( ShortcutBinding binding, int row )
	{
		this.binding = binding;

		AddClass( "keybind-row" );
		SetClass( "alt", row % 2 == 1 );

		var name = AddChild<Rinten.UI.Label>( "name" );
		name.Text = binding.Name;

		//Kept in the row whether it says anything or not: an icon that comes and goes moves
		//everything after it, and the field then sits in a different place on every row.
		warning = AddChild<Rinten.UI.Label>( "warning" );
		warning.AddClass( "material-icon" );

		revert = AddChild<ToolbarButton>();
		revert.Icon = "history";
		revert.ToolTip = "Revert to default";
		revert.OnPressed = () =>
		{
			binding.Revert();
			Refresh();
		};

		clear = AddChild<ToolbarButton>();
		clear.Icon = "clear";
		clear.ToolTip = "Clear";
		clear.OnPressed = () =>
		{
			binding.Clear();
			Refresh();
		};

		//In a slot of its own, so every row's field is the same width: the control fills whatever it is given, and what it is given is written once.
		var slot = AddChild<Panel>( "keys" );

		var keys = slot.AddChild<KeyBindControl>();
		var serialized = EditorTypeLibrary.GetSerializedObject( binding );
		serialized.OnPropertyChanged = _ => Refresh();
		keys.Property = serialized.GetProperty( nameof( ShortcutBinding.Keys ) );

		Refresh();
	}

	/// <summary>What the shortcut is bound to now, and so what can be done to it.</summary>
	public void Refresh()
	{
		binding.Reread();

		var conflicts = binding.Conflicts();

		var shared = conflicts.Count > 0;

		warning.Text = shared ? "warning" : "";
		warning.Tooltip = shared
			? "Shared with " + string.Join( ", ", conflicts.Select( x => $"\"{x}\"" ) )
			: null;

		revert.Disabled = !binding.IsOverridden;
		clear.Disabled = !binding.CanClear;
	}
}
