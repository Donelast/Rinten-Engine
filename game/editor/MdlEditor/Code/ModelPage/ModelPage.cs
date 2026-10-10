using Rinten.UI;
﻿using System;

namespace Editor.MdlEditor;

//The page itself is the one widget here, because the central slot of the dock manager is arranged as a widget - see PanelDockHost. Everything under it, the stage and the viewport it holds, is panels.
/// <summary>The model half's 3D stage. Tree and inspector live in docks around it.</summary>
public partial class ModelPage : PanelDockHost, IModelDocHost
{
	readonly IModelPageHost _host;
	readonly ModelStage _stage;

	ModelOutliner _outliner;
	ModelNodeInspector _inspector;
	readonly ModelHierarchy _hierarchy;

	Asset _asset;
	ModelDefinition _definition;
	Model _model;
	int _cleanHash;
	bool _placementUndoOpen;

	internal Asset Asset => _asset;
	internal ModelDefinition Definition => _definition;
	internal Model CompiledModel => _model;
	internal ModelOutliner Outliner => _outliner;
	internal ModelNodeInspector Inspector => _inspector;
	internal ModelHierarchy Hierarchy => _hierarchy;
	internal ModelStage Stage => _stage;

	//The bone picked in the hierarchy, for whatever is being added next.
	internal string PickedBone => _hierarchy.IsValid() ? _hierarchy.Selected?.Name : null;

	//The page as a ModelDoc host. Written out rather than made public, because the outliner it hands back is internal
	//and a public member cannot return one - and because the names the rest of the editor uses are these ones.
	Asset IModelDocHost.Asset => _asset;
	ModelDefinition IModelDocHost.Definition => _definition;
	Model IModelDocHost.CompiledModel => _model;
	ModelOutliner IModelDocHost.Outliner => _outliner;

	//The model editor writes the file on a button; the inspector does not.
	bool IModelDocHost.CanSave => true;

	void IModelDocHost.SaveModel() => SaveFromOutliner();

	void IModelDocHost.NotifyDefinitionChanged( bool rebuildInspector )
		=> NotifyDefinitionChanged( rebuildInspector );

	void IModelDocHost.BeginDefinitionEdit( string name ) => BeginDefinitionEdit( name );

	void IModelDocHost.EndDefinitionEdit( string name ) => EndDefinitionEdit( name );

	Transform? IModelDocHost.BoneNow( string bone ) => _stage?.BoneNow( bone );

	internal void BeginDefinitionEdit( string name )
	{
		FlushPlacementUndo();
		_host.PushModelUndo( name );
	}

	internal void EndDefinitionEdit( string name = null ) => _host.CommitModelUndo( name );

	public ModelPage( IModelPageHost host ) : base( null )
	{
		_host = host;

		_stage = new ModelStage();

		//The skeleton's list is the page's own; the window stands it in its "Hierarchy" dock, a tab beside the inspector - see ModelEditorWindow.
		_hierarchy = new ModelHierarchy();
		_hierarchy.ItemClicked = item => _stage.HighlightBone( item?.Name );

		SetContent( _stage );
	}

	internal void Bind( ModelOutliner outliner, ModelNodeInspector inspector )
	{
		_outliner = outliner;
		_inspector = inspector;
		_outliner.ItemSelected = OnTreeItemSelected;
	}

	internal void BindPreview( Preview preview ) => _stage.Attach( preview );

	internal void UnbindPreview() => _stage.Detach();

	//A bone clicked in the picture, or null for a click on none: picked in the hierarchy.
	internal void PickBone( string bone ) => _hierarchy.SelectBone( bone );

	internal void SaveFromOutliner() => _host.SaveModel();

	/// <summary>Shows the editable definition and compiled model.</summary>
	public void SetModel( Asset asset, ModelDefinition definition, Model model )
	{
		var sameDefinition = ReferenceEquals( _definition, definition );
		_asset = asset;
		_definition = definition;
		_model = model;
		_model?.ReloadAttachments();
		_definition?.EnsureEditorDefaults();
		_cleanHash = HashDefinition();
		_placementUndoOpen = false;

		_outliner?.SetModel( asset, definition );
		var selected = _outliner?.Selected;
		if ( sameDefinition )
			BindPlacement( selected );
		else
			_stage.SetPlacement( null );
		_stage.SetSelection( selected );
		_outliner?.UpdateSaveState( definition?.HasUnsavedChanges ?? false );
		_hierarchy?.Rebuild( asset, model );
		_inspector?.Show( definition, selected );
	}

	internal void SetCompiledModel( Model model )
	{
		_model = model;
		_model?.ReloadAttachments();
		_hierarchy?.Rebuild( _asset, model );
	}

	internal ModelDocContext DocContext() => new() { Host = this, IsCollapsed = kind => _outliner?.IsCollapsed( kind ) ?? false };

	internal void OnModelUndoRestored()
	{
		NotifyDefinitionChanged( rebuildInspector: true );
		BindPlacement( _outliner?.Selected );
	}

	internal void NotifyDefinitionChanged( bool rebuildInspector = false )
	{
		if ( _definition is null )
			return;

		//The model reads its attachments off the definition once and keeps them. Dropped here so an attachment somebody just added or dragged
		//exists as far as the preview - and anything asking the model for one - is concerned, rather than only after the editor is opened again.
		_model?.ReloadAttachments();

		if ( PlacementPressed() && !_placementUndoOpen )
		{
			_host.PushModelUndo( "Place" );
			_placementUndoOpen = true;
		}

		_outliner?.RefreshTree();
		if ( rebuildInspector )
			_inspector?.Refresh();

		if ( !_definition.HasUnsavedChanges )
		{
			var hash = HashDefinition();
			if ( hash == _cleanHash )
				return;

			_cleanHash = hash;
			_definition.StateHasChanged();
		}

		_outliner?.UpdateSaveState( true );
		_host.OnModelEdited();
	}

	void OnTreeItemSelected( ModelDocItem item )
	{
		_inspector?.Show( _definition, item );

		if ( item?.Kind == ModelDocKind.Animation )
			_host.PlayModelClip( ModelDoc.Of( item.Kind )?.Clip( DocContext(), item ) );

		BindPlacement( item );
		_stage.SetSelection( item );
	}

	void BindPlacement( ModelDocItem item )
	{
		if ( _outliner?.SelectedItems is { Count: > 1 } picked
			&& ModelGroupPlacement.For( DocContext(), picked, item ) is { } group )
		{
			_stage.SetPlacement( group );
			return;
		}

		_stage.SetPlacement( item is null ? null : ModelDoc.Of( item.Kind )?.Placement( DocContext(), item ) );
	}

	void FlushPlacementUndo()
	{
		if ( !_placementUndoOpen )
			return;

		_placementUndoOpen = false;
		_host.CommitModelUndo( "Place" );
	}

	[EditorEvent.Frame]
	void TickPlacementUndo()
	{
		if ( !IsValid || !_placementUndoOpen || PlacementPressed() )
			return;

		FlushPlacementUndo();
	}

	bool PlacementPressed() => _stage.PlacementDragging;

	int HashDefinition()
	{
		if ( _definition is null )
			return 0;

		try
		{
			return _definition.Serialize().ToJsonString().FastHash();
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Model: could not read the definition back to compare it" );
			return 0;
		}
	}
}

//Scene view filling the remaining width, with the placement tools over it
[StyleSheet( "ModelPage.cs.scss" )]
sealed class ModelStage : EditorPanel
{
	Preview _preview;

	//The layer the overlays hang on. It goes straight into the viewport's own frame while there is a preview here - the preview is a panel in
	//this tree now, so the layer is a box over the picture rather than a widget the editor's geometry had to keep in step with it every frame.
	readonly Panel _overlays;

	ModelDocItem _selection;

	public ModelStage()
	{
		AddClass( "model-stage" );

		_overlays = new ViewportOverlays();
	}

	internal bool PlacementDragging => _preview?.PlacementDragging == true;

	//Where a bone is in the pose the viewport is playing.
	internal Transform? BoneNow( string bone ) => _preview?.BoneNow( bone );

	//Lights one bone in the picture - the one picked in the hierarchy.
	internal void HighlightBone( string bone ) => _preview?.SetHighlightedBone( bone );

	public void SetPlacement( ScenePlacement placement )
	{
		_placement = placement;
		_preview?.SetPlacement( placement );
	}

	public void SetSelection( ModelDocItem item )
	{
		_selection = item;
		_preview?.SetModelSelection( item );
	}

	private ScenePlacement _placement;
	private PlacementToolBar _tools;

	/// <summary>Take the preview in as this stage's own content.</summary>
	public void Attach( Preview preview )
	{
		_preview = preview;

		_tools?.Delete( true );
		_tools = null;

		if ( !preview.IsValid() )
		{
			_overlays.Parent = null;
			return;
		}

		AddChild( preview );

		_overlays.Parent = preview.ViewportOverlayParent;
		_tools = _overlays.AddChild( new PlacementToolBar( preview ) );

		preview.SetPlacement( _placement );
		preview.SetModelSelection( _selection );
		preview.SetModelMode( true );
	}

	/// <summary>Give the preview back, alive, for the animation half to re-home.</summary>
	public void Detach()
	{
		_preview?.SetPlacement( null );
		_preview?.SetModelSelection( null );
		_preview?.SetModelMode( false );

		_tools?.Delete( true );
		_tools = null;

		//The layer goes with the preview it was over, and comes back with it.
		_overlays.Parent = null;

		//Unparented, not destroyed - the window owns the preview and hands it between the two halves for the life of the editor.
		if ( _preview.IsValid() )
			_preview.Parent = null;

		_preview = null;
	}

	public override void Tick()
	{
		base.Tick();

		//The overlays hold themselves against the viewport's corners, so the only one with anything left
		//to be told is the tool bar - how far up from the bottom the viewport's own furniture reaches.
		_tools?.PlaceIn();
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		//It is over the preview rather than in this panel, and the preview outlives the stage.
		_overlays?.Delete( true );
	}
}

//The layer over the model viewport, where the hierarchy and the placement tools hang. Deaf to the pointer itself,
//so that what is under it - the 3D view - still hears everything that does not land on one of the overlays.
[StyleSheet( "ModelPage.cs.scss" )]
sealed class ViewportOverlays : Panel
{
	public ViewportOverlays() => AddClass( "viewport-overlays" );
}
