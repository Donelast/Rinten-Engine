using Facepunch.ActionGraphs;
using System.Text.Json.Nodes;

namespace Rinten;

//A prefab which is loaded and accessible via GameObject.GetPrefab( x )
internal partial class PrefabCacheScene : PrefabScene
{
	internal PrefabCacheScene() : base( false )
	{
	}

	//Contains the the JSON for the prefab after loading it's cached scene and expanding all prefab
	//instances. We cache this since we use this quite often to resolve nested prefab instance overrides.
	internal JsonObject FullPrefabInstanceJson { get; private set; }

	private Func<JsonObject, Json.Patch> calculateDifferences;

	internal Json.Patch CalculateDifferences( JsonObject instance ) => calculateDifferences( instance );

	//Contains all the prefab files that are referenced by this prefab scene.
	private HashSet<PrefabFile> referencedPrefabs = new();

	public override bool Load( GameResource resource ) => Load( resource, reuse: false );

	//Loads the prefab into this scene; on reuse it refreshes in place instead of rebuilding everything
	private bool Load( GameResource resource, bool reuse )
	{
		Assert.NotNull( resource );

		if ( !reuse ) Clear();

		if ( resource is not PrefabFile file )
		{
			Log.Warning( "Resource is not a PrefabFile" );
			return false;
		}

		Source = file;

		//Don't network spawn any GameObjects we create here
		using var suppressSpawnScope = SceneNetworkSystem.SuppressSpawnMessages();

		using var sourceScope = ActionGraph.PushSerializationOptions( file.SerializationOptions with { ForceUpdateCached = IsEditor } );
		using var sceneScope = Push();
		//Parsed once and kept on the file until its blobs change, rather than read off
		//disk on every refresh. Clears the file's binary data, which is now loaded.
		using var blobs = BlobDataSerializer.LoadTable( file.TakeBlobTable() );

		if ( file.RootObject is null )
		{
			file.RootObject = new GameObject( file.ResourceName ).Serialize();
			//Need to clear again because GO will be added to the scene, leading to id conflicts when deserializing it.
			Clear();
		}

		using ( CallbackBatch.Isolated() )
		{
			Deserialize( file.RootObject, new GameObject.DeserializeOptions
			{
				IsRefreshing = reuse,
				KeepJson = true,
			} );
		}

		//The cached snapshot outlives both the resource's loading scope and any ambient capture.
		using var suppressBlobs = BlobDataSerializer.Suppress();
		FullPrefabInstanceJson = Serialize( new SerializeOptions { SerializePrefabForDiff = true } );
		calculateDifferences = Json.CreateDifferenceCalculator( FullPrefabInstanceJson, DiffObjectDefinitions );

		//Iterate all gameobjects in scene and find prefab instances, add them to reference set
		referencedPrefabs = GetAllObjects( false ).Where( o => o.IsPrefabInstanceRoot ).Select( p => ResourceLibrary.Get<PrefabFile>( p.PrefabInstanceSource ) ).ToHashSet();

		return true;
	}

	/// <summary>Don't try and do this. You can't destroy a PrefabCacheScene.</summary>
	public override void Destroy()
	{
		throw new InvalidOperationException( $"Destroying a {nameof( PrefabCacheScene )} is not allowed. Did you mean to destroy the GameObject instance?" );
	}

	internal void Refresh( PrefabFile file )
	{
		Load( file, reuse: true );
		UpdateDependencies( file );
	}

	private void UpdateDependencies( PrefabFile file )
	{
		var dependantSet = new HashSet<PrefabFile>();
		BuildDependantSet( file, dependantSet, ResourceLibrary.GetAll<PrefabFile>().ToArray() );

		//Expanded hierarchies reference transitive dependencies too. Discovery order is not
		//load order: only rebuild a consumer once all of its affected dependencies are current.
		var ordered = new List<PrefabFile>( dependantSet.Count );
		while ( dependantSet.Count > 0 )
		{
			var ready = dependantSet.Where( x => x.CachedScene is null || !x.CachedScene.referencedPrefabs.Overlaps( dependantSet ) ).ToArray();
			if ( ready.Length == 0 )
			{
				Log.Warning( $"Cyclic prefab dependencies while refreshing {file.ResourceName}" );
				return;
			}

			foreach ( var dependant in ready )
			{
				dependantSet.Remove( dependant );
				ordered.Add( dependant );
			}
		}

		foreach ( var dependant in ordered )
		{
			dependant.CachedScene?.Load( dependant, reuse: true );
		}
	}

	private void BuildDependantSet( PrefabFile file, HashSet<PrefabFile> prefabScenesRequiringUpdate, PrefabFile[] prefabFiles )
	{
		foreach ( var pf in prefabFiles )
		{
			if ( prefabScenesRequiringUpdate.Contains( pf ) )
			{
				continue;
			}

			//Only check prefabs that already have a cached scene loaded. Prefabs without a cached scene will load
			//fresh data when first accessed, so we don't need to force-load them just to check dependencies.
			if ( pf.CachedScene is not PrefabCacheScene prefabScene )
			{
				continue;
			}

			if ( !prefabScene.IsValid() )
			{
				Log.Warning( $"Failed to update prefab dependencies, prefab {pf.ResourceName} is not valid" );
				continue;
			}

			if ( prefabScene == this )
			{
				continue;
			}

			if ( !prefabScene.referencedPrefabs.Contains( file ) )
			{
				continue;
			}

			prefabScenesRequiringUpdate.Add( pf );

			BuildDependantSet( pf, prefabScenesRequiringUpdate, prefabFiles );
		}
	}
}


