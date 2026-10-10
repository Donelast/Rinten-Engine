namespace Rinten;

//Declares the frame rate cap console variables RenderSettings reads and writes by name
internal static class FrameRateConVars
{
	[ConVar( "fps_max", ConVarFlags.Protected | ConVarFlags.Saved,
		Help = "Most frames a second the engine may run, 0 for as fast as it can" )]
	public static int FpsMax { get; set; } = 144;

	[ConVar( "fps_max_inactive", ConVarFlags.Protected | ConVarFlags.Saved,
		Help = "The cap while the window is in the background - a game nobody is looking at is drawn slowly on purpose" )]
	public static int FpsMaxInactive { get; set; } = 60;

	[ConVar( "fps_max_menu", ConVarFlags.Protected | ConVarFlags.Saved,
		Help = "The cap while the main menu is what is on the screen" )]
	public static int FpsMaxMenu { get; set; } = 144;
}
