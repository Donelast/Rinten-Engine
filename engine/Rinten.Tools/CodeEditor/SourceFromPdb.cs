using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace Editor;

/// <summary>Where a type in a built assembly was written, read out of the debug file next to it.</summary>
public static class SourceFromPdb
{
	static readonly Dictionary<Type, (string File, int Line)> known = new();
	static readonly Dictionary<Assembly, MetadataReaderProvider> readers = new();

	/// <summary>Where this type was written, if a debug file beside its assembly says.</summary>
	public static bool TryGet( Type type, out string file, out int line )
	{
		file = null;
		line = 0;

		//Nothing compiled here is asked about, and nothing compiled here is remembered: those types die
		//on the next hotload, and a cache holding them keeps the assembly they came from alive with them.
		if ( type is null || type.Assembly.IsDynamic )
			return false;

		if ( !known.TryGetValue( type, out var found ) )
		{
			found = Read( type );
			known[type] = found;
		}

		file = found.File;
		line = found.Line;

		return file is not null;
	}

	static (string File, int Line) Read( Type type )
	{
		var reader = ReaderFor( type.Assembly );

		if ( reader is null )
			return default;

		//Where the type starts in each file it is spread over, and how much of it is in that one.
		var starts = new Dictionary<string, (int Line, int Points)>();

		foreach ( var method in Methods( type ) )
		{
			var row = method.MetadataToken & 0xFFFFFF;

			if ( row <= 0 || row > reader.MethodDebugInformation.Count )
				continue;

			try
			{
				var information = reader.GetMethodDebugInformation( MetadataTokens.MethodDebugInformationHandle( row ) );

				foreach ( var point in information.GetSequencePoints() )
				{
					if ( point.IsHidden )
						continue;

					var document = reader.GetString( reader.GetDocument( point.Document ).Name );
					var seen = starts.TryGetValue( document, out var at ) ? at : (Line: point.StartLine, Points: 0);

					starts[document] = (Math.Min( seen.Line, point.StartLine ), seen.Points + 1);
				}
			}
			catch ( BadImageFormatException )
			{
				continue;
			}
		}

		if ( starts.Count == 0 )
			return default;

		//A partial class is in several files; the one most of it is in is the one to open.
		var best = starts.OrderByDescending( x => x.Value.Points ).First();
		var path = Localise( best.Key );

		return path is null ? default : (path, best.Value.Line);
	}

	static IEnumerable<MethodBase> Methods( Type type )
	{
		const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

		foreach ( var method in type.GetMethods( all ) )
			yield return method;

		foreach ( var constructor in type.GetConstructors( all ) )
			yield return constructor;
	}

	static MetadataReader ReaderFor( Assembly assembly )
	{
		if ( !readers.TryGetValue( assembly, out var provider ) )
		{
			provider = Open( assembly );
			readers[assembly] = provider;
		}

		try
		{
			return provider?.GetMetadataReader();
		}
		catch ( Exception )
		{
			return null;
		}
	}

	//The debug file, read into memory rather than held open - a build has to be able to overwrite it.
	static MetadataReaderProvider Open( Assembly assembly )
	{
		if ( assembly.IsDynamic || string.IsNullOrEmpty( assembly.Location ) )
			return null;

		var path = Path.ChangeExtension( assembly.Location, ".pdb" );

		if ( !File.Exists( path ) )
			return null;

		try
		{
			return MetadataReaderProvider.FromPortablePdbImage( ImmutableArray.Create( File.ReadAllBytes( path ) ) );
		}
		catch ( Exception )
		{
			return null;
		}
	}

	//The same file on this machine: a debug file records the path the machine that built it
	//used, and an assembly downloaded with a build was compiled somewhere else entirely.
	static string Localise( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return null;

		if ( File.Exists( path ) )
			return path;

		//The editor runs in game/, so the repository is one above it.
		if ( Directory.GetParent( Environment.CurrentDirectory )?.FullName is not { } root )
			return null;

		foreach ( var folder in new[] { "engine", "game" } )
		{
			foreach ( var separator in new[] { '\\', '/' } )
			{
				var at = path.IndexOf( $"{separator}{folder}{separator}", StringComparison.OrdinalIgnoreCase );

				if ( at < 0 )
					continue;

				var tail = path[(at + 1)..].Replace( '\\', Path.DirectorySeparatorChar ).Replace( '/', Path.DirectorySeparatorChar );
				var candidate = Path.Combine( root, tail );

				if ( File.Exists( candidate ) )
					return candidate;
			}
		}

		return null;
	}
}
