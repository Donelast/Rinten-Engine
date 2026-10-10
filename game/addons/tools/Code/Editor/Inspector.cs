namespace Editor;

/// <summary>Inspector dock: the only Widget in this panel. Everything inside is Panel + razor.</summary>
[Dock( "Editor", "Inspector", "manage_search", DockArea.Right )]
public class Inspector : PanelWidget
{
	InspectorShellPanel Shell;

	public bool IsLocked = false;

	private InspectorPanel _currentPanel;

	public Inspector( Widget parent ) : base( parent )
	{
		HorizontalSizeMode = SizeMode.Flexible;
		VerticalSizeMode = SizeMode.Flexible;

		Shell = new InspectorShellPanel { Host = this };
		SetPanel( Shell );

		EditorUtility.OnInspect += StartInspecting;

		var inspect = EditorUtility.InspectorObject;
		if ( inspect is not null )
			StartInspecting( inspect, false );
		else if ( SceneEditorSession.Active?.Selection.Count > 0 )
			StartInspecting( SceneEditorSession.Active.Selection.ToArray(), false );
	}

	private void StartInspecting( EditorUtility.OnInspectArgs args )
	{
		if ( !StartInspecting( args.Object, true ) )
			args.Cancel = true;
	}

	private bool StartInspecting( object obj, bool addToHistory = true )
	{
		if ( IsLocked ) return true;

		SerializedObject so = obj?.GetSerialized();

		if ( obj is Array array )
		{
			if ( array.Length == 0 ) return true;
			if ( array.Length == 1 )
				so = array.GetValue( 0 )?.GetSerialized();
			else
			{
				var mo = new MultiSerializedObject();
				for ( int i = 0; i < array.Length; i++ )
				{
					var val = array.GetValue( i );
					if ( val is not null ) mo.Add( val.GetSerialized() );
				}
				mo.Rebuild();
				so = mo;
			}
		}

		if ( !so.IsValid() )
		{
			_currentPanel = null;
			Shell.SetInspectorContent( null );
			return true;
		}

		if ( IsAlreadyInspectingObject( so ) ) return true;

		if ( _currentPanel is not null && !_currentPanel.CloseInspector( obj ) )
			return false;

		_currentPanel = InspectorPanel.Create( so ) ?? new Editor.Inspectors.GenericInspectorPanel( so );

		Shell.SetInspectorContent( _currentPanel );

		if ( addToHistory )
		{
			while ( ObjectHistory.Count > HistoryPlace + 1 )
				ObjectHistory.RemoveAt( ObjectHistory.Count - 1 );

			ObjectHistory.Add( new HistoryReference( obj ) );
			HistoryPlace = ObjectHistory.Count - 1;

			if ( ObjectHistory.Count > 100 )
			{
				ObjectHistory.RemoveAt( 0 );
				HistoryPlace--;
			}
		}

		UpdateBackForward();
		return true;
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();
		EditorUtility.OnInspect -= StartInspecting;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.Button == MouseButtons.Back && GoBack() ) { e.Accepted = true; return; }
		if ( e.Button == MouseButtons.Forward && GoForward() ) { e.Accepted = true; return; }
		base.OnMousePress( e );
	}

	/// <summary>The identifier of the back key, which the toolbar shows beside the arrow.</summary>
	public const string BackShortcut = "inspector.back";

	//Back through what was inspected, from anywhere in the window. Q is also the viewport's fly-down key, but that one only holds Q
	//while the right button is down and hands it on otherwise - see SceneViewportPanel.Shortcuts - so flying down never steps back.
	[Shortcut( BackShortcut, "Q", ShortcutType.Window )]
	void BackFromShortcut()
	{
		if ( !GoBack() ) EditorShortcuts.PassShortcut = true;
	}

	public bool GoBack()
	{
		HistoryPlace = HistoryPlace.Clamp( 0, ObjectHistory.Count );
		while ( HistoryPlace > 0 )
		{
			HistoryPlace--;
			if ( JumpToHistory() ) { UpdateBackForward(); return true; }
			ObjectHistory.RemoveAt( HistoryPlace );
		}
		UpdateBackForward();
		return false;
	}

	public bool GoForward()
	{
		HistoryPlace = HistoryPlace.Clamp( 0, ObjectHistory.Count );
		while ( HistoryPlace < ObjectHistory.Count - 1 )
		{
			HistoryPlace++;
			if ( JumpToHistory() ) { UpdateBackForward(); return true; }
			ObjectHistory.RemoveAt( HistoryPlace );
			HistoryPlace--;
		}
		UpdateBackForward();
		return false;
	}

	private bool JumpToHistory()
	{
		if ( !ObjectHistory[HistoryPlace].TryGetTarget( out var target ) ) return false;
		StartInspecting( target, false );
		SelectInScene( target );
		return true;
	}

	//When going back to scene objects, select them too; history stays intact so forward still works
	private void SelectInScene( object target )
	{
		if ( IsLocked ) return;

		var session = SceneEditorSession.Active;
		if ( session?.Scene is not { } scene || !scene.IsValid() ) return;

		var items = target is Array array ? array.Cast<object>().ToArray() : [target];
		if ( items.Length == 0 ) return;

		var objects = new List<GameObject>( items.Length );

		foreach ( var item in items )
		{
			if ( item is not GameObject go || go is Rinten.Scene || !go.IsValid() || go.Scene != scene ) return;

			objects.Add( go );
		}

		session.Selection.Clear();

		foreach ( var go in objects )
			session.Selection.Add( go );
	}

	public void ToggleLock()
	{
		IsLocked = !IsLocked;
		UpdateBackForward();
		if ( !IsLocked )
		{
			var selection = SceneEditorSession.Current?.Selection;
			if ( selection is not null && selection.Count > 0 )
				StartInspecting( EditorUtility.InspectorObject );
		}
	}

	void UpdateBackForward()
	{
		Shell?.UpdateNavigation(
			HistoryPlace > 0,
			HistoryPlace < ObjectHistory.Count - 1,
			IsLocked );
	}

	[EditorEvent.Frame]
	public void Frame()
	{
		if ( _currentPanel is not null && _currentPanel.IsValid() && !_currentPanel.SerializedObject.IsValid() )
		{
			_currentPanel.Delete();
			_currentPanel = null;
			Shell.SetInspectorContent( null );
		}
	}

	private bool IsAlreadyInspectingObject( SerializedObject newObj )
	{
		if ( _currentPanel?.SerializedObject is null ) return false;

		var oldTargets = _currentPanel.SerializedObject.Targets.ToHashSet();
		if ( oldTargets.Count != newObj.Targets.Count() ) return false;

		foreach ( var t in newObj.Targets )
			if ( !oldTargets.Contains( t ) ) return false;

		return true;
	}

	public int HistoryPlace = 0;
	public List<HistoryReference> ObjectHistory = new();

	public struct HistoryReference
	{
		List<WeakReference<object>> _objects = new();

		public HistoryReference( object obj )
		{
			if ( obj is Array array )
			{
				foreach ( var element in array )
					_objects.Add( new WeakReference<object>( element ) );
				return;
			}
			_objects.Add( new WeakReference<object>( obj ) );
		}

		public bool TryGetTarget( out object target )
		{
			var list = new List<object>();
			foreach ( var item in _objects )
			{
				if ( item.TryGetTarget( out var element ) )
				{
					if ( !((element as IValid)?.IsValid() ?? true) ) continue;
					list.Add( element );
				}
			}
			target = list.ToArray();
			return list.Count > 0;
		}
	}
}
