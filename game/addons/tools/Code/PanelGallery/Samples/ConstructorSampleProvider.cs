namespace Editor.Gallery;

//Interfaces, abstract classes and anything that needs arguments are declined, and the card shows its editor over a null - which is itself a state every editor has to survive.
/// <summary>The last resort: whatever a parameterless constructor gives, or the default of a struct.</summary>
public sealed class ConstructorSampleProvider : ISampleValueProvider
{
	public bool TryCreate( Type type, ISampleValueProvider root, out object value )
	{
		value = null;

		if ( type.IsInterface || type.IsAbstract || type.IsGenericTypeDefinition || type.IsPointer || type.IsByRef )
			return false;

		if ( type.IsValueType )
		{
			value = Activator.CreateInstance( type );
			return true;
		}

		if ( type.GetConstructor( Type.EmptyTypes ) is null )
			return false;

		value = Activator.CreateInstance( type );
		return true;
	}
}
