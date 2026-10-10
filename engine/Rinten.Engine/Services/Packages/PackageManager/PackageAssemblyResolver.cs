using Mono.Cecil;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;

namespace Rinten;

/// <summary>Finds the Cecil definition of an assembly by name, for hotload.</summary>
[SkipHotload]
public sealed class PackageAssemblyResolver : IAssemblyResolver
{
	//Definitions of the assemblies that are loaded, newest version of each.
	private ConcurrentDictionary<AssemblyNameReference, AssemblyDefinition> assemblies = new( AssemblyNameComparer.Instance );

	//Raw bytes of registered package assemblies; definitions are built from them only during hotload
	private ConcurrentDictionary<AssemblyNameReference, byte[]> registered = new( AssemblyNameComparer.Instance );

	//The framework and engine assemblies, read from disk once for the process.
	private static readonly ConcurrentDictionary<AssemblyNameReference, AssemblyDefinition> onDisk = new( AssemblyNameComparer.Instance );

	private readonly object packageLock = new();

	/// <summary>Called when a <c>package.*</c> assembly isn't already known. Return the raw DLL bytes to have a definition built from them, or null to let the resolve fail.</summary>
	public Func<string, byte[]> PackageBytes { get; set; }

	public void Dispose()
	{
		if ( assemblies is null ) return;

		foreach ( var assembly in assemblies.Values )
			assembly.Dispose();

		assemblies = null;
		registered = null;
	}

	/// <summary>Lets go of every package definition made since the last time - after a hotload, which is the only thing that reads them. The bytes stay, so the next one can make them again.</summary>
	public void ReleaseDefinitions()
	{
		if ( assemblies is null ) return;

		lock ( packageLock )
		{
			foreach ( var name in assemblies.Keys.ToArray() )
			{
				if ( assemblies.TryRemove( name, out var assembly ) )
					assembly.Dispose();
			}
		}
	}

	/// <summary>Remember an assembly that has just been loaded, so anything referencing it resolves without going to disk for it - which for a package would fail, there being no file.</summary>
	public void Register( byte[] dll )
	{
		if ( dll is null || assemblies is null ) return;

		//Its name and version out of the metadata table, which is all registering needs - see registered.
		using var pe = new System.Reflection.PortableExecutable.PEReader( new MemoryStream( dll ) );
		var metadata = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader( pe );
		var definition = metadata.GetAssemblyDefinition();
		var name = new AssemblyNameReference( metadata.GetString( definition.Name ), definition.Version );

		lock ( packageLock )
		{
			ForgetOlderVersions( name );

			//The same name and version again is a rebuild: whatever was made from the old bytes is not what these say.
			if ( assemblies.TryRemove( name, out var stale ) ) stale.Dispose();

			registered[name] = dll;
		}
	}

	/// <summary>Forget every version of the named assembly older than this one.</summary>
	public void ForgetOlderVersions( AssemblyName name )
		=> ForgetOlderVersions( new AssemblyNameReference( name.Name, name.Version ) );

	/// <summary>Forget every version of the named assembly older than this one.</summary>
	public void ForgetOlderVersions( AssemblyNameReference name )
	{
		if ( assemblies is null ) return;

		var older = assemblies
			.Where( x => x.Key.Name.Equals( name.Name, StringComparison.OrdinalIgnoreCase ) )
			.Where( x => x.Key.Version.CompareTo( name.Version ) < 0 )
			.ToArray();

		foreach ( var match in older )
		{
			if ( assemblies.Remove( match.Key, out var assembly ) )
				assembly.Dispose();
		}

		foreach ( var key in registered.Keys.ToArray() )
		{
			if ( key.Name.Equals( name.Name, StringComparison.OrdinalIgnoreCase ) && key.Version.CompareTo( name.Version ) < 0 )
				registered.TryRemove( key, out _ );
		}
	}

	public AssemblyDefinition Resolve( AssemblyNameReference name, ReaderParameters parameters ) => Resolve( name );

	public AssemblyDefinition Resolve( AssemblyNameReference name )
	{
		if ( TryCached( name, out var cached ) )
			return cached;

		//A registered package, made from its bytes now that somebody wants it. Without its symbols: hotload reads names and bodies, never lines.
		if ( registered is not null && FindRegistered( name, out var key, out var dll ) )
		{
			lock ( packageLock )
			{
				if ( assemblies.TryGetValue( key, out var built ) )
					return built;

				var options = new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = this };
				var assembly = AssemblyDefinition.ReadAssembly( new MemoryStream( dll ), options );

				assemblies[key] = assembly;
				return assembly;
			}
		}

		//A package assembly has no file to be found by, so it is asked for. Serialised: Cecil reads are not thread safe and this
		//runs under hotload's parallel walk, so without the lock two threads could build separate definitions of the same assembly.
		if ( name.Name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) && PackageBytes is not null )
		{
			lock ( packageLock )
			{
				//Another thread may have built it while we waited
				if ( TryCached( name, out var built ) )
					return built;

				if ( PackageBytes( name.Name ) is { } bytes )
				{
					var stream = new MemoryStream( bytes );
					var options = new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = this };
					var assembly = AssemblyDefinition.ReadAssembly( stream, options );

					assemblies[assembly.Name] = assembly;
					return assembly;
				}
			}
		}

		//Non-package assemblies are files on disk; FromDisk reports whether the file exists
		if ( name.Name.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) )
			throw NotResolved( name );

		if ( onDisk.TryGetValue( name, out var known ) )
			return known;

		lock ( onDisk )
		{
			return onDisk.GetOrAdd( name, FromDisk );
		}
	}

	//The registered bytes for this name - this version, or the newest of the name, as TryCached picks.
	private bool FindRegistered( AssemblyNameReference name, out AssemblyNameReference key, out byte[] dll )
	{
		if ( registered.TryGetValue( name, out dll ) )
		{
			key = registered.Keys.First( x => AssemblyNameComparer.Instance.Equals( x, name ) );
			return true;
		}

		var newest = registered
			.Where( x => x.Key.Name.Equals( name.Name, StringComparison.OrdinalIgnoreCase ) )
			.OrderByDescending( x => x.Key.Version )
			.FirstOrDefault();

		key = newest.Key;
		dll = newest.Value;

		return dll is not null;
	}

	private bool TryCached( AssemblyNameReference name, out AssemblyDefinition assembly )
	{
		assembly = null;

		if ( assemblies is null )
			return false;

		if ( assemblies.TryGetValue( name, out assembly ) )
			return true;

		//The same name at a different version is what a hotloaded package is
		var newest = assemblies
			.Where( x => x.Key.Name.Equals( name.Name, StringComparison.OrdinalIgnoreCase ) )
			.OrderByDescending( x => x.Key.Version )
			.FirstOrDefault();

		if ( newest.Value is null )
			return false;

		assembly = newest.Value;
		return true;
	}

	private AssemblyDefinition FromDisk( AssemblyNameReference name )
	{
		var dll = $"{name.Name}.dll";

		var framework = System.IO.Path.GetDirectoryName( typeof( object ).Assembly.Location );
		var path = System.IO.Path.Combine( framework, dll );

		if ( !System.IO.File.Exists( path ) )
		{
			var beside = System.IO.Path.GetDirectoryName( GetType().Assembly.Location );
			path = System.IO.Path.Combine( beside, dll );
		}

		if ( !System.IO.File.Exists( path ) )
			throw NotResolved( name );

		var stream = new MemoryStream( System.IO.File.ReadAllBytes( path ) );
		var options = new ReaderParameters { ReadingMode = ReadingMode.Immediate, InMemory = true, AssemblyResolver = this };

		return AssemblyDefinition.ReadAssembly( stream, options );
	}

	private Exception NotResolved( AssemblyNameReference name )
	{
		var known = assemblies is null ? "" : string.Join( ";", assemblies.Keys.Select( x => x.ToString() ) );

		return new Exception( $"Couldn't resolve '{name}' [{known}]" );
	}

	private sealed class AssemblyNameComparer : IEqualityComparer<AssemblyNameReference>
	{
		public static AssemblyNameComparer Instance { get; } = new();

		public bool Equals( AssemblyNameReference x, AssemblyNameReference y )
		{
			if ( ReferenceEquals( x, y ) ) return true;
			if ( x is null || y is null ) return false;

			return string.Equals( x.Name, y.Name, StringComparison.OrdinalIgnoreCase ) && x.Version.Equals( y.Version );
		}

		public int GetHashCode( AssemblyNameReference obj )
		{
			var hash = new HashCode();
			hash.Add( obj.Name, StringComparer.OrdinalIgnoreCase );
			hash.Add( obj.Version );
			return hash.ToHashCode();
		}
	}
}
