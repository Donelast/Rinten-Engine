using Editor.TreeBrowser;
using System;
using System.Collections.Generic;

namespace Editor.MdlEditor;

/// <summary>The .mdl model editor; its own workspace, kept on the same model as the graph via EditorLink</summary>
[EditorForAssetType( "mdl" )]
[EditorApp( "Model Editor", "view_in_ar", "Models: meshes, materials, bodygroups, bones, attachments and collision" )]
public sealed partial class ModelEditorWindow : DockWindow, IAssetEditor, IPreviewHost, IModelPageHost, IOpenModel, ILinkedEditor,
	AssetSystem.IEventListener
{
	//Which saved dock layout this window's arrangement belongs to. Bumped whenever the default layout changes shape.
	internal const string LayoutCookie = "ModelEditor.6";

	private Asset _asset;
	private ModelDefinition _definition;
	private Model _model;

	private ModelPage _page;
	private ModelOutliner _outliner;
	private ModelNodeInspector _inspector;
	private PanelDockHost _hierarchyDock;
	private PanelDockHost _timelineDock;
	private Preview _preview;

	public bool CanOpenMultipleAssets => true;

	/// <summary>The model open here, if there is one.</summary>
	public Asset ModelAsset => _asset;
	public ModelDefinition ModelDefinition => _definition;
	public Model OpenModel => _model;

	//Whether the definition has edits that aren't on disk.
	internal bool ModelDirty => _definition?.HasUnsavedChanges ?? false;

	public ModelEditorWindow()
	{
		DeleteOnClose = true;
		Size = new Vector2( 1400, 900 );

		SetWindowIcon( "view_in_ar" );

		BuildUI();
		UpdateTitle();

		//After the docks and not before: setting this is what restores the saved layout, and
		//the default it falls back to opens docks that have to have been registered already.
		StateCookie = LayoutCookie;

		OpenModels.Register( this );
		EditorLink.Register( this );
	}

	public void AssetOpen( Asset asset )
	{
		if ( asset is null || !asset.HasSourceFile )
			return;

		//What is open goes to the disk before what is being opened takes its place. Nothing is asked, because nothing is at risk.
		Checkpoint();

		OpenModelAsset( asset );
		Show();
	}

	//Open a model definition here. A .mdl is a model definition - the editable half of a model, which is what the parameters on the page are.
	internal void OpenModelAsset( Asset asset )
	{
		var previousPath = _asset?.Path;

		_asset = asset;
		_definition = asset.TryLoadResource<ModelDefinition>( out var definition ) ? definition : null;
		_model = Model.Load( asset.Path );
		_compiledOnScreen = CompiledWriteTime();

		_page?.SetModel( _asset, _definition, _model );

		if ( !ModelGraphResolver.SamePath( previousPath, asset?.Path ) )
			ClearModelUndo();

		_preview?.StopClip();
		_preview?.RebuildModel( force: true );

		//Newest first, for anything asking which model is meant.
		OpenModels.Register( this );

		UpdateTitle();
	}

	public override void Show()
	{
		if ( !IsEmbedded )
			base.Show();

		StateCookie = LayoutCookie;

		ApplyLayout();
	}

	public void SelectMember( string memberName ) { }

	//Whether this editor is a tab in the main window rather than a window of its own - the workspace
	//takes the docks out of the frame and leaves the window itself an empty husk that never shows.
	internal bool IsEmbedded => DockManager.IsValid() && DockManager.Parent != this;

	protected override bool OnClose()
	{
		//Written rather than asked about.
		Checkpoint();

		return true;
	}

	public override void OnDestroyed()
	{
		EditorLink.Unregister( this );
		OpenModels.Unregister( this );

		base.OnDestroyed();
	}

	//The preview's host - see IPreviewHost. There is no graph here, so everything about one is nothing.

	Data IPreviewHost.Graph => null;
	bool IPreviewHost.IsModelPreview => true;
	SceneModel IPreviewHost.PreviewModel => _preview?.ModelRenderer?.SceneModel;
	bool IPreviewHost.IsPreviewPaused => _preview?.IsPaused ?? false;
	void IPreviewHost.MarkDirty( bool updatePreview ) { }
	void IPreviewHost.CommitChange( string name ) { }
	void IPreviewHost.NotifyModelDocChanged() => _page?.NotifyDefinitionChanged();
	void IPreviewHost.ApplyPreviewParameters() { }
	void IPreviewHost.ConsumeAutoResetPreviews() { }
	void IPreviewHost.CollectActiveAnimations( SceneModel model, List<(string Name, float Cycle, float Duration)> animations ) { }
	void IPreviewHost.SetRecordingReviewMode( bool reviewing, bool paused ) => _preview?.SetRecordingReviewMode( reviewing, paused );
	bool IPreviewHost.ApplyRecordedUpdate( int updateIndex ) => _preview?.ApplyRecordedUpdate( updateIndex ) ?? false;
	void IPreviewHost.PickBone( string bone ) => _page?.PickBone( bone );

	//The link to the animation graph - see EditorLink.

	LinkedKind ILinkedEditor.Kind => LinkedKind.Model;
	Asset ILinkedEditor.CurrentAsset => _asset;

	//Nothing: a graph is not an asset of its own. The graph editor is handed this model instead, and opens the graph it carries - see AdoptCounterpart.
	Asset ILinkedEditor.CounterpartAsset => null;

	bool ILinkedEditor.IsOnScreen => IsEmbedded
		? EditorWorkspaces.IsCurrent( GetType() ) && ShownInTree( DockManager )
		: Visible;

	//A graph with no model on disk has nothing this editor can show for it.
	void ILinkedEditor.AdoptCounterpart( Asset counterpart ) { }

	//Whether a widget and everything above it is shown - a document tab that is not the current one is not.
	internal static bool ShownInTree( Widget widget )
	{
		for ( ; widget.IsValid(); widget = widget.Parent )
		{
			if ( !widget.Visible ) return false;
		}

		return true;
	}
}
