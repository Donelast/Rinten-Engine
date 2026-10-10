using Rinten.Utility;
using System.IO;
using System.Reflection;

namespace Rinten.Network;

/// <summary>The providers there are, and the one place that finds them.</summary>
public static class NetworkProviders
{
	private static readonly Logger log = new( "NetProvider" );

	private static readonly List<NetworkProvider> all = new();

	/// <summary>Every provider that was loaded, available or not.</summary>
	public static IReadOnlyList<NetworkProvider> All => all;

	/// <summary>The provider that answers to a scheme, if one does and it can be used.</summary>
	public static NetworkProvider Find( string scheme )
	{
		if ( string.IsNullOrWhiteSpace( scheme ) ) return null;

		return all.FirstOrDefault( x => x.IsAvailable && string.Equals( x.Scheme, scheme, StringComparison.OrdinalIgnoreCase ) );
	}

	//The provider a target like steam://123 names, and what follows the scheme.
	internal static bool TryResolve( string target, out NetworkProvider provider, out string rest )
	{
		provider = null;
		rest = null;

		if ( string.IsNullOrWhiteSpace( target ) ) return false;

		var at = target.IndexOf( "://", StringComparison.Ordinal );
		if ( at <= 0 ) return false;

		provider = Find( target[..at] );
		rest = target[(at + 3)..];

		return provider is not null;
	}

	private static readonly Dictionary<string, string> presence = new();

	//What the player is doing, told to every available provider - and remembered, so one that becomes available later says the same.
	internal static void SetRichPresence( string key, string value )
	{
		if ( string.IsNullOrWhiteSpace( key ) ) return;

		if ( value is null ) presence.Remove( key );
		else presence[key] = value;

		foreach ( var provider in all )
		{
			if ( !provider.IsAvailable ) continue;

			try
			{
				provider.SetRichPresence( key, value );
			}
			catch ( Exception e )
			{
				log.Warning( e, $"{provider.Title}: {e.Message}" );
			}
		}
	}

	internal static void ClearRichPresence()
	{
		presence.Clear();

		foreach ( var provider in all )
		{
			if ( !provider.IsAvailable ) continue;

			try
			{
				provider.ClearRichPresence();
			}
			catch ( Exception e )
			{
				log.Warning( e, $"{provider.Title}: {e.Message}" );
			}
		}
	}

	/// <summary>Make a provider known. A module does this from its own startup.</summary>
	public static void Register( NetworkProvider provider )
	{
		if ( provider is null || all.Contains( provider ) ) return;

		all.Add( provider );

		//Only one that is there: a provider that is not - Steam without its library - is the ordinary case and was a line on every start.
		if ( provider.IsAvailable )
			log.Info( $"{provider.Title} ({provider.Scheme}://) is available" );

		LocalUser.Adopt( provider );

		if ( provider.IsAvailable )
		{
			foreach ( var (key, value) in presence )
				provider.SetRichPresence( key, value );
		}
	}

	//Loads each net/x/x.dll module; skipped on web, and load failures are only warnings
	internal static void LoadAssemblies()
	{
		if ( Application.OnWeb ) return;
		if ( !Directory.Exists( "net" ) ) return;

		foreach ( var folder in Directory.EnumerateDirectories( "net" ) )
		{
			var name = Path.GetFileName( folder );
			var file = Path.GetFullPath( Path.Combine( folder, name + ".dll" ) );

			if ( !File.Exists( file ) ) continue;

			try
			{
				var assembly = Assembly.LoadFile( file );

				foreach ( var type in assembly.GetTypes() )
				{
					if ( type.IsAbstract || !typeof( NetworkProvider ).IsAssignableFrom( type ) ) continue;
					if ( type.GetConstructor( Type.EmptyTypes ) is null ) continue;

					Register( (NetworkProvider)Activator.CreateInstance( type ) );
				}
			}
			catch ( Exception e )
			{
				log.Warning( e, $"Couldn't load network module {file}: {e.Message}" );
			}
		}
	}

	internal static void Tick()
	{
		foreach ( var provider in all )
		{
			if ( !provider.IsAvailable ) continue;

			try
			{
				provider.Tick();
			}
			catch ( Exception e )
			{
				log.Warning( e, $"{provider.Title}: {e.Message}" );
			}
		}
	}

	internal static void Shutdown()
	{
		foreach ( var provider in all )
		{
			try
			{
				provider.Shutdown();
			}
			catch ( Exception e )
			{
				log.Warning( e, $"{provider.Title}: {e.Message}" );
			}
		}
	}

	internal static void RequestJoin( NetworkProvider provider, string target )
	{
		var full = $"{provider.Scheme}://{target}";

		log.Info( $"asked to join {full}" );

		//Where the game's own code gets to say no or wait is not here: a join from outside is the same as the player typing the target in.
		Networking.Connect( full );
	}
}
