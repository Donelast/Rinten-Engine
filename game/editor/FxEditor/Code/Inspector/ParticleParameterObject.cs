namespace Editor.FxEditor;

//Effect parameters as sheet rows, showing override or default; writing sets an override
internal sealed class ParticleParameterObject : SerializedObject
{
	readonly ParticlePlayer _player;

	public override string TypeName => "Parameters";
	public override string TypeTitle => "Parameters";

	public ParticleParameterObject( ParticlePlayer player, ParticleDefinition definition )
	{
		_player = player;

		PropertyList = new List<SerializedProperty>();

		foreach ( var parameter in definition.FloatParameters )
		{
			//A range on the parameter is a slider on the row, the way a [Range] on a member is - the effect said how far it is meant to go.
			var attributes = parameter.HasRange
				? new Attribute[] { new PropertyAttribute(), new RangeAttribute( parameter.Min, parameter.Max ) }
				: null;

			Add( parameter, typeof( float ), () => _player.GetFloat( parameter.Name ), value => _player.Set( parameter.Name, (float)value ), attributes );
		}

		foreach ( var parameter in definition.VectorParameters )
			Add( parameter, typeof( Vector3 ), () => _player.GetVector( parameter.Name ), value => _player.Set( parameter.Name, (Vector3)value ) );

		foreach ( var parameter in definition.ColorParameters )
			Add( parameter, typeof( ParticleGradient ), () => _player.GetColor( parameter.Name ), value => _player.SetGradient( parameter.Name, (ParticleGradient)value ) );
	}

	void Add( ParticleDefinition.Parameter parameter, Type type, Func<object> get, Action<object> set, Attribute[] attributes = null )
		=> PropertyList.Add( new ParticleParameterProperty( this, parameter.Name, type, get, set, attributes ) );

	/// <summary>Whether the effect declared anything at all.</summary>
	public bool IsEmpty => PropertyList.Count == 0;

	public override bool IsValid => _player.IsValid();
}

//One parameter of the effect, read off and written back through the player. A property backed by two delegates rather than by a field somewhere. What
//it stands for - "the value of the parameter called Size on this player" - is not a member of anything, so there is nothing for reflection to find.
internal sealed class ParticleParameterProperty : SerializedProperty
{
	readonly SerializedObject _parent;
	readonly string _name;
	readonly Type _type;
	readonly Func<object> _get;
	readonly Action<object> _set;

	static readonly Attribute[] Plain = [new PropertyAttribute()];

	readonly Attribute[] _attributes;

	public ParticleParameterProperty( SerializedObject parent, string name, Type type, Func<object> get, Action<object> set, Attribute[] attributes = null )
	{
		_parent = parent;
		_name = name;
		_type = type;
		_get = get;
		_set = set;
		_attributes = attributes ?? Plain;
	}

	public override SerializedObject Parent => _parent;
	public override string Name => _name;
	public override string DisplayName => _name;
	public override Type PropertyType => _type;
	public override bool IsProperty => true;

	//Said explicitly, because a sheet asks a property whether it is one of the ones it should show, and nothing hung this off a member to read it from.
	public override IEnumerable<Attribute> GetAttributes() => _attributes;

	public override T GetValue<T>( T defaultValue = default ) => _get() is T value ? value : defaultValue;

	public override void SetValue<T>( T value )
	{
		if ( value is null ) return;

		_parent.NotePreChange( this );
		_set( value );
		_parent.NoteChanged( this );
	}
}
