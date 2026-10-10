using System;
using System.Linq;
using System.Threading.Tasks;

namespace Rinten;

public static class Launcher
{
	public static int Main()
	{
		new CoreServerAppSystem().Run();

		return 0;
	}
}

//A dedicated server on our own core. The same start as a game's, minus the window and plus the console: a server is a frame
//loop with nobody looking at it. What it runs is decided by the commands typed at it, or by the command line - game <ident> .
public class CoreServerAppSystem : CoreAppSystem
{
	private DedicatedServerConsole console;

	protected override bool Dedicated => true;

	public override void Init()
	{
		base.Init();

		if ( !Environment.CommandLine.Contains( "game " ) )
		{
			DedicatedServerHelp.Print();
		}

		//Redirecting console output (docker/wine/etc..) won't support this overlay, so don't bother with it
		if ( !Console.IsOutputRedirected )
		{
			console = new DedicatedServerConsole();
			console.Update();
		}
	}

	protected override bool RunFrame()
	{
		console?.Update();

		return base.RunFrame();
	}
}

//What a server says when it comes up with nothing to run.
public static class DedicatedServerHelp
{
	public static void Print()
	{
		var Log = new Diagnostics.Logger( "Console" );

		Log.Info( "Welcome to the Rinten dedicated server." );
		Log.Info( "Submit any issues to the project issue tracker" );
		Log.Info( "" );
		Log.Info( "game <gameident> - load a game" );
		Log.Info( "game <gameident> <mapident> - load a game with a map" );
		Log.Info( "find <text> - find a concommand or convar" );
		Log.Info( "kick <id> - kick a player by name or id" );
		Log.Info( "status - current game status" );
		Log.Info( "quit - quit" );
		Log.Info( "" );
	}
}
