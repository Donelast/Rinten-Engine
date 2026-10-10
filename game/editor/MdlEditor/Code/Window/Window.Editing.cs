using Rinten.Helpers;

namespace Editor.MdlEditor;

internal enum GraphChangeScope
{
	Runtime,
	EditorOnly
}

public partial class Window
{
	private bool _dirty;
	private string _selectedParameterName;
	private UndoSystem _undoSystem;
	private string _pendingUndoName;
	private GraphChangeScope _pendingChangeScope = GraphChangeScope.Runtime;
	private string _committedState;
	private StateMachineNode _selectedStateMachine;
	private StateMachineNode.State _selectedState;
	private string _selectedStateName;
	private object _primarySelectionContext;
	private object _primarySelectionItem;

	internal void PushGraphUndo( string name, GraphChangeScope changeScope = GraphChangeScope.Runtime )
	{
		_pendingUndoName = name;
		_pendingChangeScope = changeScope;
	}

	internal void PushGraphRedo() => CommitChange( _pendingUndoName, _pendingChangeScope );

	internal void CommitChange( string name, GraphChangeScope changeScope = GraphChangeScope.Runtime )
	{
		_pendingUndoName = null;
		_pendingChangeScope = GraphChangeScope.Runtime;

		var pre = _committedState;
		var post = _graph.Serialize();

		if ( pre is null || pre == post )
		{
			_committedState = post;
			return;
		}

		_committedState = post;
		_undoSystem.Insert( name ?? "Edit Graph", () => RestoreGraphState( pre, changeScope ), () => RestoreGraphState( post, changeScope ) );
		MarkDirty( changeScope == GraphChangeScope.Runtime );
		NotifyGraphEdited();
	}

	//Tell docks the graph changed so they can refresh, instead of them polling every frame.
	private void NotifyGraphEdited()
	{
		_parameterList?.OnGraphEdited();
		_clipList?.OnGraphEdited();
		_variationEditor?.OnGraphEdited();
		(_pageCanvas as INodePage)?.OnGraphEdited();
	}

	private void RestoreGraphState( string state, GraphChangeScope changeScope )
	{
		var graph = new Data();
		graph.Deserialize( state );

		//The variation being edited isn't part of the serialized state, so it'd reset to
		//the default one on every undo - it falls back to default if the variation is gone
		graph.SelectedVariationID = _graph?.SelectedVariationID;

		var previous = (_graph?.Parameters ?? []).DistinctBy( p => p.Name ).ToDictionary( p => p.Name );
		for ( var i = 0; i < graph.Parameters.Count; i++ )
		{
			if ( previous.TryGetValue( graph.Parameters[i].Name, out var existing ) )
				graph.Parameters[i] = existing.CopyFrom( graph.Parameters[i] );
		}

		_session.ReplaceGraph( graph );
		_committedState = state;
		_clipInfo = null;
		NotifyGraphEdited();

		if ( changeScope == GraphChangeScope.Runtime )
			_preview?.RebuildModel();

		var stale = _nav.FindIndex( page => ResolvePage( page ) is null );
		if ( stale > 0 )
			_nav.RemoveRange( stale, _nav.Count - stale );

		ShowPage();
		MarkDirty( changeScope == GraphChangeScope.Runtime );
	}

	[Shortcut( "editor.undo", "CTRL+Z" )]
	internal bool Undo() => _undoSystem.Undo();

	[Shortcut( "editor.redo", "CTRL+Y" )]
	internal bool Redo() => _undoSystem.Redo();

	[Shortcut( "editor.cut", "CTRL+X" )]
	private void CutSelection()
	{
		if ( FocusedNodePage?.CutSelection() == true )
			return;

		FocusedGraphView?.CutSelection();
	}

	[Shortcut( "editor.copy", "CTRL+C" )]
	private void CopySelection()
	{
		if ( FocusedNodePage?.CopySelection() == true )
			return;

		FocusedGraphView?.CopySelection();
	}

	[Shortcut( "editor.paste", "CTRL+V" )]
	private void PasteSelection()
	{
		if ( FocusedNodePage?.PasteSelection() == true )
			return;

		FocusedGraphView?.PasteSelection();
	}

	[Shortcut( "editor.duplicate", "CTRL+D" )]
	private void DuplicateSelection() => FocusedGraphView?.DuplicateSelection();

	[Shortcut( "editor.select-all", "CTRL+A" )]
	private void SelectAll() => FocusedGraphView?.SelectAll();

	[Shortcut( "editor.clear-selection", "ESC" )]
	private void ClearSelection() => FocusedGraphView?.ClearSelection();

	[Shortcut( "gameObject.frame", "F" )]
	private void CenterOnSelection() => FocusedGraphView?.CenterOnSelection();

	private INodePage FocusedNodePage => (_workspace?.SecondaryView.HasFocus ?? false) ? null : _pageCanvas as INodePage;

	//Null while the model half is up: the graph canvas is destroyed with it, so there is no view to have focus.
	private View FocusedGraphView => (_workspace?.SecondaryView.HasFocus ?? false) ? _workspace.SecondaryView
		: (_pageCanvas as INodePage)?.FocusedGraphView
		?? (_graphView.IsValid() && (_graphView.HasFocus || IsGraphShown) ? _graphView : null);

	internal void MarkDirty( bool updatePreview = true )
	{
		_dirty = true;
		Edited();
		UpdateTitle();

		if ( !updatePreview )
			return;

		_previewCompileStale = true;

		if ( _autoCompilePreviewEnabled )
			CompilePreview();
	}

	private void UpdateTitle()
	{
		var star = _dirty ? "*" : "";

		var name = _model?.Path ?? "no model";
		Title = $"Animgoo - {name}{star}";
	}

	public void OnNodeSelected( View source, Node node )
	{
		if ( node is null && _clipInfo is not null )
			return;

		_clipInfo = null;

		if ( source != _workspace?.SecondaryView )
		{
			_primarySelectionContext = node;
			_primarySelectionItem = node;
			_selectedState = null;
			_selectedStateMachine = null;
			_workspace?.Show( node, node );
		}

		ApplyNodeSelection( node );
	}

	internal void OnCanvasSelection( object context, object item )
	{
		_clipInfo = null;
		_primarySelectionContext = context;
		_primarySelectionItem = item;
		_selectedStateMachine = context as StateMachineNode;
		_selectedState = item as StateMachineNode.State;
		_selectedStateName = _selectedState?.Name;
		_workspace?.Show( context, item );

		SetSelectedTarget( item ?? context ?? (object)_graph );
	}

	internal SecondaryGraphInfo? ResolveSecondaryGraph( object context, object selected )
	{
		if ( selected is ParameterNode parameter && FindVirtualParameter( parameter ) is { } virtualParameter )
			return new( virtualParameter.Graph, virtualParameter.Name, "Virtual Parameter", "vibration", Theme.Yellow, $"parameter:{virtualParameter.Identifier}" );

		if ( selected is Node node && node.SecondaryGraph is { } graph )
		{
			var (title, icon) = SubGraphInfo( node, graph );
			return new( graph, node.DisplayInfo.Name, title, icon, node.PrimaryColor, $"node:{node.Identifier}" );
		}

		if ( context is Nodes.Blend2DNode blend && selected is Nodes.Blend2DNode.BlendPoint point )
		{
			var index = blend.Points.IndexOf( point );
			return new( point.Graph, blend.PointTitle( index ), "Blend Point", "grain", blend.PrimaryColor, $"{blend.Identifier}:point:{index}" );
		}

		if ( context is Nodes.Blend1DNode blend1D && selected is Nodes.Blend1DNode.BlendPoint point1D )
		{
			var index = blend1D.Points.IndexOf( point1D );
			return new( point1D.Graph, blend1D.PointTitle( index ), "Blend Point", "linear_scale", blend1D.PrimaryColor, $"{blend1D.Identifier}:point:{index}" );
		}

		if ( context is not StateMachineNode machine )
			return null;

		return selected switch
		{
			StateMachineNode.State state => new( state.LayerGraph, state.Name, "State Layer Data", "layers", Theme.Red, $"{machine.Identifier}:state:{state.Name}" ),
			StateMachineNode.Conduit conduit => new( conduit.Graph, $"{conduit.From} → {conduit.To}", "Transition", "route", Theme.Pink, $"{machine.Identifier}:transition:{conduit.From}:{conduit.To}" ),
			StateMachineCanvas.SharedSelection shared => new( shared.GetGraph( machine ), shared.Title, "State Machine", shared.Icon, Theme.Pink, $"{machine.Identifier}:{shared.Key}" ),
			_ => null
		};
	}

	internal void RestorePrimarySelection()
	{
		if ( ReferenceEquals( _primarySelectionContext, _primarySelectionItem )
			&& _primarySelectionItem is Node node )
		{
			ApplyNodeSelection( node );
			return;
		}

		_selectedStateMachine = _primarySelectionContext as StateMachineNode;
		_selectedState = _primarySelectionItem as StateMachineNode.State;
		_selectedStateName = _selectedState?.Name;
		SetSelectedTarget( _primarySelectionItem ?? _primarySelectionContext ?? (object)_graph );
	}

	private void ApplyNodeSelection( Node node )
	{
		_selectedState = null;
		_selectedStateMachine = null;

		if ( node is AnimationClipNode clipNode && !string.IsNullOrWhiteSpace( clipNode.AnimationName ) )
			_properties?.PlayAnimation( clipNode.AnimationName );
		else if ( node is AnimationPoseNode poseNode && !string.IsNullOrWhiteSpace( poseNode.AnimationName ) )
			_properties?.ShowPose( poseNode );

		SetSelectedTarget( node ?? (object)_graph );
	}

	public void OnClipSelected( string animName )
	{
		_properties?.PlayAnimation( animName );

		if ( _clipInfo is null || _clipInfo.AnimationName != animName )
		{
			//The info reads from the clip resource, so a different clip needs a new one
			_clipInfo = new AnimationClipInfo( animName );
		}
		else
		{
			//Same clip, same object - the target setter would see no change, so the panel is told to read it again itself.
			_properties?.UpdateClipInfo();
		}

		SetSelectedTarget( _clipInfo );
	}

	public void OnParameterSelected( IParameter parameter )
	{
		_clipInfo = null;
		_selectedParameterName = parameter.Name;
		SetSelectedTarget( parameter );
	}

	internal bool IsSelected( object target ) => _selectedTarget == target;

	void SetSelectedTarget( object target )
	{
		if ( target == _selectedTarget )
			return;

		_selectedTarget = target;

		if ( _properties.IsValid() )
			_properties.Target = target;

		_parameterList?.UpdateSelection();
		_variationEditor?.UpdateSelection();
	}

	internal void ApplyPreviewParameters()
	{
		foreach ( var parameter in _graph?.Parameters ?? [] )
		{
			if ( parameter.AutoReset && parameter.PreviewIsStart )
				continue;

			SetPreviewParameter( parameter );
		}
	}

	internal void ConsumeAutoResetPreviews()
	{
		var changed = false;

		foreach ( var parameter in _graph?.Parameters ?? [] )
		{
			if ( !parameter.ConsumeAutoResetPreview() )
				continue;

			SetPreviewParameter( parameter );
			changed = true;
		}

		if ( changed )
			_parameterList?.RefreshPreviewValues();
	}

	internal void SetPreviewParameter( Parameter parameter )
	{
		var renderer = _preview?.ModelRenderer;
		if ( renderer is null || string.IsNullOrWhiteSpace( parameter.Name ) )
			return;

		switch ( parameter.Type )
		{
			case AnimParameterType.Float: renderer.Set( parameter.Name, parameter.PreviewFloat ); break;
			case AnimParameterType.Bool: renderer.Set( parameter.Name, parameter.PreviewBool ); break;
			case AnimParameterType.ID: renderer.Set( parameter.Name, parameter.PreviewID ?? "" ); break;
			case AnimParameterType.Vector: renderer.Set( parameter.Name, parameter.PreviewVector ); break;
			case AnimParameterType.Target:
				if ( !parameter.PreviewTargetIsSet )
					renderer.UnsetTarget( parameter.Name );
				else if ( parameter.PreviewTargetIsBone )
					renderer.SetTargetBone( parameter.Name, parameter.PreviewBone );
				else
					renderer.Set( parameter.Name, new Transform( parameter.PreviewPosition, parameter.PreviewRotation ) );
				break;
		}
	}

	private void OnPropertyUpdated( SerializedProperty property )
	{
		if ( _selectedTarget is IParameter parameter )
		{
			if ( _selectedParameterName is not null && _selectedParameterName != parameter.Name )
			{
				var oldName = _selectedParameterName;
				foreach ( var node in _graph.AllNodes.OfType<ParameterNode>()
					.Where( node => node.ParameterIdentifier == parameter.Identifier
						|| string.Equals( node.ParameterName, oldName, StringComparison.OrdinalIgnoreCase ) ) )
				{
					node.ParameterIdentifier = parameter.Identifier;
					node.ParameterName = parameter.Name;
					node.Update();
				}

				if ( parameter is Parameter { Type: AnimParameterType.ID } renamedControl )
				{
					foreach ( var selector in _graph.AllNodes.OfType<IDBasedSelectorNode>()
						.Where( node => node.ParameterIdentifier == renamedControl.Identifier
							|| string.Equals( node.ParameterName, oldName, StringComparison.OrdinalIgnoreCase ) ) )
					{
						selector.ParameterIdentifier = renamedControl.Identifier;
						selector.ParameterName = renamedControl.Name;
						selector.Update();
					}
				}

				_selectedParameterName = parameter.Name;
				_parameterList?.Rebuild();

				if ( parameter is VirtualParameter virtualParameter )
					OnVirtualParameterRenamed( virtualParameter );
			}

			if ( parameter is Parameter control )
			{
				if ( ParameterList.AffectsIDSelectors( property ) )
				{
					_parameterList?.SynchronizeIDSelectors( control,
						ParameterList.ResetsIDSelectorOptions( property ) );
				}

				SetPreviewParameter( control );
			}
		}

		if ( _selectedTarget is StateMachineNode.State state && state == _selectedState
			&& _selectedStateMachine is { } machine && _selectedStateName != state.Name )
		{
			machine.RenameState( _selectedStateName, state.Name );
			_selectedStateName = state.Name;
		}

		(_pageCanvas as INodePage)?.ViewCanvas?.Repaint();
		_graphView?.Graph?.Nodes?.OfType<Node>().ToList().ForEach( n => n.Update() );

		//Dragging a value fires this every frame - the preview compiles once the edit finishes, on the same signal that pushes the undo entry
		MarkDirty( updatePreview: false );
		_previewCompileStale = true;

		if ( property.Name is "ClipSource" or "TargetModel" )
			_clipList?.Rebuild();

		NotifyGraphEdited();
	}
}
