using Editor.ProjectSettingsUI;

namespace Editor;

internal sealed partial class ProjectSettingsWindow : Window
{
	ProjectSettingsPanel panel;
	Project currentProject;
	EditorModal popup;

	public Action<Project> OnSave { get; set; }
	public Action<SerializedProperty> OnPropertyChanged { get; set; }

	bool hasUnsavedChanges;

	public ProjectSettingsWindow( Project project, string page = null )
	{
		if ( project is null )
			return;

		Parent = EditorWindow;
		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint | WindowFlags.WindowTitle;

		currentProject = project;
		StatusBar.Visible = false;

		DeleteOnClose = true;
		WindowTitle = "Project Settings";
		Size = new Vector2( 1024, 768 );
		MinimumSize = new Vector2( 1024, 768 );

		SetWindowIcon( "settings" );

		panel = new ProjectSettingsPanel
		{
			Host = this,
			Project = project,
			InitialPage = page,
			OnSaveRequested = Save,
			OnRevertRequested = Reset,
			OnPropertyChanged = prop =>
			{
				OnPropertyChanged?.Invoke( prop );
				SetUnsavedChanges( true );
			},
			OnLeavingPage = ConfirmLeavingPage
		};

		Canvas = new ProjectSettingsBodyHost( this, panel );

		Show();
	}

	void SetUnsavedChanges( bool value )
	{
		hasUnsavedChanges = value;
		panel?.SetSaveEnabled( value );
	}

	void Reset()
	{
		SetUnsavedChanges( false );
		panel?.RemountCurrent();
	}

	[Shortcut( "editor.save", "CTRL+S" )]
	internal void Save()
	{
		panel?.SaveCategories();
		SetUnsavedChanges( false );
	}

	static ProjectSettingsWindow instance;

	public static void OpenForProject( Project project, string page = null )
	{
		if ( instance.IsValid() )
			instance.Close();

		instance = new ProjectSettingsWindow( project, page );
	}

	//Changing page with the one open unsaved asks first, the same as closing does - the
	//page being left is remounted when it is next opened, and what was typed into it goes.
	void ConfirmLeavingPage( Action leave, Action stay )
	{
		if ( !hasUnsavedChanges )
		{
			leave();
			return;
		}

		ShowUnsavedChangesPopup(
			onSave: () =>
			{
				Save();
				leave();
			},
			onDiscard: () =>
			{
				SetUnsavedChanges( false );
				leave();
			},
			onCancel: stay );
	}

	protected override bool OnClose()
	{
		if ( !hasUnsavedChanges )
			return true;

		ShowUnsavedChangesPopup(
			onSave: () =>
			{
				Save();
				Close();
			},
			onDiscard: () =>
			{
				hasUnsavedChanges = false;
				Close();
			} );

		return false;
	}

	void ShowUnsavedChangesPopup( Action onSave, Action onDiscard, Action onCancel = null )
	{
		if ( popup.IsValid() )
		{
			//Already asking; the question being answered will not be this one.
			onCancel?.Invoke();
			return;
		}

		//Over this window's own settings, which are what it is asking about.
		popup = EditorModal.Choose( panel, "Project Settings",
			"You have some unsaved settings, do you want to save them?", "Cancel",
			new[]
			{
				new EditorModal.Choice( "Don't Save", onDiscard, Destructive: true ),
				new EditorModal.Choice( "Save", onSave ),
			} );

		if ( popup is not null )
			popup.OnDismissed = onCancel;
	}
}
