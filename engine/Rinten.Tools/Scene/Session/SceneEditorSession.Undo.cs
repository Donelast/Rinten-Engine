using Rinten.Helpers;
using System;
using System.Text.Json.Nodes;

namespace Editor;

public partial class SceneEditorSession
{
	public UndoSystem UndoSystem { get; } = new UndoSystem();

	internal SceneUndoSnapshot LastUndoSnapshot { get; set; }
	internal bool IsUndoScopeOpen => LastUndoSnapshot is { IsOpen: true };

	bool _suppressUndoSounds = false;

	private void InitUndo()
	{
		UndoSystem.Initialize();

		//annoy everyone as much as possible
		UndoSystem.OnUndo = ( x ) =>
		{
			HasUnsavedChanges = true;
		};

		UndoSystem.OnRedo = ( x ) =>
		{
			HasUnsavedChanges = true;
		};
	}

	sealed class SuppressUndoSoundScope : IDisposable
	{
		readonly SceneEditorSession _session;
		readonly bool _previous;

		public SuppressUndoSoundScope( SceneEditorSession session )
		{
			_session = session;
			_previous = session._suppressUndoSounds;
			session._suppressUndoSounds = true;
		}

		public void Dispose()
		{
			_session._suppressUndoSounds = _previous;
		}
	}

	/// <summary>Temporarily disables undo/redo sounds.</summary>
	public IDisposable SuppressUndoSounds()
	{
		return new SuppressUndoSoundScope( this );
	}

	/// <summary>Push the current selection into the undo system</summary>
	[Obsolete( "Manual selections snapshots are no longer working use UndoScope or AddUndo" )]
	public void PushUndoSelection()
	{
	}

	[Obsolete( "EditLog is no longer working use UndoScope or AddUndo" )]
	void Scene.ISceneEditorSession.OnEditLog( string name, object source )
	{
	}

	public void AddUndo( string name, Action undo, Action redo )
	{
		UndoSystem.Insert( name, undo, redo );
	}

	public ISceneUndoScope UndoScope( string name )
	{
		return new SceneUndoScope( this, name );
	}

	/// <summary>Cancels any open undo scopes, because we don't want the most recent change to appear in the undo stack.</summary>
	public void CancelUndoScope()
	{
		LastUndoSnapshot?.Cancel();
	}

	internal void SucceedUndoRedo()
	{
		if ( !_suppressUndoSounds && EditorPreferences.UndoSounds )
		{
			EditorUtility.PlayRawSound( "sounds/editor/success.wav" );
		}
	}

	internal void FailUndoRedo()
	{
		if ( !_suppressUndoSounds && EditorPreferences.UndoSounds )
		{
			EditorUtility.PlayRawSound( "sounds/editor/fail.wav" );
		}
	}
}

internal sealed class SceneUndoSnapshot : IDisposable
{
	sealed record ScopeSnapshot( JsonObject Scene, SelectionSnapshot Selection, ComponentSnapshot ComponentSnapshot, GameObjectSnapshot GameObjectSnapshot )
	{
		public bool Equals( ScopeSnapshot other )
		{
			if ( other == null )
				return false;
			if ( Scene == other.Scene &&
				Selection == other.Selection &&
				ComponentSnapshot == other.ComponentSnapshot &&
				GameObjectSnapshot == other.GameObjectSnapshot )
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( Scene, Selection, ComponentSnapshot, GameObjectSnapshot );
		}
	}

	sealed record SelectionSnapshot
	{
		public readonly GameObjectReference[] SelectedGoRefs;
		public readonly ComponentReference[] SelectedComponentRefs;

		public struct ObjectReference
		{
			public Type Type;
			public JsonNode Node;
		}

		public readonly ObjectReference[] SelectedObjectRefs;
		private readonly SceneEditorSession _session;

		public SelectionSnapshot( SceneEditorSession session )
		{
			_session = session;

			SelectedGoRefs = _session.Selection.OfType<GameObject>().Select( GameObjectReference.FromInstance ).ToArray();
			SelectedComponentRefs = _session.Selection.OfType<Component>().Select( ComponentReference.FromInstance ).ToArray();
			SelectedObjectRefs = _session.Selection
				.Where( x => x is not GameObject and not Component and not null )
				.Select( x => new ObjectReference { Type = x.GetType(), Node = Json.ToNode( x ) } )
				.ToArray();
		}

		public void Restore( Scene scene )
		{
			_session.Selection.Clear();
			foreach ( var goRef in SelectedGoRefs )
			{
				var go = goRef.Resolve( scene, true );
				if ( go.IsValid() )
				{
					_session.Selection.Add( go );
				}
			}

			foreach ( var compRef in SelectedComponentRefs )
			{
				var comp = compRef.Resolve( scene );
				if ( comp.IsValid() )
				{
					_session.Selection.Add( comp );
				}
			}

			//We have not garantuee those are still exist and are valid but we try
			foreach ( var objRef in SelectedObjectRefs )
			{
				var obj = Json.FromNode( objRef.Node, objRef.Type );
				if ( obj is IValid x && !x.IsValid() )
				{
					continue;
				}
				_session.Selection.Add( obj );
			}
		}

		public bool Equals( SelectionSnapshot other )
		{
			if ( other == null )
				return false;

			if ( SelectedGoRefs.Select( x => x.GameObjectId ).SequenceEqual( other.SelectedGoRefs.Select( x => x.GameObjectId ) ) &&
				SelectedComponentRefs.Select( x => x.GameObjectId ).SequenceEqual( other.SelectedComponentRefs.Select( x => x.GameObjectId ) ) &&
				SelectedObjectRefs.SequenceEqual( other.SelectedObjectRefs ) )
			{
				return true;
			}

			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( SelectedGoRefs, SelectedComponentRefs, SelectedObjectRefs );
		}
	}

	//Whether two snapshots hold the same thing. Each is serialised afresh, so comparing the arrays themselves said every edit changed something: a
	//brush stroke over tiles that were already there, or a click that picked a tile, was a step of its own for undo to walk back through doing nothing.
	static bool SameState<T>( IReadOnlyList<T> a, IReadOnlyList<T> b ) where T : JsonNode
	{
		if ( ReferenceEquals( a, b ) ) return true;
		if ( a is null || b is null || a.Count != b.Count ) return false;

		for ( var i = 0; i < a.Count; i++ )
		{
			if ( !JsonNode.DeepEquals( a[i], b[i] ) ) return false;
		}

		return true;
	}

	sealed record ComponentSnapshot
	{
		public readonly JsonNode[] State;
		public readonly ComponentReference[] ComponentRefs;

		public ComponentSnapshot( IEnumerable<Component> components )
		{
			ComponentRefs = components.Select( ComponentReference.FromInstance ).ToArray();

			var serializeOptions = new GameObject.SerializeOptions { };
			State = components.Select( comp =>
			{
				using var blobs = BlobDataSerializer.Capture();

				var json = comp.Serialize( serializeOptions );
				blobs.SaveTo( json );
				return json;
			} ).ToArray();
		}

		public void Restore( Scene scene )
		{
			for ( int i = 0; i < State.Length; i++ )
			{
				Component comp = null;
				comp = ComponentRefs[i].Resolve( scene );

				if ( comp is null )
				{
					continue;
				}

				comp.Deserialize( State[i].AsObject() );
			}
		}

		public void PostRestore( Scene scene )
		{
			for ( int i = 0; i < ComponentRefs.Length; i++ )
			{
				var comp = ComponentRefs[i].Resolve( scene );
				if ( !comp.IsValid() )
					continue;

				using var blobs = BlobDataSerializer.LoadFrom( State[i] );
				comp.PostDeserialize();
			}
		}

		public bool Equals( ComponentSnapshot other )
		{
			if ( other == null )
				return false;
			if ( SameState( State, other.State ) &&
				ComponentRefs.Select( x => x.ComponentId ).SequenceEqual( other.ComponentRefs.Select( x => x.ComponentId ) ) )
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( State, ComponentRefs );
		}
	}

	//Internal rather than private so that what an undo of a delete does can be checked without an editor window to hang a session off.
	internal sealed record GameObjectSnapshot
	{
		public readonly List<JsonObject> State;
		public readonly List<GameObjectReference> GameObjectRefs;
		public readonly List<GameObjectReference> GameObjectNextSiblingRefs;
		public readonly List<GameObjectReference> GameObjectParentRefs;
		private readonly Dictionary<Guid, PrefabInstanceData.OwnershipSnapshot> PrefabOwnership = new();

		private void CapturePrefabOwnership( GameObject go )
		{
			if ( go.IsPrefabInstanceRoot && !PrefabOwnership.ContainsKey( go.Id ) )
				PrefabOwnership.Add( go.Id, go.PrefabInstance.CaptureOwnership() );
		}

		public GameObjectSnapshot( Dictionary<GameObject, GameObjectUndoFlags> gameObjects )
		{
			var goCount = gameObjects.Count;
			GameObjectRefs = new( goCount );
			State = new( goCount );
			GameObjectNextSiblingRefs = new( goCount );
			GameObjectParentRefs = new( goCount );

			foreach ( var (go, flags) in gameObjects )
			{
				if ( !go.IsValid() )
				{
					Log.Info( $"Undo GameObjectSnapshot: GameObject queued for snapshot is not valid" );
					continue;
				}
				var serializeOptions = new GameObject.SerializeOptions
				{
					IgnoreChildren = !flags.HasFlag( GameObjectUndoFlags.Children ),
					IgnoreComponents = !flags.HasFlag( GameObjectUndoFlags.Components ),
					SerializeForUndo = true
				};

				using var blobs = BlobDataSerializer.Capture();

				var json = go.Serialize( serializeOptions );
				if ( json is null )
					continue;

				blobs.SaveTo( json );
				if ( serializeOptions.IgnoreChildren )
				{
					//Reparenting can detach nested roots and remove mappings from their old owners.
					for ( var ancestor = go.Parent; ancestor.IsValid(); ancestor = ancestor.Parent )
						CapturePrefabOwnership( ancestor );
					foreach ( var descendant in go.GetAllObjects( false ) )
						CapturePrefabOwnership( descendant );
				}
				GameObjectRefs.Add( GameObjectReference.FromInstance( go ) );
				State.Add( json );
				GameObjectNextSiblingRefs.Add( go.GetNextSibling( false ).IsValid() ? GameObjectReference.FromInstance( go.GetNextSibling( false ) ) : GameObjectReference.FromId( Guid.Empty ) );
				GameObjectParentRefs.Add( go.Parent.IsValid() ? GameObjectReference.FromInstance( go.Parent ) : GameObjectReference.FromId( Guid.Empty ) );
			}
		}

		public void Restore( Scene scene, HashSet<GameObjectReference> createdGos = null )
		{
			for ( int i = 0; i < State.Count; i++ )
			{
				GameObject go = null;
				if ( createdGos is not null && createdGos.Contains( GameObjectRefs[i] ) )
				{
					go = new GameObject();
					//just need the id for now to ensure references are resolved when doing full deserialize later
					go.DeserializeId( State[i] );
				}
				else
				{
					go = GameObjectRefs[i].Resolve( scene );
				}

				if ( go is null )
				{
					continue;
				}
			}
		}

		public void PostRestore( Scene scene, BlobDataSerializer.BlobContext blobs )
		{
			//second pass fully desiarizes and restores hierachy
			for ( int i = 0; i < State.Count; i++ )
			{
				GameObject go = GameObjectRefs[i].Resolve( scene );

				if ( go is null )
				{
					continue;
				}

				//Merge blobs into the shared context - component PostDeserialize is deferred to the batch
				//flush, so a per-object context would be gone before its blob data (eg. mesh) is read.
				blobs.LoadFrom( State[i] );
				go.Deserialize( State[i], new GameObject.DeserializeOptions { IsRefreshing = true } );
			}

			RestoreHierachy( scene );
			foreach ( var (id, ownership) in PrefabOwnership )
			{
				var root = scene.Directory.FindByGuid( id );
				if ( root.IsValid() ) ownership.Restore( root );
			}
		}

		private void RestoreHierachy( Scene scene )
		{
			//You could probably also sort the siblings in a smart way, but we will just brought force it in n^2
			for ( int j = 0; j < State.Count; j++ )
			{
				for ( int i = 0; i < State.Count; i++ )
				{
					GameObject go = GameObjectRefs[i].Resolve( scene );

					if ( go is null )
					{
						continue;
					}

					//restore position in scene hierachy
					var prevSibling = GameObjectNextSiblingRefs[i].Resolve( scene );
					var parent = GameObjectParentRefs[i].Resolve( scene );
					if ( prevSibling is not null )
					{
						prevSibling.AddSibling( go, true, false );
					}
					else if ( parent is not null )
					{
						go.SetParent( parent, false );
					}
					else
					{
						go.SetParent( scene );
					}
				}
			}
		}

		public bool Equals( GameObjectSnapshot other )
		{
			if ( other == null )
				return false;

			if ( SameState( State, other.State ) &&
				GameObjectRefs.Select( x => x.GameObjectId ).SequenceEqual( other.GameObjectRefs.Select( x => x.GameObjectId ) ) &&
				GameObjectNextSiblingRefs.Select( x => x.GameObjectId ).SequenceEqual( other.GameObjectNextSiblingRefs.Select( x => x.GameObjectId ) ) &&
				GameObjectParentRefs.Select( x => x.GameObjectId ).SequenceEqual( other.GameObjectParentRefs.Select( x => x.GameObjectId ) ) )
			{
				return true;
			}
			return false;
		}

		public override int GetHashCode()
		{
			return HashCode.Combine( State, GameObjectNextSiblingRefs, GameObjectParentRefs );
		}
	}

	private readonly SceneEditorSession _session;
	private readonly string _name;

	private ScopeSnapshot _initialState;

	private HashSet<GameObject> _createdGameObjects = new();

	private HashSet<Component> _createdComponents = new();

	private Dictionary<GameObject, GameObjectUndoFlags> _initalCapturedGameObjects = new();

	private HashSet<Component> _initialCapturedComponents = new();

	private Dictionary<GameObject, GameObjectReference> _destroyedGameObjects { get; } = new();

	private Dictionary<Component, ComponentReference> _destroyedComponents { get; } = new();

	private readonly HashSet<Guid> _prefabRootsToRefresh = new();

	private void TrackPrefabRoot( GameObject go )
	{
		if ( !go.IsValid() ) return;
		if ( go.IsPrefabInstance )
			_prefabRootsToRefresh.Add( go.OutermostPrefabInstanceRoot.Id );
		if ( go.Parent.IsValid() && go.Parent.IsPrefabInstance )
			_prefabRootsToRefresh.Add( go.Parent.OutermostPrefabInstanceRoot.Id );
	}

	private static void RefreshPrefabPatches( Scene scene, IEnumerable<Guid> rootIds )
	{
		foreach ( var id in rootIds )
		{
			var root = scene.Directory.FindByGuid( id );
			if ( root.IsValid() && root.IsOutermostPrefabInstanceRoot )
				root.PrefabInstance.RefreshPatch();
		}
	}

	private bool _captureDestructions = false;

	private bool _captureComponentCreations = false;

	private bool _captureGameObjectCreations = false;

	private bool _captureSelections = false;

	private bool _captureComponentChanges => _initialCapturedComponents.Count > 0;

	private bool _captureGameObjectChanges => _initalCapturedGameObjects.Count > 0;

	internal SceneUndoSnapshot( SceneUndoScope builder )
	{
		_session = builder.Session;

		using var sceneScope = _session.Scene.Push();

		_name = builder.Name;

		//resolve builder contents
		foreach ( var (gos, flags) in builder.CapturedGameObjects )
		{
			foreach ( var go in gos )
			{
				TrackPrefabRoot( go );
				//Keep full snapshots for hierarchy edits; property changes can be captured locally.
				if ( go.IsPrefabInstance && flags.HasFlag( GameObjectUndoFlags.Children ) )
				{
					_initalCapturedGameObjects[go.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
					continue;
				}

				if ( _initalCapturedGameObjects.ContainsKey( go ) )
				{
					_initalCapturedGameObjects[go] |= flags;
				}
				else
				{
					_initalCapturedGameObjects[go] = flags;
				}
			}
		}


		foreach ( var destroyedGo in builder.DestroyedGameObjects )
		{
			TrackPrefabRoot( destroyedGo );
			//if destroyed go is part of prefab we need to update it's instance cache
			if ( destroyedGo.IsPrefabInstance && !destroyedGo.IsOutermostPrefabInstanceRoot )
			{
				_initalCapturedGameObjects[destroyedGo.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}
			//If we delete a nested instance we need to let a potential parent instance know and need to capture it.
			else if ( destroyedGo.IsOutermostPrefabInstanceRoot && destroyedGo.Parent.IsValid() && destroyedGo.Parent.IsPrefabInstance )
			{
				_initalCapturedGameObjects[destroyedGo.Parent.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}

			//Serialise the object before it is destroyed; this copy is what undoing the delete restores
			_initalCapturedGameObjects[destroyedGo] = GameObjectUndoFlags.All;

			_destroyedGameObjects[destroyedGo] = GameObjectReference.FromInstance( destroyedGo );
		}

		foreach ( var destroyedComp in builder.DestroyedComponents )
		{
			TrackPrefabRoot( destroyedComp.GameObject );
			//if destroyed component is part of prefab we need to update it's instance cache
			if ( destroyedComp.GameObject.IsPrefabInstance )
			{
				_initalCapturedGameObjects[destroyedComp.GameObject.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}
			//A component cannot be put back on its own - restoring one resolves it and finds nothing
			//- so what is kept is the object it was on, which carries its components with it.
			if ( destroyedComp.GameObject.IsValid() )
				_initalCapturedGameObjects[destroyedComp.GameObject] = GameObjectUndoFlags.All;

			_destroyedComponents[destroyedComp] = ComponentReference.FromInstance( destroyedComp );
		}

		foreach ( var comp in builder.CapturedComponents )
		{
			TrackPrefabRoot( comp.GameObject );

			//only add if parent is not already watched or does not have component flag
			if ( !_initalCapturedGameObjects.ContainsKey( comp.GameObject ) || !_initalCapturedGameObjects[comp.GameObject].Contains( GameObjectUndoFlags.Components ) )
			{
				_initialCapturedComponents.Add( comp );
			}
		}

		//The objects of the meshes whose origins will be put back in the middle at the end - see CenterOrigins - kept from
		//before the edit as well, or undoing it would put the vertices back and leave the object where the middle went.
		WatchRecenteredObjects();

		_captureDestructions = builder.CaptureDeletions;
		_captureComponentCreations = builder.CaptureComponentCreations;
		_captureGameObjectCreations = builder.CaptureGameObjectCreations;
		_captureSelections = builder.CaptureSelections;

		//Undo snapshots

		SelectionSnapshot selection = null;
		if ( _captureSelections || _captureDestructions )
		{
			selection = new SelectionSnapshot( _session );
		}

		ComponentSnapshot componentSnapshot = null;
		if ( _initialCapturedComponents.Count > 0 )
		{
			componentSnapshot = new ComponentSnapshot( _initialCapturedComponents );
		}

		GameObjectSnapshot gameObjectSnapshot = null;
		if ( _initalCapturedGameObjects.Count > 0 )
		{
			gameObjectSnapshot = new GameObjectSnapshot( _initalCapturedGameObjects );
		}

		//No whole-scene snapshot any more, for deletions or anything else: what an edit touched is what it can be undone from, and that is what these hold.
		_initialState = new ScopeSnapshot( null, selection, componentSnapshot, gameObjectSnapshot );

		if ( _captureGameObjectCreations )
		{
			_session.Scene.Directory.OnGameObjectAdded += OnGameObjectAdded;
		}
		if ( _captureComponentCreations )
		{
			_session.Scene.Directory.OnComponentAdded += OnComponentAdded;
		}
	}

	private bool _cancelled;
	private bool _alreadyDisposed = false;

	internal bool IsOpen => !_alreadyDisposed;

	public void Cancel()
	{
		_cancelled = true;
	}

	public void Dispose()
	{
		if ( _alreadyDisposed )
			return;

		try
		{
			if ( !_cancelled )
			{
				DisposeInternal();
			}
		}
		finally
		{
			_session?.Scene?.Directory?.OnComponentAdded -= OnComponentAdded;
			_session?.Scene?.Directory?.OnGameObjectAdded -= OnGameObjectAdded;

			_alreadyDisposed = true;
		}
	}

	void DisposeInternal()
	{
		using var sceneScope = _session.Scene.Push();

		//Before the state after the edit is taken, so the edit and its origin are one step to undo.
		CenterOrigins();

		//Redo snapshots

		//we don't capture the scene here again because that is never needed for redo

		SelectionSnapshot selection = null;
		if ( _captureSelections || _captureDestructions )
		{
			selection = new SelectionSnapshot( _session );
		}

		Dictionary<GameObject, GameObjectUndoFlags> disposeWatchedGameObjects = new();

		//add all gos still valid and not destroyed
		foreach ( var (go, flags) in _initalCapturedGameObjects )
		{
			if ( !go.IsValid() || go.IsDestroyed )
			{
				continue;
			}

			TrackPrefabRoot( go );

			if ( !disposeWatchedGameObjects.ContainsKey( go ) )
			{
				disposeWatchedGameObjects.Add( go, flags );
			}
			else
			{
				disposeWatchedGameObjects[go] |= flags;
			}
		}

		//add all created gos
		HashSet<GameObjectReference> createdGameObjectRefs = new();
		foreach ( var go in _createdGameObjects )
		{
			if ( !go.IsValid() || go.IsDestroyed )
			{
				continue;
			}

			TrackPrefabRoot( go );
			//Need to capture the prefab root and only the prefab root if we edited an instance
			if ( go.IsPrefabInstance )
			{
				disposeWatchedGameObjects[go.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
				//If we are a prefabinstanceroot and ahve been added to a prefab we also need to capture the outer prefab
				if ( go.IsOutermostPrefabInstanceRoot && go.Parent.IsValid() && go.Parent.IsPrefabInstance )
				{
					disposeWatchedGameObjects[go.Parent.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
				}
			}
			else
			{
				disposeWatchedGameObjects[go] = GameObjectUndoFlags.All;
			}
			createdGameObjectRefs.Add( GameObjectReference.FromInstance( go ) );
		}

		//for all components that have been created capture their parent go
		HashSet<ComponentReference> createdComponentRefs = new();
		foreach ( var component in _createdComponents )
		{
			if ( !component.IsValid() )
			{
				//can hapen if component was created and immediatly destoryed within undo scope
				continue;
			}

			TrackPrefabRoot( component.GameObject );
			//Need to capture the prefab root and only the prefab root if we edited an instance
			if ( component.GameObject.IsPrefabInstance )
			{
				disposeWatchedGameObjects[component.GameObject.OutermostPrefabInstanceRoot] = GameObjectUndoFlags.All;
			}
			else if ( disposeWatchedGameObjects.ContainsKey( component.GameObject ) )
			{
				disposeWatchedGameObjects[component.GameObject] |= GameObjectUndoFlags.Components;
			}
			else
			{
				disposeWatchedGameObjects[component.GameObject] = GameObjectUndoFlags.Components;
			}
			createdComponentRefs.Add( ComponentReference.FromInstance( component ) );
		}

		HashSet<Component> disposeWatchedComponents = new();
		foreach ( var comp in _initialCapturedComponents )
		{
			if ( !comp.IsValid() )
			{
				continue;
			}

			TrackPrefabRoot( comp.GameObject );
			//only add if parent is not already watched or does not have component flag
			if ( !disposeWatchedGameObjects.TryGetValue( comp.GameObject, out var flags ) || !flags.Contains( GameObjectUndoFlags.Components ) )
			{
				disposeWatchedComponents.Add( comp );
			}
		}

		ComponentSnapshot componentSnapshot = null;
		if ( disposeWatchedComponents.Count > 0 )
		{
			componentSnapshot = new ComponentSnapshot( disposeWatchedComponents );
		}

		GameObjectSnapshot gameObjectSnapshot = null;
		if ( disposeWatchedGameObjects.Count > 0 )
		{
			gameObjectSnapshot = new GameObjectSnapshot( disposeWatchedGameObjects );
		}

		var disposeState = new ScopeSnapshot( null, selection, componentSnapshot, gameObjectSnapshot );

		var destroyedGameObjectRefs = _destroyedGameObjects.Select( x => x.Value ).ToArray();

		//The ones undo has to make again, in the shape Restore wants them - the mirror of the created ones a redo makes again.
		var destroyedGameObjectSet = new HashSet<GameObjectReference>( destroyedGameObjectRefs );
		var destroyedComponentRefs = _destroyedComponents.Select( x => x.Value ).ToArray();

		var prefabRootsToRefresh = _prefabRootsToRefresh.ToArray();
		//Full root snapshots already refreshed their patch during serialization.
		var refreshedRoots = disposeWatchedGameObjects
			.Where( x => x.Key.IsOutermostPrefabInstanceRoot && x.Value.HasFlag( GameObjectUndoFlags.Children ) )
			.Select( x => x.Key.Id );
		RefreshPrefabPatches( _session.Scene, prefabRootsToRefresh.Except( refreshedRoots ) );

		//if nothing changed, don't add an undo
		if ( _initialState == disposeState )
		{
			return;
		}

		//check if we have non selection changes
		if ( _captureComponentChanges || _captureGameObjectChanges || _captureDestructions || _captureGameObjectCreations || _captureComponentCreations )
		{
			_session.HasUnsavedChanges = true;
		}

		//Into the history on the disk, as the edit finishes. The same data the undo below holds: what the edit left behind, object by
		//object, and the ids of whatever it took away. A few hundred bytes appended to a file rather than the scene serialised again.
		{
			//What the edit left behind.
			var after = SceneEdit.Record( _name );

			Describe( after, disposeState );

			foreach ( var goRef in destroyedGameObjectRefs )
				SceneEdit.AddRemoved( after, goRef.GameObjectId, isGameObject: true );

			foreach ( var compRef in destroyedComponentRefs )
				SceneEdit.AddRemoved( after, compRef.ComponentId, isGameObject: false );

			//And what was there before it, which is the same thing the other way round: the
			//objects as they were, and the ones the edit made are the ones that have to go.
			var before = SceneEdit.Record( _name );

			Describe( before, _initialState );

			foreach ( var goRef in createdGameObjectRefs )
				SceneEdit.AddRemoved( before, goRef.GameObjectId, isGameObject: true );

			foreach ( var compRef in createdComponentRefs )
				SceneEdit.AddRemoved( before, compRef.ComponentId, isGameObject: false );

			if ( !SceneEdit.IsEmpty( after ) || !SceneEdit.IsEmpty( before ) )
			{
				_session.RecordEdit( SceneEdit.Both( _name, after, before ) );
			}
		}

		//copy we want to avoid capture this
		var preChangeStateCopy = _initialState;
		_session.AddUndo( _name,
			() =>
			{
				using var sceneScope = _session.Scene.Push();

				{
					//Must outlive the callback batch: deferred PostDeserialize reads blob data on flush
					using var blobs = BlobDataSerializer.LoadFromMemory( default );
					using var batch = CallbackBatch.Isolated();

					//What the edit destroyed is made again first, so that anything deserialised after
					//it can point at it - the same way a redo makes again what the edit created.
					preChangeStateCopy.GameObjectSnapshot?.Restore( _session.Scene, destroyedGameObjectSet );
					preChangeStateCopy.ComponentSnapshot?.Restore( _session.Scene );

					preChangeStateCopy.ComponentSnapshot?.PostRestore( _session.Scene );
					preChangeStateCopy.GameObjectSnapshot?.PostRestore( _session.Scene, blobs );

					//delete created components
					foreach ( var compRef in createdComponentRefs )
					{
						compRef.Resolve( _session.Scene )?.Destroy();
					}

					//delete created gos
					foreach ( var goRef in createdGameObjectRefs )
					{
						goRef.Resolve( _session.Scene )?.Destroy();
					}
				}

				RefreshPrefabPatches( _session.Scene, prefabRootsToRefresh );

				//At last restore selection
				preChangeStateCopy.Selection?.Restore( _session.Scene );
			},
			() =>
			{
				if ( disposeState.Scene != null )
				{
					throw new InvalidOperationException( "Redo should never use scene snapshots." );
				}

				using var sceneScope = _session.Scene.Push();

				//Must outlive the callback batch: deferred PostDeserialize reads blob data on flush
				using var blobs = BlobDataSerializer.LoadFromMemory( default );
				using ( CallbackBatch.Isolated() )
				{
					//delete destroyed components
					foreach ( var compRef in destroyedComponentRefs )
					{
						compRef.Resolve( _session.Scene )?.Destroy();
					}

					//delete destroyed gos
					foreach ( var goRef in destroyedGameObjectRefs )
					{
						goRef.Resolve( _session.Scene )?.Destroy();
					}

					//Restore Gos and pass created gos which need to be created first
					disposeState.GameObjectSnapshot?.Restore( _session.Scene, createdGameObjectRefs );
					disposeState.ComponentSnapshot?.Restore( _session.Scene );

					//Do actual deserialization
					disposeState.GameObjectSnapshot?.PostRestore( _session.Scene, blobs );
					disposeState.ComponentSnapshot?.PostRestore( _session.Scene );
				}

				RefreshPrefabPatches( _session.Scene, prefabRootsToRefresh );

				//restore selection
				if ( disposeState.Selection != null )
				{
					disposeState.Selection.Restore( _session.Scene );
				}
			} );
	}

	//Writes a snapshot's objects into a history record, per object, deletions included
	private static void Describe( JsonObject record, ScopeSnapshot snapshot )
	{
		if ( snapshot is null ) return;

		if ( snapshot.GameObjectSnapshot is not null )
		{
			var gameObjects = snapshot.GameObjectSnapshot;

			for ( int i = 0; i < gameObjects.State.Count; i++ )
			{
				SceneEdit.AddGameObject( record,
					gameObjects.GameObjectRefs[i].GameObjectId,
					gameObjects.GameObjectParentRefs[i].GameObjectId,
					gameObjects.GameObjectNextSiblingRefs[i].GameObjectId,
					gameObjects.State[i] );
			}
		}

		if ( snapshot.ComponentSnapshot is null ) return;

		var components = snapshot.ComponentSnapshot;

		for ( int i = 0; i < components.State.Length; i++ )
		{
			SceneEdit.AddComponent( record, components.ComponentRefs[i].ComponentId,
				components.State[i] as JsonObject );
		}
	}

	//── origins ───────────────────────────────────────────────────────────────── A mesh has its origin in the middle of it. Every edit of one ends in
	//one of these scopes, so this is where the origin is put back: at the end of the edit, not while a drag is still going, and inside the same undo step.

	//Whether a mesh has its origin put back in the middle when an edit of it ends.
	static bool Recenters( MeshComponent mesh )
		=> mesh.IsValid() && mesh.Mesh is not null && !mesh.KeepOrigin
		&& mesh.Scene is { IsEditor: true } && !mesh.GameObject.IsPrefabInstance;

	//The meshes an edit can change: captured on their own, or on an object captured with its components.
	List<MeshComponent> EditedMeshes()
	{
		var meshes = new List<MeshComponent>();

		foreach ( var comp in _initialCapturedComponents )
		{
			if ( comp is MeshComponent mesh && !meshes.Contains( mesh ) ) meshes.Add( mesh );
		}

		foreach ( var (go, flags) in _initalCapturedGameObjects )
		{
			if ( !flags.Contains( GameObjectUndoFlags.Components ) || !go.IsValid() ) continue;

			foreach ( var mesh in go.Components.GetAll<MeshComponent>() )
			{
				if ( !meshes.Contains( mesh ) ) meshes.Add( mesh );
			}
		}

		return meshes;
	}

	void Watch( GameObject go, GameObjectUndoFlags flags )
	{
		if ( !go.IsValid() ) return;

		_initalCapturedGameObjects[go] = _initalCapturedGameObjects.TryGetValue( go, out var had ) ? had | flags : flags;
	}

	//Keeps the properties of every edited mesh's object from before the edit, and of its children, whose local positions change to keep them still.
	void WatchRecenteredObjects()
	{
		foreach ( var mesh in EditedMeshes() )
		{
			if ( !Recenters( mesh ) ) continue;

			Watch( mesh.GameObject, GameObjectUndoFlags.Properties );

			foreach ( var child in mesh.GameObject.Children )
				Watch( child, GameObjectUndoFlags.Properties );
		}
	}

	//Puts the origin of every mesh the edit touched back in the middle of it. Only where the object's properties were kept from before -
	//see WatchRecenteredObjects - or the object is new, which undo takes away whole. Anything else would move with nothing to move it back.
	void CenterOrigins()
	{
		var meshes = EditedMeshes();

		foreach ( var go in _createdGameObjects )
		{
			if ( !go.IsValid() ) continue;

			foreach ( var mesh in go.Components.GetAll<MeshComponent>() )
			{
				if ( !meshes.Contains( mesh ) ) meshes.Add( mesh );
			}
		}

		foreach ( var mesh in meshes )
		{
			if ( !Recenters( mesh ) ) continue;

			var go = mesh.GameObject;
			var kept = _initalCapturedGameObjects.TryGetValue( go, out var flags ) && flags.Contains( GameObjectUndoFlags.Properties );

			if ( !kept && !_createdGameObjects.Contains( go ) ) continue;

			mesh.CenterOrigin();
		}
	}

	private void OnComponentAdded( Component comp )
	{
		_createdComponents.Add( comp );
	}

	private void OnGameObjectAdded( GameObject go )
	{
		_createdGameObjects.Add( go );
	}
}

internal class SceneUndoScope : ISceneUndoScope
{
	internal SceneEditorSession Session { get; }
	internal string Name { get; }
	internal bool CaptureSelections { get; private set; }
	internal bool CaptureGameObjectCreations { get; private set; }
	internal bool CaptureComponentCreations { get; private set; }
	internal List<(GameObject[], GameObjectUndoFlags)> CapturedGameObjects { get; } = new();
	internal List<Component> CapturedComponents { get; } = new();
	internal List<GameObject> DestroyedGameObjects { get; } = new();
	internal List<Component> DestroyedComponents { get; } = new();
	internal bool CaptureDeletions => DestroyedGameObjects.Count > 0 || DestroyedComponents.Count > 0;

	public SceneUndoScope( SceneEditorSession session, string name )
	{
		Session = session;
		Name = name;

		//Always capture selections If we ever want to make selections capture optional, it should be opt out (IngoreSelections) rather than opt in.
		CaptureSelections = true;
	}
	public ISceneUndoScope WithGameObjectCreations()
	{
		CaptureGameObjectCreations = true;
		return this;
	}
	public ISceneUndoScope WithGameObjectDestructions( IEnumerable<GameObject> gameObjects )
	{
		DestroyedGameObjects.AddRange( gameObjects );
		return this;
	}
	public ISceneUndoScope WithGameObjectDestructions( GameObject gameObject )
	{
		DestroyedGameObjects.Add( gameObject );
		return this;
	}
	public ISceneUndoScope WithGameObjectChanges( IEnumerable<GameObject> objects, GameObjectUndoFlags flags )
	{
		CapturedGameObjects.Add( (objects.ToArray(), flags) );
		return this;
	}
	public ISceneUndoScope WithGameObjectChanges( GameObject gameObject, GameObjectUndoFlags flags )
	{
		CapturedGameObjects.Add( (new[] { gameObject }, flags) );
		return this;
	}
	public ISceneUndoScope WithComponentCreations()
	{
		CaptureComponentCreations = true;
		return this;
	}
	public ISceneUndoScope WithComponentDestructions( IEnumerable<Component> components )
	{
		DestroyedComponents.AddRange( components );
		return this;
	}
	public ISceneUndoScope WithComponentDestructions( Component component )
	{
		DestroyedComponents.Add( component );
		return this;
	}
	public ISceneUndoScope WithComponentChanges( IEnumerable<Component> components )
	{
		foreach ( var comp in components )
		{
			CapturedComponents.Add( comp );
		}
		return this;
	}
	public ISceneUndoScope WithComponentChanges( Component component )
	{
		CapturedComponents.Add( component );
		return this;
	}
	public IDisposable Push()
	{
		return Session.LastUndoSnapshot = new SceneUndoSnapshot( this );
	}
}
