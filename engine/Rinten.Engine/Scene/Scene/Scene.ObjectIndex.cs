using Rinten.Internal;
using Rinten.UI;
using Rinten.Utility;

namespace Rinten;

//Active components and systems indexed by type, base types and interfaces for fast GetAll<T>

public partial class Scene : GameObject
{
	//objects collections indexed by type
	[SuppressNullKeyWarning]
	Dictionary<Type, HashSetEx<object>> objectIndex = new();

	//all objects in the index
	HashSet<object> objectsInIndex = new();

	//Bumped whenever anything enters or leaves the index, so cached object lists know they're stale
	internal int DirectoryVersion { get; private set; } = 1;

	//Bump DirectoryVersion when the hierarchy changes without index changes, e.g. reparenting
	internal void ShapeChanged() => DirectoryVersion++;

	//Component magic
	internal HashSetEx<Component> updateComponents = new();
	internal HashSetEx<Component> fixedUpdateComponents = new();
	internal HashSetEx<Component> preRenderComponents = new();

	//Cached per-type list of indexable types (bases and interfaces), cleared on hotload
	static readonly ReflectionCache<Type, Type[]> indexableTypesCache = new( rootType =>
	{
		var types = new List<Type>();
		var t = rootType;
		while ( t is not null )
		{
			if ( t == typeof( object ) ) break;
			if ( t == typeof( Component ) ) break;
			if ( t == typeof( GameObjectSystem ) ) break;
			if ( t == typeof( Panel ) ) break;
			if ( t == typeof( Label ) ) break;

			types.Add( t );
			types.AddRange( t.GetInterfaces() );

			t = t.BaseType;
		}
		return types.ToArray();
	} );

	//Should only be called when destroying the scene. This here just to avoid
	//unregistering all of the objects when we don't need to, because we're just quitting.
	void ClearObjectIndex()
	{
		foreach ( var t in objectIndex.Values )
		{
			t?.Clear();
		}

		objectsInIndex.Clear();
		objectIndex.Clear();
		updateComponents.Clear();
		fixedUpdateComponents.Clear();
		preRenderComponents.Clear();
	}

	//When hotload occurs, the interfaces etc could have changed. So first of all we want to go
	//through and remove any null entries, then we want to go through and re-add everything.
	void HotloadObjectIndex()
	{
		var ft = FastTimer.StartNew();

		//copy the list
		var temporaryHash = objectsInIndex.ToList();

		//clear the old list
		objectsInIndex.Clear();
		objectIndex.Clear();

		//We can't clear updateComponents etc because ActionGraph add to them manually

		//re-add everything
		foreach ( var obj in temporaryHash )
		{
			if ( obj is null ) continue;

			AddObjectToDirectory( obj );
		}

		if ( ft.ElapsedMilliSeconds > 10 )
		{
			Log.Warning( $"HotloadObjectIndex took {ft.ElapsedMilliSeconds:0.0}ms" );
		}
	}


	//Adds object instance, indexed by type, to the directory so that its values are accessible by GetAll
	internal void AddObjectToDirectory( object obj )
	{
		if ( !objectsInIndex.Add( obj ) ) return;

		DirectoryVersion++;

		foreach ( var t in indexableTypesCache[obj.GetType()] )
		{
			objectIndex.GetOrCreate( t ).Add( obj );
		}

		//little bit of magic for components
		if ( obj is Component c )
		{
			if ( c is IUpdateSubscriber || c.OnComponentUpdate is not null ) updateComponents.Add( c );
			if ( c is IFixedUpdateSubscriber || c.OnComponentFixedUpdate is not null ) fixedUpdateComponents.Add( c );
			if ( c is IPreRenderSubscriber ) preRenderComponents.Add( c );
		}
	}

	//Adds object instance, indexed by type, to the directory so that its values are accessible by GetAll
	internal void RemoveObjectFromDirectory( object obj )
	{
		if ( !objectsInIndex.Remove( obj ) ) return;

		DirectoryVersion++;

		foreach ( var t in indexableTypesCache[obj.GetType()] )
		{
			objectIndex.GetOrCreate( t ).Remove( obj );
		}

		//little bit of magic for components
		if ( obj is Component c )
		{
			if ( c is IUpdateSubscriber || c.OnComponentUpdate is not null ) updateComponents.Remove( c );
			if ( c is IFixedUpdateSubscriber || c.OnComponentFixedUpdate is not null ) fixedUpdateComponents.Remove( c );
			if ( c is IPreRenderSubscriber ) preRenderComponents.Remove( c );
		}
	}

	/// <summary>Get all objects of this type. This could be a component or a GameObjectSystem, or other stuff in the future.</summary>
	[Pure]
	public ObjectsOf<T> GetAll<T>() => new( objectIndex );

	/// <summary>The objects of one type in a scene, as <see cref="GetAll{T}()"/> hands them back.</summary>
	public readonly struct ObjectsOf<T> : IEnumerable<T>
	{
		private readonly Dictionary<Type, HashSetEx<object>> index;

		internal ObjectsOf( Dictionary<Type, HashSetEx<object>> index ) => this.index = index;

		public Enumerator GetEnumerator()
		{
			if ( index is null || !index.TryGetValue( typeof( T ), out var set ) || set.Count == 0 )
				return default;

			return new Enumerator( set );
		}

		IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

		/// <summary>Walks one type's objects, passing over the ones that are gone.</summary>
		public struct Enumerator : IEnumerator<T>
		{
			private HashSetEx<object>.Enumerator inner;
			private readonly bool open;

			internal Enumerator( HashSetEx<object> set )
			{
				inner = set.EnumerateLocked().GetEnumerator();
				open = true;
				Current = default;
			}

			public T Current { get; private set; }

			object System.Collections.IEnumerator.Current => Current;

			public bool MoveNext()
			{
				if ( !open ) return false;

				while ( inner.MoveNext() )
				{
					var c = (T)inner.Current;

					if ( c is null ) continue;
					if ( c is IValid v && !v.IsValid ) continue;

					Current = c;
					return true;
				}

				return false;
			}

			public void Dispose()
			{
				if ( open ) inner.Dispose();
			}

			public void Reset() => throw new NotSupportedException();
		}
	}

	/// <summary>Get all objects of this type. This could be a component or a GameObjectSystem, or other stuff in the future.</summary>
	[Pure]
	public void GetAll<T>( List<T> target )
	{
		if ( !objectIndex.TryGetValue( typeof( T ), out var set ) || set.Count == 0 )
			return;

		foreach ( var e in set.EnumerateLocked() )
		{
			T c = (T)e;
			if ( c is null ) continue;
			if ( c is IValid v && !v.IsValid ) continue;

			target.Add( c );
		}
	}

	/// <summary>Gets the first object found of this type. This could be a component or a GameObjectSystem, or other stuff in the future.</summary>
	[Pure]
	public T Get<T>()
	{
		if ( !objectIndex.TryGetValue( typeof( T ), out var set ) || set.Count == 0 )
			return default;

		foreach ( var e in set.EnumerateLocked() )
		{
			T c = (T)e;
			if ( c is null ) continue;
			if ( c is IValid v && !v.IsValid ) continue;
			return c;
		}

		return default;
	}
}
