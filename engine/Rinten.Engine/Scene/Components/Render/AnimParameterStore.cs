namespace Rinten;

//Remembers animgraph parameters set on a renderer so they can be reapplied; per-type to avoid boxing
sealed class AnimParameterStore
{
	readonly SkinnedModelRenderer _renderer;

	SceneModel SceneModel => _renderer.SceneModel;

	Dictionary<string, float> _floats;
	Dictionary<string, int> _ints;
	Dictionary<string, bool> _bools;
	Dictionary<string, Vector3> _vectors;
	Dictionary<string, Rotation> _rotations;
	Dictionary<string, string> _enums;
	Dictionary<string, Transform> _targets;
	Dictionary<string, string> _targetBones;

	//The build of the graph everything was last said to.
	int _saidGeneration = -1;

	internal AnimParameterStore( SkinnedModelRenderer renderer )
	{
		_renderer = renderer;
	}

	//A parameter can be reached through more than one overload - an enum by index or by option name, a bool by float.
	//Every write drops the other stores' entry for the name, so the last write wins when the stored parameters reapply.

	internal void Set( string v, Vector3 value )
	{
		Clear( v );
		(_vectors ??= new( StringComparer.OrdinalIgnoreCase ))[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	internal void Set( string v, int value )
	{
		Clear( v );
		(_ints ??= new( StringComparer.OrdinalIgnoreCase ))[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	internal void Set( string v, float value )
	{
		Clear( v );
		(_floats ??= new( StringComparer.OrdinalIgnoreCase ))[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	internal void Set( string v, bool value )
	{
		Clear( v );
		(_bools ??= new( StringComparer.OrdinalIgnoreCase ))[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	internal void Set( string v, Rotation value )
	{
		Clear( v );
		(_rotations ??= new( StringComparer.OrdinalIgnoreCase ))[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	internal void Set( string v, Transform value )
	{
		Clear( v );
		(_targets ??= new( StringComparer.OrdinalIgnoreCase ))[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	//Set an enum parameter by option name (e.g. Set( "holdtype", "pistol" )).
	internal void Set( string v, string option )
	{
		Clear( v );
		(_enums ??= new( StringComparer.OrdinalIgnoreCase ))[v] = option;
		SceneModel?.SetAnimParameter( v, option );
	}

	internal void SetTargetBone( string v, string bone )
	{
		Clear( v );
		(_targetBones ??= new( StringComparer.OrdinalIgnoreCase ))[v] = bone ?? "";
		SceneModel?.SetAnimTargetBone( v, bone );
	}

	internal void UnsetTarget( string v )
	{
		Clear( v );
		SceneModel?.ClearAnimTarget( v );
	}

	//Getters return what was set; unknown names read as default. Option-name ids can't be read back

	internal bool GetBool( string v )
	{
		if ( _bools is not null && _bools.TryGetValue( v, out var stored ) ) return stored;

		return false;
	}

	internal int GetInt( string v )
	{
		if ( _ints is not null && _ints.TryGetValue( v, out var stored ) ) return stored;

		//An id set by option name is a number to whoever asks, and only the graph knows which number: the position of that name in the list it declared.
		return SceneModel?.GetInt( v ) ?? 0;
	}

	internal float GetFloat( string v )
	{
		if ( _floats is not null && _floats.TryGetValue( v, out var stored ) ) return stored;

		return 0.0f;
	}

	internal Vector3 GetVector( string v )
	{
		if ( _vectors is not null && _vectors.TryGetValue( v, out var stored ) ) return stored;

		return Vector3.Zero;
	}

	internal Rotation GetRotation( string v )
	{
		if ( _rotations is not null && _rotations.TryGetValue( v, out var stored ) ) return stored;

		return Rotation.Identity;
	}

	//Forget one name, whichever kind of thing it was set as.
	internal void Clear( string name )
	{
		_floats?.Remove( name );
		_ints?.Remove( name );
		_bools?.Remove( name );
		_vectors?.Remove( name );
		_rotations?.Remove( name );
		_targets?.Remove( name );
		_targetBones?.Remove( name );
		_enums?.Remove( name );
	}

	//Forget one name, and put it back to the graph's own default.
	internal void Reset( string name )
	{
		Clear( name );

		if ( SceneModel.IsValid() ) SceneModel.ResetAnimParameter( name );
	}

	//Forget everything, and put the graph back to its own defaults.
	internal void ClearAll()
	{
		_floats?.Clear();
		_ints?.Clear();
		_bools?.Clear();
		_vectors?.Clear();
		_rotations?.Clear();
		_enums?.Clear();
		_targets?.Clear();
		_targetBones?.Clear();

		if ( SceneModel.IsValid() )
		{
			SceneModel.ResetAnimParameters();
		}
	}

	internal bool Contains( string name )
	{
		return (_floats?.ContainsKey( name ) ?? false)
			|| (_ints?.ContainsKey( name ) ?? false)
			|| (_bools?.ContainsKey( name ) ?? false)
			|| (_vectors?.ContainsKey( name ) ?? false)
			|| (_rotations?.ContainsKey( name ) ?? false)
			|| (_enums?.ContainsKey( name ) ?? false);
	}

	//Total number of stored (modified) anim-graph parameters across all types.
	internal int Count => (_floats?.Count ?? 0) + (_ints?.Count ?? 0) + (_bools?.Count ?? 0) + (_vectors?.Count ?? 0) + (_rotations?.Count ?? 0) + (_enums?.Count ?? 0);

	//Say everything the game has set on this renderer, again. The store here is the only full record of it: a parameter set before
	//there was a scene object at all, a target, an enum set by its option name. So it is what a graph that has just been built is told.
	internal void Write()
	{
		var sceneModel = SceneModel;
		if ( !sceneModel.IsValid() ) return;

		if ( _targets is not null ) foreach ( var p in _targets ) sceneModel.SetAnimParameter( p.Key, p.Value );
		if ( _targetBones is not null ) foreach ( var p in _targetBones ) sceneModel.SetAnimTargetBone( p.Key, p.Value );

		if ( _vectors is not null ) foreach ( var p in _vectors ) sceneModel.SetAnimParameter( p.Key, p.Value );
		if ( _floats is not null ) foreach ( var p in _floats ) sceneModel.SetAnimParameter( p.Key, p.Value );
		if ( _ints is not null ) foreach ( var p in _ints ) sceneModel.SetAnimParameter( p.Key, p.Value );
		if ( _bools is not null ) foreach ( var p in _bools ) sceneModel.SetAnimParameter( p.Key, p.Value );
		if ( _rotations is not null ) foreach ( var p in _rotations ) sceneModel.SetAnimParameter( p.Key, p.Value );
		if ( _enums is not null ) foreach ( var p in _enums ) sceneModel.SetAnimParameter( p.Key, p.Value );

		_saidGeneration = sceneModel.GraphGeneration;
	}

	//Re-send everything if the graph was rebuilt (e.g. model change), since a new graph is at defaults
	internal void WriteIfGraphRebuilt()
	{
		var sceneModel = SceneModel;
		if ( !sceneModel.IsValid() ) return;
		if ( sceneModel.GraphGeneration == _saidGeneration ) return;

		Write();
	}
}
