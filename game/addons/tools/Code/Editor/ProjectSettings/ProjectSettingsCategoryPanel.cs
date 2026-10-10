using Editor.Inspectors;
using Rinten.Internal;

namespace Editor.ProjectSettingsUI;

/// <summary>Base for a project settings content page hosted as a panel.</summary>
[StyleSheet( "ProjectSettingsCategoryPanel.cs.scss" )]
public abstract partial class ProjectSettingsCategoryPanel : Panel
{
	public Project Project { get; private set; }
	public Widget HostWindow { get; set; }

	protected Panel Body { get; set; }
	protected Action<Project> SaveCallback { get; private set; }
	protected Action<SerializedProperty> PropertyChanged { get; private set; }

	bool pendingInit;

	public virtual bool ShowTitle => true;

	public ProjectSettingsCategoryPanel()
	{
		AddClass( "project-settings-category" );
	}

	public void InitFromProject( Project project, Action<Project> saveCallback, Action<SerializedProperty> propertyCallback )
	{
		Project = project;
		SaveCallback = saveCallback;
		PropertyChanged = propertyCallback;
		pendingInit = true;
		TryRunInit();
	}

	protected override void OnAfterTreeRender( bool firstTime )
	{
		base.OnAfterTreeRender( firstTime );
		TryRunInit();
	}

	void TryRunInit()
	{
		if ( !pendingInit || Body is null || Project is null )
			return;

		pendingInit = false;
		OnInit( Project );
	}

	protected virtual void OnInit( Project project )
	{
	}

	protected void ListenForChanges( SerializedObject so )
	{
		so.OnPropertyChanged = StateHasChanged;
	}

	protected void StateHasChanged( SerializedProperty prop = null )
	{
		PropertyChanged?.Invoke( prop );
	}

	protected Editor.Inspectors.ControlSheet AddControlSheet( SerializedObject so )
	{
		var sheet = Body.AddChild<Editor.Inspectors.ControlSheet>();
		sheet.Target = so;
		sheet.Rebuild();
		ListenForChanges( so );
		return sheet;
	}

	protected Editor.Inspectors.ControlSheet AddControlSheet( object settings )
	{
		return AddControlSheet( settings.GetSerialized() );
	}

	protected Editor.Inspectors.ControlSheet AddControlSheet( IEnumerable<SerializedProperty> properties )
	{
		var sheet = Body.AddChild<Editor.Inspectors.ControlSheet>();
		sheet.Target = properties;
		sheet.Rebuild();
		return sheet;
	}

	protected void AddControlRows( Editor.Inspectors.ControlSheet sheet, SerializedObject so, params string[] names )
	{
		var props = names
			.Select( n => so.GetProperty( n ) )
			.Where( p => p is not null )
			.ToList();

		sheet.Target = props;
		sheet.Rebuild();
	}

	protected void AddControlGroup( Editor.Inspectors.ControlSheet sheet, string name, IEnumerable<SerializedProperty> properties )
	{
		sheet.AddGroup( new IControlSheet.Group( properties.ToList() ) { Name = name } );
		sheet.Rebuild();
	}

	protected Panel AddSectionHeader( string title )
	{
		var header = Body.AddChild<Panel>( "section-header" );
		header.AddClass( "settings-section-header" );
		var label = header.AddChild<Rinten.UI.Label>( "title" );
		label.Text = title;
		return header;
	}

	protected SettingsCalloutPanel AddCallout( string html, bool warning = false )
	{
		var callout = Body.AddChild<SettingsCalloutPanel>();
		callout.Html = html;
		callout.Warning = warning;
		return callout;
	}

	protected Panel AddButton( string text, string icon, Action onClick, bool primary = false )
	{
		var row = Body.AddChild<Panel>( "button-row" );
		row.AddClass( "settings-button-row" );

		var button = row.AddChild<Panel>( "btn" );
		button.AddClass( primary ? "good" : null );
		button.AddEventListener( "onclick", _ => onClick?.Invoke() );

		if ( !string.IsNullOrEmpty( icon ) )
		{
			var iconLabel = button.AddChild<Rinten.UI.Label>( "icon" );
			iconLabel.AddClass( "material-icon" );
			iconLabel.Text = icon;
		}

		var textLabel = button.AddChild<Rinten.UI.Label>( "text" );
		textLabel.Text = text;

		return button;
	}

	public virtual void OnSave()
	{
		EditorUtility.Projects.Updated( Project );
		SaveCallback?.Invoke( Project );
		EditorEvent.Run( "project.settings.saved" );
	}

	public static ProjectSettingsCategoryPanel Create( Type categoryType )
	{
		var typeInfo = EditorTypeLibrary.GetType( categoryType );
		return typeInfo?.Create<ProjectSettingsCategoryPanel>();
	}

	public static IEnumerable<SettingsTreeChildNode> GetTreeChildren( Type categoryType )
	{
		var instance = EditorTypeLibrary.GetType( categoryType )?.Create<ProjectSettingsCategoryPanel>();
		return instance?.GetTreeChildren();
	}

	public virtual IEnumerable<SettingsTreeChildNode> GetTreeChildren() => null;

	public record SettingsTreeChildNode( string Title, string Icon = "settings", Action<ProjectSettingsCategoryPanel> Configure = null );
}
