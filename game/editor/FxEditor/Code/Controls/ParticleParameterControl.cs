namespace Editor.FxEditor;

using Editor.Inspectors;

/// <summary>The name of a parameter, picked out of the ones the effect declares.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( ParticleParameterNameAttribute )] )]
public sealed class ParticleParameterControl : StringDropdownControl
{
	/// <summary>Which of the three lists this name is picked from.</summary>
	public ParticleParameterKind Kind { get; set; }

	/// <summary>Something the definition holds, to find the definition from.</summary>
	public object Owner { get; set; }

	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option>
		{
			new( "None", "block", "" )
		};

		var current = Read() ?? "";
		var found = string.IsNullOrWhiteSpace( current );

		foreach ( var parameter in Parameters() )
		{
			if ( string.IsNullOrWhiteSpace( parameter.Name ) ) continue;

			found |= string.Equals( current, parameter.Name, StringComparison.OrdinalIgnoreCase );

			options.Add( new Rinten.UI.Option( parameter.Name, IconFor(), parameter.Name ) );
		}

		//A parameter that has been deleted or renamed still shows, so the row says what it is set to rather than looking empty.
		if ( !found )
			options.Add( new Rinten.UI.Option( $"{current} (missing)", "error", current ) );

		return options;
	}

	string IconFor() => KindOf() switch
	{
		ParticleParameterKind.Vector => "open_with",
		ParticleParameterKind.Color => "palette",
		_ => "looks_one"
	};

	IEnumerable<ParticleDefinition.Parameter> Parameters()
	{
		var owner = Owner ?? Property?.Parent?.Targets?.FirstOrDefault();

		if ( ParticleEditorWindow.DefinitionOf( owner ) is not { } definition )
			return [];

		return KindOf() switch
		{
			ParticleParameterKind.Vector => definition.VectorParameters,
			ParticleParameterKind.Color => definition.ColorParameters,
			_ => definition.FloatParameters
		};
	}

	//Which kind this is: whatever the binding control said, or whatever the property itself is marked with when nobody said.
	ParticleParameterKind KindOf()
	{
		if ( Owner is not null )
			return Kind;

		return Property is not null && Property.TryGetAttribute<ParticleParameterNameAttribute>( out var attribute )
			? attribute.Kind
			: Kind;
	}
}
