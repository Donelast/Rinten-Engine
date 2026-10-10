namespace Editor.Inspectors;

/// <summary>Picks one of the material groups of the model named beside this property.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( Model.MaterialGroupAttribute )] )]
public sealed class MaterialGroupControl : StringDropdownControl
{
	int _groupsHash;

	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( "None", "" ) };

		foreach ( var group in Groups().OrderBy( x => x, StringComparer.OrdinalIgnoreCase ) )
		{
			options.Add( new Rinten.UI.Option( group, group ) );
		}

		return options;
	}

	public override void Rebuild()
	{
		_groupsHash = GroupsHash();

		base.Rebuild();
	}

	/// <summary>Which groups there are comes from the model, which is picked in another row of the same inspector - so the list is watched rather than read once.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( Property is null || GroupsHash() == _groupsHash ) return;

		Rebuild();
	}

	//The groups on offer: the model's, or the ones every selected model shares.
	HashSet<string> Groups()
	{
		if ( Property is null ) return new();

		return Property.IsMultipleValues
			? CommonGroups()
			: GroupsOf( ModelOf( Property ) );
	}

	//A group only counts when every selected model has it.
	HashSet<string> CommonGroups()
	{
		HashSet<string> common = null;

		foreach ( var property in Property.MultipleProperties )
		{
			var groups = GroupsOf( ModelOf( property ) );

			if ( groups.Count == 0 ) return new();

			if ( common is null )
				common = groups;
			else
				common.IntersectWith( groups );

			if ( common.Count == 0 ) break;
		}

		return common ?? new();
	}

	static HashSet<string> GroupsOf( Model model )
	{
		var groups = new HashSet<string>();

		if ( !model.IsValid() ) return groups;

		for ( var i = 0; i < model.MaterialGroupCount; i++ )
		{
			groups.Add( model.GetMaterialGroupName( i ) );
		}

		return groups;
	}

	//The model the attribute points at.
	static Model ModelOf( SerializedProperty property )
	{
		if ( !property.TryGetAttribute<Model.MaterialGroupAttribute>( out var attribute ) )
			return null;

		return ModelProperties.Find( property, attribute.ModelParameter );
	}

	int GroupsHash()
	{
		var hash = new HashCode();

		foreach ( var group in Groups().OrderBy( x => x, StringComparer.OrdinalIgnoreCase ) )
		{
			hash.Add( group );
		}

		return hash.ToHashCode();
	}
}
