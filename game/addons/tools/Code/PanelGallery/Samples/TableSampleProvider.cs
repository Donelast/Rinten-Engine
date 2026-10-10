namespace Editor.Gallery;

/// <summary>Samples for types that belong to somebody else: engine maths, assets, the clock.</summary>
public sealed class TableSampleProvider : ISampleValueProvider
{
	readonly Dictionary<Type, Func<object>> table = new();

	public TableSampleProvider Add<T>( Func<T> make )
	{
		table[typeof( T )] = () => make();
		return this;
	}

	public bool TryCreate( Type type, ISampleValueProvider root, out object value )
	{
		if ( table.TryGetValue( type, out var make ) )
		{
			value = make();
			return true;
		}

		value = null;
		return false;
	}
}
