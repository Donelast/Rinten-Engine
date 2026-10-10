using Rinten.DataModel;
using System.IO;
using System.Text.Json;
namespace Editor;

internal class ProjectTemplate
{
	ProjectConfig Config { get; init; }
	string TemplatePath { get; init; }

	public string AddonType => Config.Type.ToLower();
	public string Title => Config.Title;

	//True for the one entry that isn't a folder of starter files: picking it means the project is built out of an existing legacy project instead.
	public bool IsPort { get; init; }

	/// <summary>Whether porting from s&amp;box is offered when making a project.</summary>
	public static bool PortOffered => false;

	//The port entry. It has no files to lay down, so it carries no template path.
	public static ProjectTemplate Port() => new( new ProjectConfig
	{
		Type = "game",
		Title = "Port from s&box",
		Org = "local",
		Schema = 1
	}, null )
	{
		IsPort = true,
		Icon = "move_down",
		Order = int.MaxValue,
		Description = "Convert an existing s&box project's models, materials and art into a new one."
	};

	//All templates (folders under /templates plus the port entry), discovered once and cached
	public static IReadOnlyList<ProjectTemplate> All() => all ??= Discover();

	static IReadOnlyList<ProjectTemplate> all;

	//Relative to the game directory.
	const string TemplatesDirectory = "/templates";

	static IReadOnlyList<ProjectTemplate> Discover()
	{
		var found = new List<ProjectTemplate>();

		if ( FileSystem.Root.DirectoryExists( TemplatesDirectory ) )
		{
			foreach ( var directory in FileSystem.Root.FindDirectory( TemplatesDirectory ) )
			{
				var templateRoot = $"{TemplatesDirectory}/{directory}";
				var addonPath = $"{templateRoot}/$ident{Project.Extension}";

				if ( !FileSystem.Root.FileExists( addonPath ) )
					continue;

				var addon = Json.Deserialize<ProjectConfig>( FileSystem.Root.ReadAllText( addonPath ) );

				if ( addon == null )
					continue;

				if ( addon.Type == "library" )
					continue;

				found.Add( new ProjectTemplate( addon, templateRoot ) );
			}
		}

		//Not a folder on disk, so it is added by hand. Order puts it last; hidden while the porter is not offered
		if ( PortOffered ) found.Add( Port() );

		return found.OrderBy( x => x.Order ).ToList();
	}

	public string Icon { get; set; } = "question_mark";
	public int Order { get; set; } = 0;
	public string Description { get; set; } = "No description provided.";

	public ProjectTemplate( ProjectConfig templateConfig, string path )
	{
		Config = templateConfig;
		TemplatePath = path;

		if ( Config.TryGetMeta( "ProjectTemplate", out DisplayData display ) )
		{
			Icon = display.Icon;
			Order = display.Order ?? 0;
			Description = display.Description ?? "No description provided.";
		}
	}

	public void Apply( string targetDir, ref ProjectConfig config )
	{
		//The port entry has nothing to copy - the source project is the template.
		if ( IsPort || string.IsNullOrEmpty( TemplatePath ) )
			return;

		var sourceDir = Editor.FileSystem.Root.GetFullPath( TemplatePath );

		if ( !Directory.Exists( sourceDir ) )
			return;

		//Log.Info( $"Copying {Title} template to destination folder: {targetDir}" );

		CopyFolder( sourceDir, targetDir, config.Ident, config.Title );

		//Grab the project file from the template, and apply what we can. We'll store the old ident and title and restore it later
		var ident = config.Ident;
		var title = config.Title;

		var fullPath = Path.Combine( sourceDir, $"$ident{Project.Extension}" );

		if ( File.Exists( fullPath ) )
		{
			//Grab everything we can from the template
			config = JsonSerializer.Deserialize<ProjectConfig>( File.ReadAllText( fullPath ) );

			//Restore ident & title from our addon's preferences
			config.Ident = ident;
			config.Title = title;

			//Clear out ProjectTemplate from our new addon. It's not needed for end users.
			config.SetMeta( "ProjectTemplate", null );

			//Log.Info( $"OK" );
		}
		else
		{
			//Log.Warning( $"Something went wrong while applying template to new project" );
		}
	}

	void CopyFolder( string sourceDir, string targetDir, string addonIdent, string title )
	{
		//Skip dot-folders (.git, .rinten, .vs) when copying a template
		var name = Path.GetFileName( sourceDir.TrimEnd( Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar ) );

		if ( name.StartsWith( '.' ) )
		{
			return;
		}

		Directory.CreateDirectory( targetDir );

		foreach ( var file in Directory.GetFiles( sourceDir ) )
		{
			CopyAndProcessFile( file, targetDir, addonIdent, title );
		}

		foreach ( var directory in Directory.GetDirectories( sourceDir ) )
		{
			CopyFolder( directory, Path.Combine( targetDir, Path.GetFileName( directory ) ), addonIdent, title );
		}
	}

	void CopyAndProcessFile( string file, string targetDir, string addonIdent, string title )
	{
		var targetname = Path.Combine( targetDir, Path.GetFileName( file ) );

		//Replace $ident with our ident in file name
		targetname = targetname.Replace( "$ident", addonIdent );

		if ( file.EndsWith( ".cs" ) || file.EndsWith( ".json" ) )
		{
			var txt = System.IO.File.ReadAllText( file );
			txt = txt.Replace( "$title", title );
			txt = txt.Replace( "$ident", addonIdent );
			System.IO.File.WriteAllText( targetname, txt );
		}
		else
		{
			File.Copy( file, targetname );
		}
	}

	class DisplayData
	{
		public string Icon { get; set; }
		public int? Order { get; set; }
		public string Description { get; set; }
	}
}
