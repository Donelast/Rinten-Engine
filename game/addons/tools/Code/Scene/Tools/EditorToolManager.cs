namespace Editor;

public class EditorToolManager
{
	/// <summary>Name of the current tool, applied on the next frame; defaults to the mesh tool</summary>
	public static string CurrentModeName { get; set; } = "MeshTool";
	public static string LastModeName { get; set; } = CurrentModeName;
	private static string CurrentSubModeName { get; set; }

	public EditorTool CurrentTool { get; private set; }

	public bool IsCurrentViewFocused { get; private set; }

	/// <summary>Whether this viewport receives input: where a held press began, else under the pointer</summary>
	public bool IsInputView { get; private set; } = true;

	//The camera of the viewport a held button was pressed in.
	private CameraComponent pressView;

	public EditorTool CurrentSubTool => CurrentTool?.CurrentTool;
	public List<EditorTool> ComponentTools { get; private set; } = new List<EditorTool>();

	private SceneEditorSession _session;
	public SceneEditorSession CurrentSession
	{
		get => _session ?? SceneEditorSession.Active;
		set => _session = value;
	}

	public EditorToolManager()
	{

	}

	internal void Frame( CameraComponent camera, SceneEditorSession session, bool isFocused )
	{
		CurrentSession = session;
		UpdateTool( CurrentModeName );
		UpdateSubTool( CurrentSubModeName );
		UpdateSelection();

		IsCurrentViewFocused = isFocused;

		UpdateInputView( camera, isFocused );

		if ( CurrentTool is not null )
		{
			CurrentTool.Frame( camera );
		}

		foreach ( var a in ComponentTools )
		{
			a.Frame( camera );
		}

		if ( CurrentTool is not null )
		{
			Gizmo.Settings.Selection = CurrentTool.AllowGameObjectSelection;
		}
		else if ( ComponentTools.Any() )
		{
			Gizmo.Settings.Selection = ComponentTools.All( x => x.AllowGameObjectSelection );
		}
		else
		{
			Gizmo.Settings.Selection = true;
		}

	}

	//Works out IsInputView for the viewport framing now.
	private void UpdateInputView( CameraComponent camera, bool isFocused )
	{
		var held = Application.MouseButtons.HasFlag( MouseButtons.Left );

		//A view that went while its press was held can never see the release.
		if ( pressView is not null && !pressView.IsValid() ) pressView = null;

		if ( held && pressView is null && isFocused ) pressView = camera;

		if ( pressView is null )
		{
			IsInputView = isFocused;
			return;
		}

		IsInputView = pressView == camera;

		//The release, seen by the view the press began in - which is still where input goes this frame, so its tool finishes what it started.
		if ( !held && IsInputView ) pressView = null;
	}

	private string currentMode;
	private string currentSubMode;

	internal void UpdateTool( string editMode )
	{
		if ( currentMode == editMode )
			return;

		currentMode = editMode;

		//So that we update the sub tool immediately after updating the parent tool
		currentSubMode = null;

		var pendingSubTool = CurrentSubModeName;

		CurrentTool?.Dispose();
		CurrentTool = null;

		var bestType = EditorTypeLibrary.GetTypesWithAttribute<EditorToolAttribute>()
			.Where( x => x.Type.IsNamed( currentMode ) )
			.FirstOrDefault();

		if ( bestType.Type is null )
		{
			return;
		}

		CurrentTool = bestType.Type.Create<EditorTool>();
		CurrentTool.InitializeInternal( this );

		if ( pendingSubTool is not null )
			CurrentSubModeName = pendingSubTool;
	}

	private void UpdateSubTool( string editMode )
	{
		if ( currentSubMode == editMode ) return;
		if ( CurrentTool is not { } parentTool ) return;

		currentSubMode = editMode;

		parentTool.CurrentTool = ToolNamed( parentTool, editMode )
			?? parentTool.CurrentTool
			?? parentTool.Tools.FirstOrDefault();
	}

	//The sub-tool with this type name, or null. Apart from UpdateSubTool because a lambda capturing the name
	//makes that method build its closure on entry - and it is asked every frame and nearly always returns at once.
	static EditorTool ToolNamed( EditorTool parent, string name )
	{
		foreach ( var tool in parent.Tools )
		{
			if ( name == tool.GetType().Name ) return tool;
		}

		return null;
	}

	int previousHash;
	int SelectionHash => CurrentSession?.Selection?.GetHashCode() ?? 0;
	List<EditorTool> deleteList = new();

	//Check if the selection has changed. If it has, recreate component tools.
	private void UpdateSelection()
	{
		//The one selected object, when exactly one is - counted by a walk rather than
		//an OfType and a ToArray every frame, only to be asked how many there were.
		GameObject single = null;
		var selectedObjects = 0;

		foreach ( var selected in CurrentSession.Selection )
		{
			if ( selected is not GameObject go ) continue;

			selectedObjects++;
			single = go;
		}

		//Add component count to hash if we have single object selected, to update when components are added/removed
		var selectionHash = SelectionHash;
		if ( selectedObjects == 1 )
		{
			selectionHash = 0;
			var components = single.Components.GetAll();

			foreach ( var component in components )
			{
				if ( !component.IsValid() )
					continue;

				selectionHash = HashCode.Combine( selectionHash, component.GetHashCode() );
			}
		}

		if ( previousHash == selectionHash )
			return;

		previousHash = selectionHash;

		CurrentTool?.OnSelectionChanged();

		deleteList.Clear();
		deleteList.AddRange( ComponentTools.Where( t => !t.ShouldKeepActive() ) );

		if ( selectedObjects == 1 )
		{
			var allComponents = single.Components.GetAll().DistinctBy( x => x.GetType() ).ToArray();

			foreach ( var a in allComponents )
			{
				CreateToolFor( a.GetType() );
			}
		}

		foreach ( var deleteMe in deleteList )
		{
			ComponentTools.Remove( deleteMe );
			deleteMe?.Dispose();
		}

		foreach ( var a in ComponentTools )
		{
			a.OnSelectionChanged();
		}
	}

	private void CreateToolFor( Type t )
	{
		var targetType = typeof( EditorTool<> ).MakeGenericType( t );

		var editors = EditorTypeLibrary.GetTypes( targetType ).ToArray();
		foreach ( var editor in editors )
		{
			//Already exists
			if ( ComponentTools.Any( x => x.GetType() == editor.TargetType ) )
			{
				deleteList.RemoveAll( x => x.GetType() == editor.TargetType ); //don't delete these
				return;
			}

			var et = editor.Create<EditorTool>();
			ComponentTools.Add( et );
			et.InitializeInternal( this );
			return;
		}


		if ( t.BaseType != typeof( Component ) )
		{
			CreateToolFor( t.BaseType );
		}
	}

	public void DisposeAll()
	{
		previousHash = -1;
		currentMode = null;
		currentSubMode = null;
		CurrentTool?.Dispose();
		CurrentTool = null;
		foreach ( var tool in ComponentTools )
			tool?.Dispose();
		ComponentTools.Clear();
	}

	/// <summary>Switches to the named tool type next editor frame.</summary>
	public static void SetTool( string name )
	{
		LastModeName = CurrentModeName;
		CurrentModeName = name;
	}

	/// <summary>Switches to the named sub-tool type next editor frame.</summary>
	public static void SetSubTool( string name )
	{
		CurrentSubModeName = name;
	}
}

public class EditorToolAttribute : System.Attribute
{
	internal string Shortcut { get; }
	public bool Hidden { get; set; }
	public EditorToolAttribute( string shortcut = "" )
	{
		Shortcut = shortcut;
	}
}
