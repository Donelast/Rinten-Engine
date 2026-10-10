using System.Text.Json.Serialization;
using static Rinten.BytePack;

namespace Rinten;

/// <summary>A resource loaded in the engine, such as a <see cref="Model"/> or <see cref="Material"/>.</summary>
[Expose]
public abstract partial class Resource : IValid, IJsonConvert, BytePack.ISerializer
{
	/// <summary>ID of this resource,</summary>
	[Hide, JsonIgnore]
	[Obsolete( "ResourceId is obsolete and will be removed in the future." )]
	public int ResourceId { get; protected set; }

	//Internal use only — a stable 64-bit hash of ResourcePath, used for networking and resource lookup.
	internal ulong ResourceIdLong { get; set; }

	/// <summary>Path to this resource.</summary>
	[Hide, JsonIgnore]
	public string ResourcePath { get; protected set; }

	/// <summary>File name of the resource without the extension.</summary>
	[Hide, JsonIgnore]
	public string ResourceName { get; protected set; }

	//This is what loads the resource. While this is alive the resource will be loaded.
	internal AsyncResourceLoader Manifest { get; set; }


	[Hide, JsonIgnore] public abstract bool IsValid { get; }
	[Hide, JsonIgnore] public virtual bool IsError => false;

	//True if this resource has been changed but the changes aren't written to disk
	[Hide, JsonIgnore] public virtual bool HasUnsavedChanges => false;

	internal virtual void Destroy()
	{
		//Unregister on main thread Null check because resource lirbary may already be gone.
		MainThread.Queue( () => { Game.Resources?.Unregister( this ); } );

		if ( Manifest != default ) MainThread.QueueDispose( Manifest );
		Manifest = default;

		GC.SuppressFinalize( this );
	}

	~Resource()
	{
		using var finalizing = MainThread.InFinalizer();
		Destroy();
	}

	internal static string FixPath( string filename )
	{
		if ( filename == null )
			return "";

		filename = filename.NormalizeFilename( false );
		if ( filename.EndsWith( "_c" ) ) filename = filename[..^2];
		filename = filename.TrimStart( '/' );

		return filename;
	}

	//Sets path, name and id and registers in the WeakIndex; for runtime/native resources only
	internal void RegisterWeakResourceId( string resourcePath )
	{
		ResourcePath = FixPath( resourcePath );
		ResourceName = System.IO.Path.GetFileNameWithoutExtension( ResourcePath );

#pragma warning disable CS0618 //Type or member is obsolete
		ResourceId = ResourcePath.FastHash();
#pragma warning restore CS0618 //Type or member is obsolete
		ResourceIdLong = ResourcePath.FastHash64();

		Game.Resources.RegisterWeak( this );
	}

	//Accessor for loading native resources, not great, doesn't need to handle GameResource
	internal static Resource Load( Type t, string filename )
	{
		if ( t == typeof( Material ) ) return Material.Load( filename );
		if ( t == typeof( Texture ) ) return Texture.Load( filename );
		if ( t == typeof( Model ) ) return Model.Load( filename );
		if ( t == typeof( SoundFile ) ) return SoundFile.Load( filename );
		if ( t == typeof( AnimGraph ) ) return AnimGraph.Load( filename );

		return null;
	}

	//The file this was read from has changed; let go of anything worked out from the old one. Called by the reload for this kind - see ResourceReload.
	internal virtual void OnReloaded()
	{
	}

	//Called on (re)load while file data is in memory; context is only valid during the call. Main thread
	internal virtual void OnLoaded( ResourceLoadContext context )
	{
	}

	//Finds the managed wrapper for a loaded resource and hands it the file data via OnLoaded
	internal static void OnResourceLoaded( string resourceName, IntPtr header )
	{
		//This fires from the engine frame, outside any context scope - the wrapper could be registered in either context's resource system, so check both.
		var resource = Engine.GlobalContext.Game.ResourceSystem.Get( typeof( Resource ), resourceName )
			?? Engine.GlobalContext.Menu.ResourceSystem.Get( typeof( Resource ), resourceName );

		resource?.OnLoaded( new ResourceLoadContext( resourceName, header ) );
	}

	public override string ToString()
	{
		return $"{GetType().Name}:{ResourceName}";
	}

	/// <summary>Should be called after the resource has been edited by the inspector</summary>
	public virtual void StateHasChanged()
	{

	}

	static object BytePack.ISerializer.BytePackRead( ref ByteStream bs, Type targetType )
	{
		var id = bs.Read<ulong>();
		//Fast path: already loaded in one of the indexes, garantueed for GameResources
		var resource = Game.Resources.GetByIdLong<Resource>( id );
		if ( resource != null ) return resource;

		//Slow path: native resource exists on disk but hasn't been loaded yet (common on connecting clients).
		var path = Game.Resources.LookupPath( id );
		if ( path == null ) return null;
		return Load( targetType, path );
	}

	static void BytePack.ISerializer.BytePackWrite( object value, ref ByteStream bs )
	{
		if ( value is not Resource resource )
		{
			bs.Write( 0UL );
			return;
		}

		bs.Write( resource.ResourceIdLong );
	}
}
