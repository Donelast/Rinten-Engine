using System;

namespace Editor;

public class ProjectList
{
	List<Project> All = new();

	public ProjectList()
	{
		Refresh();
	}

	public void Refresh()
	{
		All = new List<Project>();
		All.AddRange( EngineFileSystem.Config.ReadJsonOrDefault( "/addons.json", new List<Project>() ) );

		foreach ( var e in All )
		{
			e.LoadMinimal();
		}

		//Drop broken projects (not saved, in case they return); dedupe by folder, case-insensitive
		All = All.Where( x => !x.Broken ).DistinctBy( x => Folder( x.ConfigFilePath ), StringComparer.OrdinalIgnoreCase ).ToList();
	}

	//The folder a project's config file sits in, full and in one spelling - so that two paths
	//naming the same folder compare equal regardless of how each of them happens to be written.
	static string Folder( string configFilePath )
	{
		if ( string.IsNullOrWhiteSpace( configFilePath ) ) return configFilePath ?? string.Empty;

		var full = System.IO.Path.GetFullPath( configFilePath );

		return System.IO.Path.GetDirectoryName( full ) ?? full;
	}

	public void SaveList()
	{
		EngineFileSystem.Config.WriteJson( "/addons.json", All );
	}

	public IEnumerable<Project> GetAll() => All;

	/// <summary>Remove an item from the list. This doesn't save the changes.</summary>
	public bool Remove( Project item )
	{
		return All.Remove( item );
	}

	/// <summary>Adds a project from a file, returning it (or null); caller must call SaveList afterwards</summary>
	public Project TryAddFromFile( string path )
	{
		if ( !path.EndsWith( Project.Extension ) )
			path = System.IO.Path.Combine( path, Project.Extension );

		var cleanPath = System.IO.Path.GetFullPath( path );

		//Don't add the same project twice - by folder, and without minding case, for the same reason Refresh() dedupes the list the same way.
		if ( All.Where( a => string.Equals( Folder( a.ConfigFilePath ), Folder( cleanPath ), StringComparison.OrdinalIgnoreCase ) ).FirstOrDefault() is Project lp )
			return lp;

		var project = new Project { ConfigFilePath = cleanPath, LastOpened = DateTime.Now - TimeSpan.FromSeconds( 10 ) };
		project.LoadMinimal();

		//If it loaded broken, don't bother with it
		if ( project.Broken )
			return null;

		//If the schema needs upgrading then upgrade it and save before the engine loads it, so it's up to date at that point.
		if ( project.Config.Upgrade() )
		{
			project.Save();
		}

		All.Add( project );

		return project;
	}
}
