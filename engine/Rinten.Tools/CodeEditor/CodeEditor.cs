using Rinten.Internal;
using System;

namespace Editor;

/// <summary>Interface for editors to open code files. Any class that implements this interface is automatically added to the list. An editor is only enabled if <see cref="IsInstalled"/> returns true. Decorate your implementation with a <see cref="TitleAttribute"/>.</summary>
public interface ICodeEditor
{
	/// <summary>Friendly display name for this code editor. Defaults to the name from <see cref="TitleAttribute"/> via <see cref="DisplayInfo"/>.</summary>
	public string Title => DisplayInfo.ForType( GetType(), true ).Name;

	/// <summary>Opens a file in the editor, optionally at a line and column.</summary>
	public void OpenFile( string path, int? line = null, int? column = null );

	/// <summary>Open the solution of all sandbox projects</summary>
	public void OpenSolution();

	/// <summary>Open given addon in the editor.</summary>
	public void OpenAddon( Project addon );

	/// <summary>Whether or not this editor is installed.</summary>
	public bool IsInstalled();
}

/// <summary>For opening source code files in whatever code editor the user has selected.</summary>
public static partial class CodeEditor
{
	private static ICodeEditor _current;
	/// <summary>The current code editor we're using.</summary>
	[Title( "Code Editor" )]
	public static ICodeEditor Current
	{
		get
		{
			if ( _current == null )
			{
				var editorName = EditorCookie.GetString( "CodeEditor", GetDefault() );
				var editorType = EditorTypeLibrary.GetTypes<ICodeEditor>().FirstOrDefault( t => t.Name == editorName );

				//Check if our selected editor is still valid
				if ( editorType != null )
				{
					var editor = editorType.Create<ICodeEditor>();
					if ( !editor.IsInstalled() )
					{
						Log.Warning( $"Code editor '{editorName}' not installed, using default" );
						editorType = EditorTypeLibrary.GetTypes<ICodeEditor>().FirstOrDefault( t => t.Name == GetDefault() );
					}
				}

				//Check if our selected editor even exists as a type
				if ( editorType == null )
				{
					Log.Warning( $"Code editor '{editorName}' not found, using default" );
					editorType = EditorTypeLibrary.GetTypes<ICodeEditor>().FirstOrDefault( t => t.Name == GetDefault() );
				}

				//If you seriously have no code editor installed, _current can just be null
				if ( editorType != null )
				{
					EditorCookie.SetString( "CodeEditor", editorType.Name ); //Make sure the cookie gets reset if we're falling back
					_current = editorType.Create<ICodeEditor>();
				}
			}
			return _current;
		}
		set
		{
			_current = value;
			EditorCookie.SetString( "CodeEditor", value.GetType().Name );
		}
	}

	//Tries to find an editor type with a matching name, and checks to see if it's installed on our system.
	private static TypeDescription GetEditorDescription( string editorName )
	{
		return EditorTypeLibrary.GetTypes<ICodeEditor>().Where( x => !x.IsInterface && x.Name == editorName && x.Create<ICodeEditor>().IsInstalled() ).FirstOrDefault();
	}

	//Decides on a default code editor to use, defaults to Visual Studio or Visual Studio Code if not
	//installed. Since code editors are made in addon space, we don't have their types ready to use.
	private static string GetDefault()
	{
		//Default to Visual Studio
		var editorType = GetEditorDescription( "VisualStudio" );
		if ( editorType != null ) return editorType.Name;

		//Default to Visual Studio Code
		editorType = GetEditorDescription( "VisualStudioCode" );
		if ( editorType != null ) return editorType.Name;

		//Find any available code editor..
		return EditorTypeLibrary.GetTypes<ICodeEditor>()
			.Where( x => !x.IsInterface && x.Create<ICodeEditor>().IsInstalled() )
			.FirstOrDefault()
			?.Name ?? null;
	}

	/// <summary>Friendly name for our current code editor.</summary>
	public static string Title
	{
		get
		{
			if ( CodeEditor.Current != null )
			{
				var title = CodeEditor.Current.Title;
				if ( !string.IsNullOrEmpty( title ) )
					return title;
			}

			return "Code Editor";
		}
	}

	private static void OpenFile( string path, int? line, int? column, string memberName )
	{
		ArgumentNullException.ThrowIfNullOrEmpty( path );

		if ( AssetSystem.FindByPath( path ) is { } asset )
		{
			if ( !IAssetEditor.OpenInEditor( asset, out var editor ) )
			{
				return;
			}

			if ( memberName is null || editor is null )
			{
				return;
			}

			editor.SelectMember( memberName );
			return;
		}

		if ( !TryResolve( path, out var resolved ) )
		{
			Log.Error( $"Couldn't resolve relative path: '{path}'" );
			return;
		}

		path = resolved;

		if ( AssertCodeEditor() )
			Run( () => Current?.OpenFile( path, line, column ), $"open '{path}'" );
	}

	//Resolves a recorded source path, relative to its declaring project, to a file on disk
	private static bool TryResolve( string path, out string full )
	{
		full = null;

		if ( string.IsNullOrWhiteSpace( path ) )
			return false;

		if ( System.IO.Path.IsPathRooted( path ) )
		{
			full = path;
			return true;
		}

		foreach ( var project in EditorUtility.Projects.GetAll() )
		{
			if ( !project.Active ) continue;
			if ( !project.HasCompiler ) continue;

			var candidate = System.IO.Path.Combine( project.GetCodePath(), path );

			if ( System.IO.File.Exists( candidate ) )
			{
				full = candidate;
				return true;
			}
		}

		foreach ( var root in SourceRoots )
		{
			var candidate = System.IO.Path.Combine( root, path );

			if ( System.IO.File.Exists( candidate ) )
			{
				full = candidate;
				return true;
			}
		}

		return false;
	}

	private static string[] sourceRoots;

	//Every folder this repository compiles a project out of, found once. A copy with no sources
	//beside it finds none, which costs one directory walk and leaves the answer where it was.
	private static string[] SourceRoots => sourceRoots ??= FindSourceRoots();

	private static string[] FindSourceRoots()
	{
		//The editor runs in game/, so the repository is one above it.
		if ( System.IO.Directory.GetParent( Environment.CurrentDirectory )?.FullName is not { } repository )
			return Array.Empty<string>();

		var found = new List<string>();

		foreach ( var folder in new[] { "engine", "game" } )
		{
			var at = System.IO.Path.Combine( repository, folder );

			if ( System.IO.Directory.Exists( at ) )
				CollectSourceRoots( at, 4, found );
		}

		return found.ToArray();
	}

	//Folders holding a project file, down to a depth. Build output holds nothing worth opening.
	private static void CollectSourceRoots( string folder, int depth, List<string> into )
	{
		if ( depth <= 0 ) return;

		try
		{
			if ( System.IO.Directory.EnumerateFiles( folder, "*.csproj" ).Any() )
				into.Add( folder );

			foreach ( var child in System.IO.Directory.EnumerateDirectories( folder ) )
			{
				var name = System.IO.Path.GetFileName( child );

				if ( name is "bin" or "obj" or ".vs" or ".git" or "node_modules" ) continue;

				CollectSourceRoots( child, depth - 1, into );
			}
		}
		catch ( System.Exception )
		{
			//A folder that cannot be listed is one with nothing to open in it.
		}
	}

	public static void OpenFile( ISourcePathProvider location )
	{
		OpenFile(
			location.Path,
			(location as ISourceLineProvider)?.Line,
			(location as ISourceColumnProvider)?.Column,
			(location as IMemberNameProvider)?.MemberName );
	}

	/// <summary>Opens a file in the current editor, optionally at a line and column.</summary>
	public static void OpenFile( string path, int? line = null, int? column = null )
	{
		OpenFile( path, line, column, null );
	}

	/// <summary>Returns true if the file exists and can be opened by the current code editor.</summary>
	public static bool CanOpenFile( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return false;

		if ( AssetSystem.FindByPath( path ) is not null )
			return true;

		//A rooted path that names nothing said yes before, and a relative one that the
		//engine's own projects hold said no - both of which this answers by looking.
		return TryResolve( path, out var full ) && System.IO.File.Exists( full );
	}

	/// <summary>Open the solution of all engine projects</summary>
	public static void OpenSolution()
	{
		if ( AssertCodeEditor() )
			Run( () => Current?.OpenSolution(), "open the solution" );
	}

	public static void OpenAddon( Project addon )
	{
		if ( AssertCodeEditor() )
			Run( () => Current?.OpenAddon( addon ), $"open '{addon?.Config?.Ident}'" );
	}

	//Runs one of the launches, saying what went wrong rather than throwing out of the click that asked for it.
	private static void Run( Action action, string what )
	{
		try
		{
			action();
		}
		catch ( Exception e )
		{
			Log.Error( e, $"Couldn't {what} in {Title}: {e.Message}" );
		}
	}

	private static bool AssertCodeEditor()
	{
		if ( Current == null )
		{
			EditorUtility.DisplayDialog( "No Code Editor", "No code editor found, you'll want something like Visual Studio." );
			return false;
		}
		return true;
	}

	/// <summary>Finds a .sln this path belongs to, this is pretty much entirely for internal usage to open engine slns</summary>
	public static string FindSolutionFromPath( string path )
	{
		if ( path == null || path.Length < 5 )
			throw new Exception( $"Couldn't find solution file from path \"{path}\"" );

		var addonFile = System.IO.Path.Combine( path, Project.Extension );
		if ( System.IO.File.Exists( addonFile ) )
		{
			return AddonSolutionPath();
		}

		var solutions = System.IO.Directory.EnumerateFiles( path, "*.slnx" ).ToArray();
		if ( solutions.Length > 0 )
		{
			return string.Join( ";", solutions );
		}

		//The top of the drive, where a file outside every project ends up - said, rather than thrown at as a null.
		if ( System.IO.Directory.GetParent( path ) is not { } parent )
			throw new Exception( $"Couldn't find solution file from path \"{path}\"" );

		return FindSolutionFromPath( parent.FullName );
	}

	public static string AddonSolutionPath()
	{
		return $"{Project.Current.GetRootPath()}/{Project.Current.Config.Ident}.slnx";
	}
}
