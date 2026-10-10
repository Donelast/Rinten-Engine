using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor.TreeBrowser;

//The folders a browser is allowed to be inside, and what they are called. The grid never shows a path
//from the drive root and never walks above one of these: outside them is the machine, not the project.
static class BrowseRoots
{
	public readonly record struct Root( string Path, string Title );

	/// <summary>Every branch the tree offers, longest path first so the nearest one wins.</summary>
	public static IReadOnlyList<Root> All()
	{
		var roots = new List<Root>();

		Add( roots, Project.Current?.GetAssetsPath(), "Assets" );
		Add( roots, Project.Current?.GetCodePath(), "Code" );

		var projectRoot = Project.Current?.GetRootPath();
		if ( !string.IsNullOrEmpty( projectRoot ) )
			Add( roots, Path.Combine( projectRoot, "Editor" ), "Editor" );

		Add( roots, global::Editor.FileSystem.Root?.GetFullPath( "/core/" ), "Core" );

		foreach ( var project in EditorUtility.Projects.GetAll() )
		{
			if ( project == Project.Current || project.Config.Ident == "menu" )
				continue;

			var title = string.IsNullOrEmpty( project.Config.Title ) ? project.Config.Ident : project.Config.Title;
			Add( roots, project.GetRootPath(), title );
		}

		Add( roots, projectRoot, Project.Current?.Config?.Title ?? "Project" );

		return roots.OrderByDescending( x => x.Path.Length ).ToList();
	}

	/// <summary>The branch a folder belongs to, or null when it is off the map.</summary>
	public static Root? Find( string fullPath )
	{
		if ( string.IsNullOrEmpty( fullPath ) )
			return null;

		var normalized = Normalize( fullPath );

		foreach ( var root in All() )
		{
			if ( IsInside( normalized, root.Path ) )
				return root;
		}

		return null;
	}

	/// <summary>What the path bar says: the branch name, then the way down to here.</summary>
	public static string Format( string fullPath )
	{
		if ( string.IsNullOrEmpty( fullPath ) )
			return string.Empty;

		var normalized = Normalize( fullPath );
		var root = Find( normalized );

		if ( root is null )
			return Path.GetFileName( normalized.TrimEnd( '/' ) );

		if ( normalized.Equals( root.Value.Path, StringComparison.OrdinalIgnoreCase ) )
			return root.Value.Title;

		var relative = normalized[(root.Value.Path.Length + 1)..];
		return $"{root.Value.Title}/{relative}";
	}

	/// <summary>Is there anywhere to go up to that is still inside the project?</summary>
	public static bool CanGoUp( string fullPath )
	{
		var normalized = Normalize( fullPath );
		var root = Find( normalized );

		if ( root is null )
			return false;

		return !normalized.Equals( root.Value.Path, StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>The folder above this one, or null when this one is a branch root.</summary>
	public static string Parent( string fullPath )
	{
		if ( !CanGoUp( fullPath ) )
			return null;

		return Directory.GetParent( Normalize( fullPath ) )?.FullName;
	}

	static void Add( List<Root> roots, string path, string title )
	{
		if ( string.IsNullOrEmpty( path ) || !Directory.Exists( path ) )
			return;

		var normalized = Normalize( path );
		if ( roots.Any( x => x.Path.Equals( normalized, StringComparison.OrdinalIgnoreCase ) ) )
			return;

		roots.Add( new Root( normalized, title ) );
	}

	static bool IsInside( string path, string root ) =>
		path.Equals( root, StringComparison.OrdinalIgnoreCase ) ||
		path.StartsWith( root + "/", StringComparison.OrdinalIgnoreCase );

	static string Normalize( string path )
	{
		try
		{
			return Path.GetFullPath( path ).Replace( '\\', '/' ).TrimEnd( '/' );
		}
		catch
		{
			return path.Replace( '\\', '/' ).TrimEnd( '/' );
		}
	}
}
