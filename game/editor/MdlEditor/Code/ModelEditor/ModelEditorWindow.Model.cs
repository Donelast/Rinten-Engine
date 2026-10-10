using Editor.TreeBrowser;
using Rinten.Helpers;
using System;

namespace Editor.MdlEditor;

/// <summary>The definition's undo stack, its file, and the live compile beside it.</summary>
public sealed partial class ModelEditorWindow
{
	private readonly UndoSystem _undoSystem = new();
	private string _committedState;
	private string _pendingUndoName;
	private bool _restoring;
	private bool _saving;

	//Undo

	internal void ClearModelUndo()
	{
		_undoSystem.Initialize();
		_pendingUndoName = null;
		_committedState = SnapshotDefinition();
	}

	public void PushModelUndo( string name )
	{
		if ( _pendingUndoName is not null && _pendingUndoName != name )
			CommitModelUndo();

		_pendingUndoName = name;
	}

	public void CommitModelUndo( string name = null )
	{
		if ( _restoring )
			return;

		var title = name ?? _pendingUndoName ?? "Edit Model";
		_pendingUndoName = null;

		var pre = _committedState;
		var post = SnapshotDefinition();
		if ( pre is null || post is null || pre == post )
		{
			_committedState = post ?? pre;
			return;
		}

		_committedState = post;
		_undoSystem.Insert( title, () => RestoreDefinitionState( pre ), () => RestoreDefinitionState( post ) );
	}

	[Shortcut( "editor.undo", "CTRL+Z" )]
	internal bool Undo() => _undoSystem.Undo();

	[Shortcut( "editor.redo", "CTRL+Y" )]
	internal bool Redo() => _undoSystem.Redo();

	string SnapshotDefinition()
	{
		if ( _definition is null )
			return null;

		try
		{
			//Without the graph the model carries. That has an editor and an undo of its own, and a model undo
			//putting back the graph as it was would throw away whatever was done in the graph editor since.
			var json = _definition.Serialize();
			json.Remove( nameof( ModelDefinition.Graph ) );

			return json.ToJsonString();
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Model: could not snapshot the definition" );
			return null;
		}
	}

	void RestoreDefinitionState( string state )
	{
		if ( _definition is null || string.IsNullOrEmpty( state ) )
			return;

		var jso = Json.ParseToJsonObject( state );
		if ( jso is null )
			return;

		_restoring = true;
		try
		{
			jso.Remove( "__version" );
			_definition.Deserialize( jso );
			_committedState = state;
			_page?.OnModelUndoRestored();
		}
		finally
		{
			_restoring = false;
		}
	}

	//The file

	/// <summary>Write the model definition back to its file. The asset system compiles it on the way in, so what comes back is the model the engine will load.</summary>
	public bool SaveModel()
	{
		if ( !WriteModel() )
			return false;

		//Asked for rather than scheduled, so the compiled model beside the definition is current by the time this returns.
		CompileLiveModel();
		return true;
	}

	//Writes the definition to disk without compiling; the live compile follows shortly after
	internal bool WriteModel()
	{
		if ( _asset is null )
			return false;

		if ( _definition is null )
		{
			Log.Warning( $"Model: {_asset.Path} has no definition to save - it is a compiled model, not one of ours" );
			return false;
		}

		if ( !_asset.SaveToDisk( _definition, compile: false ) )
		{
			Log.Warning( $"Model: could not write {_asset.Path}" );
			return false;
		}

		TreeAssetBrowser.RefreshOpen();
		UpdateTitle();
		return true;
	}

	//Writes what was edited, now, where there is anything to write. There is no unsaved document at any moment - see the graph editor's Checkpoint for why.
	internal void Checkpoint()
	{
		//Mid-write already, or mid-drag on the page: a write per frame of a drag is the one case where the cost adds up.
		if ( _saving ) return;
		if ( _page?.Stage.PlacementDragging == true ) return;

		if ( !ModelDirty ) return;

		_saving = true;

		try
		{
			WriteModel();
		}
		catch ( Exception e )
		{
			Log.Warning( e, "Model editor: could not write what is open" );
		}
		finally
		{
			_saving = false;
		}
	}

	//The last thing a drag was holding back.
	[EditorEvent.Frame]
	private void WriteAfterDrag()
	{
		if ( !ModelDirty ) return;
		if ( _page?.Stage.PlacementDragging == true ) return;

		Checkpoint();
	}

	[Shortcut( "editor.save", "CTRL+S" )]
	private void Save() => SaveModel();

	//The live compile

	RealTimeUntil _liveCompileAt;
	bool _liveCompilePending;

	/// <summary>Something on the model page was edited.</summary>
	public void OnModelEdited()
	{
		Checkpoint();
		UpdateTitle();
		_preview?.FillClipList( _definition );
		ScheduleLiveCompile();
	}

	void ScheduleLiveCompile()
	{
		_liveCompilePending = true;
		_liveCompileAt = 0.15f;
	}

	[EditorEvent.Frame]
	void TickLiveCompile()
	{
		if ( !_liveCompilePending || _liveCompileAt > 0 )
			return;

		if ( _page?.Stage.PlacementDragging == true )
			return;

		_liveCompilePending = false;
		CompileLiveModel();
	}

	void CompileLiveModel()
	{
		if ( _asset is null || _definition is null )
			return;

		if ( !_asset.CompileModel( _definition ) )
			return;

		ReloadCompiledModel();
	}

	void ReloadCompiledModel()
	{
		var preview = _preview.IsValid() ? _preview.CaptureForReload() : default;
		var path = _asset?.Path ?? _asset?.AbsolutePath;

		_model = Model.ReloadFromDisk( path ) ?? Model.Load( path );
		_compiledOnScreen = CompiledWriteTime();

		_page?.SetCompiledModel( _model );

		_preview?.RebuildModel( force: true );

		if ( _preview.IsValid() )
			_preview.RestoreAfterReload( preview );
	}

	//When the compiled model on screen was written.
	DateTime _compiledOnScreen;

	DateTime CompiledWriteTime()
	{
		var definition = _asset?.AbsolutePath;
		if ( string.IsNullOrWhiteSpace( definition ) ) return default;

		try
		{
			var compiled = System.IO.Path.ChangeExtension( definition, ".mdl_c" );
			return System.IO.File.Exists( compiled ) ? System.IO.File.GetLastWriteTimeUtc( compiled ) : default;
		}
		catch ( System.IO.IOException )
		{
			return default;
		}
	}

	//Model rebuilt externally (export changed): reload only the compiled model, not the definition
	void AssetSystem.IEventListener.OnAssetChanged( Asset asset )
	{
		if ( asset is null || asset != _asset ) return;

		//Ours, on its way: reloading here would only be done twice.
		if ( _liveCompilePending ) return;

		if ( CompiledWriteTime() == _compiledOnScreen ) return;

		ReloadCompiledModel();
	}

	/// <summary>Play one of the model's own clips in the preview, or stop.</summary>
	public void PlayModelClip( ModelDefinition.ModelClip clip ) => _preview?.PlayModelClip( clip );

	private void UpdateTitle()
	{
		var star = ModelDirty ? "*" : "";
		var model = _asset?.Path ?? "untitled";

		Title = $"Model - {model}{star}";
	}
}
