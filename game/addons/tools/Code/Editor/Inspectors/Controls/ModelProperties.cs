namespace Editor.Inspectors;

//Finding the model a property is talking about, for the editors of things that are named by one - a material group, a body group mask.
static class ModelProperties
{
	//That property holds either the model itself or the path to it, and both are worth following - a component names its model as a path more often than not.
	/// <summary>The model held by the property called <paramref name="modelParameter"/>, beside this one.</summary>
	public static Model Find( SerializedProperty property, string modelParameter )
	{
		if ( string.IsNullOrEmpty( modelParameter ) ) return null;
		if ( property?.Parent?.GetProperty( modelParameter ) is not { } modelProperty ) return null;

		if ( modelProperty.PropertyType != typeof( string ) )
			return modelProperty.GetValue<Model>( null );

		var path = modelProperty.GetValue<string>();

		return string.IsNullOrWhiteSpace( path ) ? null : Model.Load( path );
	}
}
