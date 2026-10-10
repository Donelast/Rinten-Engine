namespace Editor.Gallery;

//Asked first, because a control knows better than any table what it wants to show. The table is read once per provider; the catalog makes a new provider on every rebuild, which is what hotload calls.
/// <summary>Samples the controls declared for themselves: static methods marked [PanelGallerySample].</summary>
public sealed class AttributeSampleProvider : ISampleValueProvider
{
	Dictionary<Type, MethodDescription> methods;

	public bool TryCreate( Type type, ISampleValueProvider root, out object value )
	{
		methods ??= Collect();

		if ( methods.TryGetValue( type, out var method ) )
		{
			value = method.Invoke( null );
			return true;
		}

		value = null;
		return false;
	}

	static Dictionary<Type, MethodDescription> Collect()
	{
		var table = new Dictionary<Type, MethodDescription>();

		foreach ( var (method, attribute) in EditorTypeLibrary.GetMethodsWithAttribute<PanelGallerySampleAttribute>() )
		{
			if ( method.Parameters.Length != 0 )
			{
				Log.Warning( $"Panel Gallery: sample {method.TypeDescription?.Name}.{method.Name} takes parameters" );
				continue;
			}

			var type = attribute.ForType ?? method.ReturnType;

			if ( type is null || type == typeof( void ) ) continue;

			table[type] = method;
		}

		return table;
	}
}
