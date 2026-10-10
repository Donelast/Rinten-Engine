namespace Editor.FxEditor;

/// <summary>The .fx particle editor; edits save straight to disk, undo snapshots the whole definition</summary>
[EditorForAssetType( "fx" )]
[EditorApp( "FX Editor", "auto_awesome", "Effects: emitters, modules, shapes, anchors, events and the parameters they leave open" )]
public sealed partial class ParticleEditorWindow : DockWindow, IAssetEditor, IEffectEditor
{
	//Which saved dock layout this window's arrangement belongs to. Bumped whenever the default layout changes shape.
	internal const string LayoutCookie = "ParticleEditor.2";

	//Every one of these that is open, newest first.
	internal static readonly List<ParticleEditorWindow> Editors = new();

	Asset _asset;
	ParticleDefinition _definition;

	EffectTree _tree;
	Properties _properties;
	ParticlePreview _preview;
	ParameterList _parameters;

	object _selection;

	public bool CanOpenMultipleAssets => true;

	/// <summary>The effect open here, if there is one.</summary>
	public ParticleDefinition Definition => _definition;

	/// <summary>What is selected: the effect, an emitter, or one module.</summary>
	public object Selection => _selection;

	public ParticleEditorWindow()
	{
		DeleteOnClose = true;
		Size = new Vector2( 1500, 950 );

		SetWindowIcon( "auto_awesome" );

		BuildUI();
		UpdateTitle();

		//After the docks and not before: setting this is what restores the saved layout, and
		//the default it falls back to opens docks that have to have been registered already.
		StateCookie = LayoutCookie;

		Register();
	}

	public void AssetOpen( Asset asset )
	{
		if ( asset is null )
			return;

		//What is open goes to the disk before what is being opened takes its place. Nothing is asked, because nothing is at risk.
		Checkpoint();

		OpenDefinition( asset );
		Show();
	}

	//Open an effect here.
	internal void OpenDefinition( Asset asset )
	{
		_asset = asset;
		_definition = asset.TryLoadResource<ParticleDefinition>( out var definition ) ? definition : null;

		if ( _definition is not null && _definition.Emitters.Count == 0 )
			_definition.Emitters.Add( NewEmitter( "Emitter 1" ) );

		ClearUndo();

		_tree?.SetDefinition( _definition );
		_preview?.SetDefinition( _definition );
		_parameters?.SetDefinition( _definition );

		Select( _definition );

		Register();
		UpdateTitle();
	}

	public override void Show()
	{
		if ( !IsEmbedded )
			base.Show();

		StateCookie = LayoutCookie;

		//Newest first, for anything asking which effect is meant - see DefinitionOf.
		Register();

		ApplyLayout();
	}

	public void SelectMember( string memberName ) { }

	//Whether this editor is a tab in the main window rather than a window of its own - the workspace
	//takes the docks out of the frame and leaves the window itself an empty husk that never shows.
	internal bool IsEmbedded => DockManager.IsValid() && DockManager.Parent != this;

	//Show something in the properties dock, and mark it in the tree.
	internal void Select( object target )
	{
		_selection = target;

		_properties?.Show( target );
		_tree?.NoteSelection();
		_parameters?.NoteSelection();
	}

	//Whether this object is the one selected.
	internal bool IsSelected( object target ) => ReferenceEquals( _selection, target );

	void UpdateTitle()
	{
		var path = _asset?.Path ?? "untitled";

		Title = $"FX - {path}";
	}

	protected override bool OnClose()
	{
		//Written rather than asked about.
		Checkpoint();

		return true;
	}

	public override void OnDestroyed()
	{
		Editors.Remove( this );

		base.OnDestroyed();
	}

	void Register()
	{
		Editors.Remove( this );
		Editors.Insert( 0, this );
	}

	//Finds the effect owning a module, emitter or parameter by searching the open editors
	internal static ParticleDefinition DefinitionOf( object target )
	{
		if ( target is ParticleDefinition definition )
			return definition;

		foreach ( var window in Editors )
		{
			if ( window._definition is not { } candidate ) continue;

			if ( Contains( candidate, target ) )
				return candidate;
		}

		//Nothing owned it - the newest window's effect is the best guess left, and it is right whenever there is only one of them.
		return Editors.Count > 0 ? Editors[0]._definition : null;
	}

	static bool Contains( ParticleDefinition definition, object target )
	{
		foreach ( var shape in definition.Shapes )
		{
			if ( ReferenceEquals( shape, target ) )
				return true;
		}

		foreach ( var anchor in definition.Anchors )
		{
			if ( ReferenceEquals( anchor, target ) )
				return true;
		}

		foreach ( var emitter in definition.Emitters )
		{
			if ( ReferenceEquals( emitter, target ) )
				return true;

			foreach ( var module in emitter.AllModules() )
			{
				if ( ReferenceEquals( module, target ) )
					return true;
			}
		}

		return false;
	}

	void IEffectEditor.OnEdited( string name ) => OnEdited( name );
	void IEffectEditor.CommitUndo( string name ) => CommitUndo( name );
	void IEffectEditor.NoteAnchorRenamed( ParticleAnchor anchor, string was ) => NoteAnchorRenamed( anchor, was );
	void IEffectEditor.NoteShapeRenamed( ParticleShape shape, string was ) => NoteShapeRenamed( shape, was );
	void IEffectEditor.NoteParameterRenamed( ParticleDefinition.Parameter parameter, string was ) => NoteParameterRenamed( parameter, was );
}
