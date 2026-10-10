using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>Every version of a resource, in the order it was edited.</summary>
public static class ResourceHistory
{
	//Opened and closed around the write rather than held: an editor keeps a scene open and edits it all day, and it opens a hundred resources for a moment each. A file handle per resource looked at is a handle per resource left open.
	/// <summary>Writes a version of a resource into its history.</summary>
	public static void Record( string sourceFile, string name, string before, string after )
	{
		if ( string.IsNullOrEmpty( sourceFile ) ) return;
		if ( string.IsNullOrEmpty( after ) ) return;
		if ( before == after ) return;

		var path = PathFor( sourceFile );

		if ( string.IsNullOrEmpty( path ) ) return;

		using var journal = EditJournal.Open( path, 0 );

		if ( journal is null ) return;

		var record = new JsonObject
		{
			["name"] = name ?? "Edit",
			["after"] = after,
			["afterHash"] = Hash( after ).ToString(),
		};

		if ( before is not null )
		{
			record["before"] = before;
			record["beforeHash"] = Hash( before ).ToString();
		}

		journal.Append( record.ToJsonString() );
	}

	/// <summary>Previous version, only if the file on disk is still the history's latest</summary>
	public static bool TryStepBack( string sourceFile, out string document, out string name )
	{
		document = null;
		name = null;

		if ( string.IsNullOrEmpty( sourceFile ) || !File.Exists( sourceFile ) ) return false;

		var path = PathFor( sourceFile );

		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) ) return false;

		using var journal = EditJournal.Open( path, 0 );

		if ( journal is null ) return false;

		var records = journal.Read();

		if ( records.Count == 0 ) return false;

		var current = Hash( File.ReadAllText( sourceFile ) );

		for ( var i = records.Count - 1; i >= 0; i-- )
		{
			if ( Read( records[i] ) is not JsonObject record ) continue;

			var after = record["afterHash"]?.ToString();

			//The last version this history knows about has to be the one on the disk. Anything else and the two have parted company.
			if ( after != current.ToString() ) return false;

			var before = record["before"]?.ToString();

			if ( before is null ) return false;

			document = before;
			name = record["name"]?.ToString() ?? "Edit";

			//The step back is itself a version, so the history stays a straight line of what the file has been - the same way the scene's does.
			journal.Append( new JsonObject
			{
				["name"] = $"Undo {name}",
				["after"] = before,
				["afterHash"] = Hash( before ).ToString(),
				["before"] = record["after"]?.ToString(),
				["beforeHash"] = after,
			}.ToJsonString() );

			return true;
		}

		return false;
	}

	/// <summary>How many versions of a resource are remembered.</summary>
	public static int Count( string sourceFile )
	{
		var path = PathFor( sourceFile );

		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) ) return 0;

		using var journal = EditJournal.Open( path, 0 );

		return journal?.Count ?? 0;
	}

	//Where histories are kept, when it is not the project they belong to. For tests. The rule this class exists to enforce - that a file changed by
	//something else is not stepped back over - is the one worth proving, and proving it needs somewhere to write that is not a project nobody opened.
	internal static string RootOverride { get; set; }

	//Where a resource's history lives: beside the project, not beside the resource, and named after the file it belongs to.
	private static string PathFor( string sourceFile )
	{
		var root = RootOverride ?? Project.Current?.GetRootPath();

		if ( string.IsNullOrEmpty( root ) ) return null;

		var name = Path.GetFileName( sourceFile );
		var id = (uint)sourceFile.ToLowerInvariant().NormalizeFilename( false ).FastHash();

		return Path.Combine( root, ".rinten", "history", $"{name}.{id:x}.journal" );
	}

	private static JsonNode Read( byte[] bytes )
	{
		try
		{
			return JsonNode.Parse( bytes );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	private static uint Hash( string document ) => (uint)(document ?? string.Empty).FastHash();
}
