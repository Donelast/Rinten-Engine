namespace Editor.Gallery;

/// <summary>Providers asked in order; the first that answers wins.</summary>
public sealed class SampleValueChain : ISampleValueProvider
{
	readonly IReadOnlyList<ISampleValueProvider> providers;

	public SampleValueChain( params ISampleValueProvider[] providers )
	{
		this.providers = providers ?? [];
	}

	public bool TryCreate( Type type, ISampleValueProvider root, out object value )
	{
		root ??= this;

		foreach ( var provider in providers )
		{
			try
			{
				if ( provider.TryCreate( type, root, out value ) )
					return true;
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Panel Gallery: {provider.GetType().Name} threw making a sample {type.Name}" );
			}
		}

		value = null;
		return false;
	}
}
