namespace Rinten.Build;

internal static class Constants
{
	internal enum Solutions
	{
		Engine,
		Toolbase,
		Menu,
		BuildTools,
		All,
	}

	internal enum ExitCode
	{
		Success = 0,
		Failure = 1,
	}

	internal static string GetSolutionDir( Solutions solution )
	{
		switch ( solution )
		{
			case Solutions.Engine:
				return "engine/";
			case Solutions.Toolbase:
				return "game/addons/tools/";
			case Solutions.Menu:
				return "game/addons/menu/";
			case Solutions.BuildTools:
				return "engine/Tools/";
			default:
				throw new ArgumentOutOfRangeException( nameof( solution ), solution, null );
		}
	}
}
