using System.IO;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;

namespace Rinten.Internal;

//This isolates the dll so it can be unloaded and can have the same name as other loaded dlls
class IsolatedAssemblyContext : AssemblyLoadContext
{
	public LoadContext Parent;
	public Assembly Assembly;

	public IsolatedAssemblyContext() : base( "IsolatedAssemblyContext", true )
	{
	}

	protected override Assembly Load( AssemblyName assemblyName )
	{

		return Parent.LoadFromChild( assemblyName );
	}
}

class LoadContext : AssemblyLoadContext
{
	//If the assembly isn't found in this context, we'll load from the context used by the root Assembly.
	Assembly root;

	List<IsolatedAssemblyContext> Children = new();

	/// <summary>Called when an assembly can't be resolved through normal means (Assemblies, root context). Return null to fall through to the default probing.</summary>
	public Func<string, Assembly> OnDemandResolver { get; set; }

	public LoadContext( Assembly root = null ) : base( "TLC", true )
	{
		this.root = root;
	}

	public Assembly LoadFromChild( AssemblyName assemblyName )
	{
		foreach ( var child in Children )
		{
			var childAsmName = child.Assembly.GetName();

			if ( childAsmName.Name != assemblyName.Name )
			{
				continue;
			}

			return child.Assembly;
		}

		return Load( assemblyName );
	}

	protected override Assembly Load( AssemblyName assemblyName )
	{
		//library.log.Trace( $"Searching for {assemblyName}" );

		foreach ( var assembly in Assemblies )
		{
			if ( assembly.GetName().Name == assemblyName.Name )
			{
				return assembly;
			}
		}

		if ( root is not null )
		{
			var rootContext = GetLoadContext( root );
			var asm = rootContext?.LoadFromAssemblyName( assemblyName ) ?? default;

			if ( asm is not null )
			{
				return asm;
			}
		}

		if ( OnDemandResolver is not null )
		{
			var asm = OnDemandResolver( assemblyName.Name );
			if ( asm is not null )
				return asm;
		}

		return base.Load( assemblyName );
	}

	/// <summary>The assembly might have a pdb embedded inside. So load it with PEReader and have a look inside to see if it is in there. Then if it is, load the assembly with the pdb. I don't know why this isn't done by default.. but apparently it's not. So we have to do it manually.</summary>
	public Assembly LoadWithEmbeds( byte[] assemblyData, bool unloadOldVersions = true )
	{
		using var stream = new MemoryStream( assemblyData );

		byte[] pdbData = null;

		var lc = new IsolatedAssemblyContext();
		lc.Parent = this;

		//Open PEReader
		using ( var peReader = new PEReader( stream, PEStreamOptions.LeaveOpen ) )
		{
			var pdbEntry = peReader.ReadDebugDirectory().FirstOrDefault( x => x.Type == DebugDirectoryEntryType.EmbeddedPortablePdb );

			//At some point we should probably throw a fit if it doesn't have the pdb.. but we have a few
			//months of addons that do not have the pdb embedded, and it doesn't really matter that much.

			if ( pdbEntry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb )
			{
				unsafe
				{
					var pdbReader = peReader.ReadEmbeddedPortablePdbDebugDirectoryData( pdbEntry ).GetMetadataReader();
					pdbData = new ReadOnlySpan<byte>( pdbReader.MetadataPointer, pdbReader.MetadataLength ).ToArray();
				}
			}
		}

		stream.Position = 0;

		//We have a pdb - load using that!
		if ( pdbData != null )
		{
			using var pdbStream = new System.IO.MemoryStream( pdbData );
			lc.Assembly = lc.LoadFromStream( stream, pdbStream );
		}
		else
		{
			lc.Assembly = lc.LoadFromStream( stream );
		}

		if ( unloadOldVersions )
		{
			var asmName = lc.Assembly.GetName().Name;
			var matches = Children
				.Where( x => x.Assembly.GetName().Name == asmName )
				.ToArray();

			foreach ( var oldAssembly in matches )
			{
				Retire( oldAssembly );

				oldAssembly.Assembly = null;
				oldAssembly.Unload();

				Children.Remove( oldAssembly );
			}
		}

		Children.Add( lc );

		return lc.Assembly;
	}

	public void UnloadChild( Assembly asm )
	{
		var match = Children.FirstOrDefault( x => x.Assembly == asm );
		if ( match == null ) return;

		Retire( match );

		match.Assembly = null;
		match.Unload();

		Children.Remove( match );
	}

	//Unloaded contexts tracked weakly; one still alive later means a leaked reference kept it resident

	/// <summary>One context this process asked to unload, and whether it actually went.</summary>
	public readonly record struct RetiredContext( string Name, Version Version, bool Alive );

	private static readonly List<(string Name, Version Version, WeakReference Ref)> retired = new();

	/// <summary>How many contexts have ever been retired, alive or not.</summary>
	public static int RetiredCount
	{
		get { lock ( retired ) return retired.Count; }
	}

	/// <summary>How many retired contexts are still resident. Anything above zero after a collection is an assembly that was replaced but never freed.</summary>
	public static int LiveRetiredCount
	{
		get
		{
			lock ( retired )
			{
				var live = 0;

				foreach ( var entry in retired )
					if ( entry.Ref.IsAlive ) live++;

				return live;
			}
		}
	}

	/// <summary>Every retired context, oldest first.</summary>
	public static IReadOnlyList<RetiredContext> Retired()
	{
		lock ( retired )
		{
			var output = new List<RetiredContext>( retired.Count );

			foreach ( var entry in retired )
				output.Add( new RetiredContext( entry.Name, entry.Version, entry.Ref.IsAlive ) );

			return output;
		}
	}

	private static void Retire( IsolatedAssemblyContext child )
	{
		var name = child.Assembly?.GetName();
		if ( name is null ) return;

		lock ( retired )
		{
			retired.Add( (name.Name, name.Version, new WeakReference( child )) );
		}
	}
}
