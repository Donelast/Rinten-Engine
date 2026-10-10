using Rinten.DataModel;
using System.IO;

namespace Editor;

//A project being made: answers, validation (Problem) and writing it out. No UI here
internal sealed class ProjectCreation
{
	public ProjectCreation( ProjectTemplate template )
	{
		Template = template;

		//Slashes the one way round, but the case left alone: this path is shown to somebody and the
		//folder is made from it, and the folder is called Rinten Projects rather than rinten projects.
		Location = LauncherPreferences.DefaultProjectLocation.NormalizeFilename( false, false );

		Title = DefaultProjectName();

		//Steam's copy, and only Steam's. Nothing is asked for unless this is a port, so the walk costs nothing to a project made from a template.
		if ( IsPorting ) sboxPath = EditorUtility.FindSBoxInstall();
	}

	//What it is being made out of.
	public ProjectTemplate Template { get; }

	//Built out of an existing legacy project rather than out of starter files.
	public bool IsPorting => Template?.IsPort ?? false;

	//What it is called.
	public string Title
	{
		get => title;
		set
		{
			title = value ?? string.Empty;

			//The ident follows the title until somebody types one of their own.
			if ( !identEdited ) ident = Trim( ConvertToIdent( title ) );
		}
	}

	//What the folder and the project file are called: the title, in the only characters a filename and a namespace can both hold.
	public string Ident
	{
		get => ident;
		set
		{
			ident = Trim( ConvertToIdent( value ?? string.Empty ) );
			identEdited = true;
		}
	}

	//The folder the project's own folder is made in.
	public string Location { get; set; }

	//The legacy project file to convert from, where there is one.
	public string PortSource
	{
		get => portSource;
		set
		{
			portSource = value;

			if ( !string.IsNullOrEmpty( portSource ) ) AdoptSourceIdentity();
		}
	}

	//The legacy install from Steam, read for base content when porting; a source checkout has no content
	public string SBoxPath => sboxPath;

	//Whether Steam's copy is there and is one the porter can read.
	public bool HasSBox => SBoxInstall is not null;

	//The install as the porter will read it, or null where there is none.
	public string SBoxInstall => EditorUtility.SBoxInstallAt( SBoxPath );

	public bool GitIgnore { get; set; } = true;

	//Whether this location becomes the one offered next time.
	public bool MakeDefaultLocation { get; set; }

	private string title = string.Empty;
	private string ident = string.Empty;
	private string portSource;
	private string sboxPath;
	private bool identEdited;

	//Where the project ends up, as somebody would read it.
	public string FullPath
	{
		get
		{
			if ( string.IsNullOrWhiteSpace( Location ) || string.IsNullOrWhiteSpace( Ident ) )
				return string.Empty;

			return Path.Combine( Location, Ident ).NormalizeFilename( false, false );
		}
	}

	//What stops this from being made, in one line, or nothing where nothing does.
	public string Problem
	{
		get
		{
			if ( IsPorting && string.IsNullOrEmpty( PortSource ) )
				return "Choose the s&box project to convert from";

			if ( IsPorting && !File.Exists( PortSource ) )
				return "That s&box project file is not there";

			if ( string.IsNullOrWhiteSpace( Title ) )
				return "Give it a name";

			if ( string.IsNullOrWhiteSpace( Ident ) || Ident.Length < 2 )
				return "The ident needs at least two letters";

			if ( string.IsNullOrWhiteSpace( Location ) )
				return "Say where it goes";

			if ( Path.Exists( FullPath ) )
				return $"{FullPath} already exists";

			//Never into the project being read. A port that writes over its own source leaves nothing to try again from.
			if ( IsPorting && Inside( FullPath, Path.GetDirectoryName( Path.GetFullPath( PortSource ) ) ) )
				return "That is inside the project it would be porting from";

			return null;
		}
	}

	public bool CanCreate => Problem is null;

	//Something worth knowing before pressing the button, which is not a reason not to press it.
	public string Note
	{
		get
		{
			if ( !IsPorting ) return null;

			if ( HasSBox ) return null;

			return "No s&box installed, so there is nothing to read the game's own content out of. " +
				"The project's own files are still converted; anything that leans on s&box's own content is skipped.";
		}
	}

	//Writes the project out and answers with its project file. Only ever called where
	//CanCreate - everything this could refuse to do has already been said on the page.
	public string Create()
	{
		var addonPath = Path.Combine( Location, Ident );

		Directory.CreateDirectory( addonPath );

		var config = new ProjectConfig
		{
			Ident = Ident,
			Title = Title,
			Org = "local",
			Type = "game",
			Schema = 1
		};

		//A template lays down starter content, which a port is about to replace. Applying both would
		//leave the new project holding two sets of assets that disagree about what the game is.
		if ( !IsPorting ) Template?.Apply( addonPath, ref config );

		config.CreatedWith = EngineVersion();

		var configPath = Path.Combine( addonPath, $"{config.Ident}{Project.Extension}" );

		File.WriteAllText( configPath, config.ToJson() );

		if ( GitIgnore ) CopyGitIgnore( addonPath );

		if ( MakeDefaultLocation ) LauncherPreferences.DefaultProjectLocation = Location;

		return configPath;
	}

	//Copies the template .gitignore if it exists; missing is not worth failing over
	static void CopyGitIgnore( string addonPath )
	{
		var to = Path.Combine( addonPath, ".gitignore" );

		if ( File.Exists( to ) ) return;

		var template = FileSystem.Root.GetFullPath( "/templates/template.gitignore" );

		if ( File.Exists( template ) )
		{
			File.Copy( template, to );
			return;
		}

		new Rinten.Diagnostics.Logger( "Generic" ).Warning(
			$"no .gitignore template at {template} - the project is made without one" );
	}

	//Takes the name and ident from the project being ported. Asking for them again would only invite a mismatch between the two projects.
	void AdoptSourceIdentity()
	{
		var name = Path.GetFileNameWithoutExtension( PortSource );
		var id = ConvertToIdent( name );

		try
		{
			var source = Json.Deserialize<ProjectConfig>( File.ReadAllText( PortSource ) );

			if ( !string.IsNullOrWhiteSpace( source?.Title ) ) name = source.Title;
			if ( !string.IsNullOrWhiteSpace( source?.Ident ) ) id = source.Ident;
		}
		catch
		{
			//Unreadable .sbproj - the filename is a good enough fallback.
		}

		title = name;
		ident = Trim( ConvertToIdent( id ) );

		//What was taken from the source stays taken: the title is not typed here when porting, so nothing would put the ident back.
		identEdited = true;
	}

	//Whether one path lies within another.
	static bool Inside( string path, string folder )
	{
		if ( string.IsNullOrEmpty( path ) || string.IsNullOrEmpty( folder ) ) return false;

		return Path.GetFullPath( path ).StartsWith( folder, StringComparison.OrdinalIgnoreCase );
	}

	//Null on a build with no release number; a source build's version is 0000000
	static Version EngineVersion()
	{
		var text = Rinten.Application.Version?.TrimStart( 'v' ).Split( '-' )[0];

		return Version.TryParse( text, out var version ) ? version : null;
	}

	//A name nothing is called yet: My Project, then My Project 1.
	static string DefaultProjectName()
	{
		var name = "My Project";
		var i = 1;

		while ( Path.Exists( Path.Combine( LauncherPreferences.DefaultProjectLocation, ConvertToIdent( name ) ) ) )
		{
			name = $"My Project {i++}";
		}

		return name;
	}

	static string ConvertToIdent( string title )
	{
		return System.Text.RegularExpressions.Regex
			.Replace( title.ToLower(), "[^A-Za-z0-9_]", "_" )
			.Trim( '_' );
	}

	//An ident is a folder name, and 32 characters is as long as one gets.
	static string Trim( string ident ) => ident.Length <= 32 ? ident : ident[..32];
}
