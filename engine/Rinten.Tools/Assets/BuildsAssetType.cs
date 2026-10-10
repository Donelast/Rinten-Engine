using System;
using System.Linq;

namespace Editor;

/// <summary>Marks a static method that builds one kind of source asset into the compiled file beside it.</summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class BuildsAssetTypeAttribute : Attribute
{
	public string Extension { get; }

	public BuildsAssetTypeAttribute( string extension )
	{
		Extension = extension;
	}
}

//The builders addons have declared, by the extension each of them builds.
internal static class AssetBuilders
{
	/// <summary>Whether anything has said it can build this extension.</summary>
	public static bool Has( string extension ) => Find( extension ) is not null;

	/// <summary>Builds one asset, and says whether anything was written.</summary>
	public static bool Build( string extension, Asset asset )
	{
		if ( asset is null ) return false;

		var method = Find( extension );

		if ( method is null ) return false;

		try
		{
			return method.InvokeWithReturn<bool>( null, new object[] { asset } );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"{asset.Path}: could not be built - {e.Message}" );
			return false;
		}
	}

	private static MethodDescription Find( string extension )
	{
		if ( string.IsNullOrWhiteSpace( extension ) ) return null;

		foreach ( var found in EditorTypeLibrary.GetMethodsWithAttribute<BuildsAssetTypeAttribute>() )
		{
			if ( !string.Equals( found.Attribute.Extension, extension,
				StringComparison.OrdinalIgnoreCase ) ) continue;

			if ( found.Method is null ) continue;

			return found.Method;
		}

		return null;
	}
}
