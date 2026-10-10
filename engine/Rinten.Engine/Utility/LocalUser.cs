namespace Rinten.Utility;

//Who the local user is: the id this process goes by on the network, and its name.
internal static class LocalUser
{
	const string IdFile = "localuser.txt";

	static SteamId? _id;

	/// <summary>The local user's id. Each install draws a random number once and keeps it in the config folder, and each local instance (the editor's -joinlocal clients) adds its instance id to it, so two machines, or two instances on one machine, never share an id.</summary>
	public static SteamId SteamId => _id ??= new SteamId( InstallId() + (ulong)Math.Max( 0, Application.LocalInstanceId ) );

	/// <summary>The local user's name.</summary>
	public static string PersonaName { get; private set; } = "Unnamed Player";

	//Adopt a platform's signed-in identity and name; first available provider wins, else keep install id
	internal static void Adopt( Rinten.Network.NetworkProvider provider )
	{
		if ( !provider.IsAvailable ) return;

		if ( provider.LocalId is { } id && !_adopted )
		{
			//Instances of one machine still have to differ, as they do with the made-up id.
			_id = new SteamId( id.ValueUnsigned + (ulong)Math.Max( 0, Application.LocalInstanceId ) );
			_adopted = true;
		}

		if ( !string.IsNullOrWhiteSpace( provider.LocalName ) && !_adopted_name )
		{
			PersonaName = provider.LocalName;
			_adopted_name = true;
		}
	}

	static bool _adopted;
	static bool _adopted_name;

	static ulong InstallId()
	{
		var fs = EngineFileSystem.Config;

		try
		{
			if ( fs is not null && fs.FileExists( IdFile ) && ulong.TryParse( fs.ReadAllText( IdFile ).Trim(), out var saved ) && saved != 0 )
				return saved;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't read {IdFile}: {e.Message}" );
		}

		//The low bits are left clear so adding an instance id can't wrap round to zero
		var id = (ulong)Random.Shared.NextInt64( 1, long.MaxValue ) & ~0xFFFFUL;
		if ( id == 0 ) id = 0x10000;

		try
		{
			fs?.WriteAllText( IdFile, id.ToString() );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't save {IdFile}: {e.Message}" );
		}

		return id;
	}

	private static readonly string[] AnonymousNames =
	[
		"Homer", "Marge", "Bart", "Lisa", "Maggie",
		"Ned", "Burns", "Smithers", "Moe", "Barney",
		"Krusty", "Milhouse", "Nelson", "Ralph", "Wiggum"
	];

	//An alias for a player id, shown in place of names in streamer mode. The same id always gets the same name.
	internal static string GetAnonymousName( SteamId steamId )
	{
		var index = (int)(steamId.ValueUnsigned % (ulong)AnonymousNames.Length);
		return AnonymousNames[index];
	}
}
