using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rinten.Porting;

/// <summary>Rewrites the source engine's namespace out of a ported project and this engine's namespace in.</summary>
public static class PortNames
{
	//What the source engine calls its root namespace.
	private const string From = "Sandbox";

	//What this one calls it.
	private const string To = "Rinten";

	//A qualified name: the root, a dot, and something after it. The leading group keeps MySandbox. and Other.Sandbox. out of it.
	private static readonly Regex Qualified = new(
		$@"(^|[^\w.]){From}\.(?=\w)", RegexOptions.Compiled | RegexOptions.Multiline );

	//The root on its own, where a using or a namespace declaration puts it. Covers global using and using static alike.
	private static readonly Regex Declared = new(
		$@"\b(using|namespace)(\s+static\s+|\s+){From}\b", RegexOptions.Compiled );

	//The root as the whole of an XML element's text, which is how a project file writes <RootNamespace>
	//. Every type a library declares lands in the wrong namespace without it, and nothing says so.
	private static readonly Regex Element = new(
		$@"(?<=>){From}(?=</)", RegexOptions.Compiled );

	//Types renamed on the way here (theirs -> ours); whole words, only names unique to one engine
	private static readonly (string Theirs, string Ours)[] Types =
	{
		//Their editor widget that renders a scene into a panel. Ours says so in its own summary: "This replaces NativeRenderingWidget".
		("NativeRenderingWidget", "SceneRenderingWidget"),
	};

	//The same, as one expression, so a file is walked once.
	private static readonly Regex Renamed = new(
		$@"\b(?<name>{string.Join( "|", Types.Select( x => Regex.Escape( x.Theirs ) ) )})\b",
		RegexOptions.Compiled );

	//Source files whose text names types.
	private static readonly string[] CodeExtensions = { ".cs", ".razor", ".csproj" };

	//Documents that name a component type per object.
	private static readonly string[] SceneExtensions = { ".scene", ".prefab" };

	//Single-object assets that also hold measurements and need unit conversion
	private static readonly string[] ResourceExtensions = { ".terrain", ".clutter" };

	public static bool IsCode( string extension ) => CodeExtensions.Contains( extension );

	/// <summary>Whether this asset is one measured object rather than a scene of them.</summary>
	public static bool IsResource( string extension ) => ResourceExtensions.Contains( extension );
	public static bool IsScene( string extension ) => SceneExtensions.Contains( extension );

	/// <summary>Rewrite one source file in place. Returns whether anything changed.</summary>
	public static bool RewriteCodeFile( string path, string relative, PortReport report )
	{
		try
		{
			var text = File.ReadAllText( path );

			var rewritten = Qualified.Replace( text, $"$1{To}." );
			rewritten = Declared.Replace( rewritten, $"$1$2{To}" );
			rewritten = Element.Replace( rewritten, To );

			//And the few that were renamed rather than moved. A .csproj names no types, so it is left out of this one.
			if ( path.EndsWith( ".cs", StringComparison.OrdinalIgnoreCase )
				|| path.EndsWith( ".razor", StringComparison.OrdinalIgnoreCase ) )
			{
				rewritten = Renamed.Replace( rewritten, match =>
				{
					foreach ( var (theirs, ours) in Types )
					{
						if ( match.Value == theirs ) return ours;
					}

					return match.Value;
				} );
			}

			if ( rewritten == text ) return false;

			File.WriteAllText( path, rewritten );
			return true;
		}
		catch ( Exception e )
		{
			//The file is already copied; saying so beats taking the port down.
			report.Skipped.Add( $"{relative}: could not rewrite names - {e.Message}" );
			return false;
		}
	}

	/// <summary>Rewrite the type names in one scene or prefab in place. Returns how many were changed.</summary>
	public static int RewriteTypesInFile( string path, string relative, PortReport report )
	{
		JsonNode root;

		try
		{
			root = JsonNode.Parse( File.ReadAllText( path ) );
		}
		catch ( Exception e )
		{
			report.Skipped.Add( $"{relative}: could not read to rewrite names - {e.Message}" );
			return 0;
		}

		if ( root is null ) return 0;

		var changed = RewriteTypes( root );
		if ( changed == 0 ) return 0;

		try
		{
			File.WriteAllText( path, root.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
		}
		catch ( Exception e )
		{
			report.Skipped.Add( $"{relative}: could not write rewritten names - {e.Message}" );
			return 0;
		}

		return changed;
	}

	/// <summary>Walk a parsed document and rename every qualified name in it. Modified in place; returns how many strings changed.</summary>
	public static int RewriteTypes( JsonNode node )
	{
		switch ( node )
		{
			case JsonObject o:
			{
				var changed = 0;

				foreach ( var (key, value) in o.ToList() )
				{
					if ( value is JsonValue v && v.TryGetValue<string>( out var text ) )
					{
						var rewritten = Qualified.Replace( text, $"$1{To}." );
						if ( rewritten == text ) continue;

						o[key] = JsonValue.Create( rewritten );
						++changed;
						continue;
					}

					changed += RewriteTypes( value );
				}

				return changed;
			}

			case JsonArray a:
			{
				var changed = 0;

				for ( var i = 0; i < a.Count; i++ )
				{
					if ( a[i] is JsonValue v && v.TryGetValue<string>( out var text ) )
					{
						var rewritten = Qualified.Replace( text, $"$1{To}." );
						if ( rewritten == text ) continue;

						a[i] = JsonValue.Create( rewritten );
						++changed;
						continue;
					}

					changed += RewriteTypes( a[i] );
				}

				return changed;
			}

			default:
				return 0;
		}
	}
}
