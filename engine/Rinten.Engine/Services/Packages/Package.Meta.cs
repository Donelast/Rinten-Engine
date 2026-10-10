using System.Text.Json;

namespace Rinten;

public partial class Package
{
	//Shortcuts to common metadata values

	/// <summary>Gets the name of the primary asset path stored in the package metadata. This could be null or empty.</summary>
	public string PrimaryAsset => GetMeta<string>( "PrimaryAsset" );

	/// <summary>Get metadata value from this package for given key. This will be specific to each <see cref="Package.Type"/>.</summary>
	public virtual T GetMeta<T>( string keyName, T defaultValue = default( T ) )
	{
		var json = GetJson();
		if ( json == null ) return defaultValue;

		if ( !json.Value.TryGetProperty( keyName, out var value ) )
			return defaultValue;

		try
		{
			//Special handling for string, value.Deserialize<string> on something that isn't a string doesn't work for whatever reason
			if ( typeof( T ) == typeof( string ) && value.ValueKind is JsonValueKind.Object )
				return (T)(object)value.GetRawText();

			return value.Deserialize<T>( Json.options ) ?? defaultValue;
		}
		catch ( JsonException e )
		{
			//if it was the wrong type, we don't care that much
			Log.Warning( $"Had a problem getting package meta \"{keyName}\" - {e}" );
			return defaultValue;
		}
	}

	Dictionary<string, object> metaCache;

	/// <summary><see cref="GetMeta"/> but with cache.</summary>
	public T GetCachedMeta<T>( string keyName, T defaultValue = default( T ) )
	{
		metaCache ??= new();

		if ( metaCache.TryGetValue( keyName, out var value ) && value is T t )
			return t;

		t = GetMeta<T>( keyName, defaultValue );
		metaCache[keyName] = t;
		return t;
	}

	/// <summary><see cref="GetMeta"/> but with cache.</summary>
	public T GetCachedMeta<T>( string keyName, Func<T> defaultValue )
	{
		metaCache ??= new();

		if ( metaCache.TryGetValue( keyName, out var value ) && value is T t )
			return t;

		t = GetMeta<T>( keyName, defaultValue() );
		metaCache[keyName] = t;
		return t;
	}

	internal virtual JsonElement? GetJson()
	{
		return null;
	}

	//Get the full ident with your choice of fidelity
	internal string GetIdent( bool withLocal, bool withVersion )
	{
		if ( !withLocal && !withVersion )
		{
			return $"{Org.Ident}.{Ident}";
		}

		if ( !withVersion )
		{
			return $"{Org.Ident}.{Ident}{(IsRemote ? "" : "#local")}";
		}

		if ( Revision is null || Revision.VersionId == default )
		{
			return $"{Org.Ident}.{Ident}";
		}

		return $"{Org.Ident}.{Ident}#{Revision.VersionId}";
	}

	//Compiler name to use when building this package's code. Will be of the form " org.ident ".
	internal string CompilerName => GetIdent( false, false );

	//Assembly name to use when building this package's code. Will be of the form " package.org.ident ".
	internal string AssemblyName => $"package.{CompilerName}";

	//Compiler name to use when building this package's editor code. Will be of the form " org.ident.editor ".
	internal string EditorCompilerName => $"{CompilerName}.editor";

	//Assembly name to use when building this package's editor code. Will be of the form " package.org.ident.editor ".
	internal string EditorAssemblyName => $"package.{EditorCompilerName}";
}
