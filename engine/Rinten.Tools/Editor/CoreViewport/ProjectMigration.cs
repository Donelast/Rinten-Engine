using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rinten;

namespace Editor;

//Renames a project's assets from the old ess-prefixed extensions to the plain ones - .essmdl to .mdl and so on - and rewrites
//every reference to them in the project's text files. One-off, for projects ported before the rename. New ports come out right.
internal static class ProjectMigration
{
	//Old extension to new, sources and compiled both. Compiled material and texture caches are
	//deleted instead - they are rebuilt on demand and deleting beats carrying stale ones about.
	private static readonly (string Old, string New)[] Renames =
	{
		( ".essmdl_c", ".mdl_c" ),
		( ".essmdl", ".mdl" ),
		( ".essmat", ".mat" ),
		( ".esstex_c", ".tex_c" ),
		( ".esstex", ".tex" ),
	};

	//Text files that may name an asset by path.
	private static readonly string[] TextExtensions =
	{
		".scene", ".prefab", ".mdl", ".mat", ".tex", Project.Extension, ".json",
	};

	[ConCmd( "core_migrate_project" )]
	internal static void Run()
	{
		var root = Project.Current?.GetAssetsPath();

		if ( string.IsNullOrEmpty( root ) || !System.IO.Directory.Exists( root ) )
		{
			Log.Warning( "no project to migrate" );
			return;
		}

		var files = System.IO.Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories )
			.Where( f => Renames.Any( r => f.EndsWith( r.Old, System.StringComparison.OrdinalIgnoreCase ) ) )
			.ToList();

		//Renames first, longest extension first so .essmdl_c is not caught by the .essmdl rule and left as a strange half-name.
		var renamed = 0;

		foreach ( var file in files )
		{
			var rule = Renames.First( r => file.EndsWith( r.Old, System.StringComparison.OrdinalIgnoreCase ) );
			var target = file[..^rule.Old.Length] + rule.New;

			if ( File.Exists( target ) ) File.Delete( target );

			File.Move( file, target );
			renamed++;
		}

		//Then every reference inside the project's text. The compiled model binaries also carry material
		//names, which this cannot reach - the renderer follows those to the renamed files itself.
		var rewritten = 0;

		foreach ( var file in System.IO.Directory.EnumerateFiles( root, "*", SearchOption.AllDirectories ) )
		{
			if ( !TextExtensions.Any( e => file.EndsWith( e, System.StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			string text;

			try
			{
				text = File.ReadAllText( file );
			}
			catch ( IOException )
			{
				continue;
			}

			var updated = text;

			foreach ( var (old, replacement) in Renames )
			{
				updated = updated.Replace( old, replacement );
			}

			if ( ReferenceEquals( updated, text ) || updated == text ) continue;

			File.WriteAllText( file, updated );
			rewritten++;
		}

		Log.Info( $"migrated: {renamed} files renamed, {rewritten} files updated. Restart the editor so everything rescans." );
	}
}
