namespace Editor.Gallery;

/// <summary>Makes a sample value for a type; root lets providers ask the whole chain for element values</summary>
public interface ISampleValueProvider
{
	bool TryCreate( Type type, ISampleValueProvider root, out object value );
}
