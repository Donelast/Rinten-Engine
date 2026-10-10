using Rinten.Audio;

namespace Editor.Audio;

[Dock( "Editor", "Mixer", "settings_input_component" )]
public class MixerDock : PanelWidget
{
	readonly MixerDockPanel root;
	bool _isDirty;
	int _cleanHash;
	int _wireHash;

	public MixerDock( Widget parent ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.Flexible;
		FocusMode = FocusMode.TabOrClickOrWheel;

		root = new MixerDockPanel();
		SetPanel( root );

		_cleanHash = GetMixerHash();
		_wireHash = GetWireHash();
		WireChanges();

		EditorEvent.Register( this );
	}

	void SetDirty()
	{
		if ( _isDirty ) return;

		_isDirty = true;
		UpdateWindowTitle();
	}

	void MarkClean()
	{
		_isDirty = false;
		_cleanHash = GetMixerHash();
		UpdateWindowTitle();
	}

	[Shortcut( "editor.save", "CTRL+S" )]
	void SaveMixer()
	{
		var ms = new MixerSettings();
		ms.Mixers = Mixer.Master.Serialize();

		EditorUtility.SaveProjectSettings( ms, "Mixer.config" );
		MarkClean();
	}

	void Restore()
	{
		var ms = EditorUtility.LoadProjectSettings<MixerSettings>( "Mixer.config" );
		Mixer.Master.Deserialize( ms.Mixers, TypeLibrary );
		root?.StateHasChanged();
		_wireHash = GetWireHash();
		WireChanges();
		MarkClean();
	}

	void UpdateWindowTitle()
	{
		WindowTitle = $"Mixer{(_isDirty ? "*" : "")}";
		var window = GetWindow();
		if ( window.WindowTitle.StartsWith( "Mixer" ) && window.WindowTitle.Length < 7 )
			window.WindowTitle = WindowTitle;
	}

	protected override bool OnClose()
	{
		if ( !_isDirty )
			return true;

		var confirm = new PopupWindow(
			"Save Mixer", "The mixer has unsaved changes. Would you like to save now?", "Cancel",
			new Dictionary<string, System.Action>()
			{
				{ "No", () => { Restore(); Destroy(); } },
				{ "Yes", () => { SaveMixer(); Destroy(); } }
			}
		);

		confirm.Show();
		return false;
	}

	[EditorEvent.Frame]
	void Frame()
	{
		var hash = GetWireHash();
		if ( hash != _wireHash )
		{
			_wireHash = hash;
			WireChanges();
		}

		if ( _isDirty ) return;

		//The net under the property events above serialises the whole mixer to JSON, so not every frame.
		if ( sinceHashed < 0.5f ) return;

		sinceHashed = 0;

		if ( GetMixerHash() != _cleanHash )
			SetDirty();
	}

	RealTimeSince sinceHashed;

	static int GetMixerHash()
	{
		if ( Mixer.Master is null ) return 0;
		return Mixer.Master.Serialize().ToJsonString().FastHash();
	}

	static int GetWireHash() => MixerTreePanel.GetStructureHash( Mixer.Master );

	void WireChanges() => WireChanges( Mixer.Master );

	void WireChanges( Mixer mixer )
	{
		if ( mixer is null ) return;

		mixer.GetSerialized().OnPropertyChanged = _ => SetDirty();

		foreach ( var processor in mixer.GetProcessors() )
			processor.GetSerialized().OnPropertyChanged = _ => SetDirty();

		foreach ( var child in mixer.GetChildren() )
			WireChanges( child );
	}
}
