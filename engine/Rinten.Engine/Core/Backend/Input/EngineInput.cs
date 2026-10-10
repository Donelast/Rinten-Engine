using NativeEngine;

namespace Rinten;

//Single entry point for input: the native systems when loaded, otherwise CoreInput on SDL3
internal static class EngineInput
{
	//Whether the pointer is over one of our windows.
	internal static bool HasMouseFocus()
		=> CoreInput.HasMouseFocus();

	//Whether the application has the window system's attention.
	internal static bool IsAppActive()
		=> CoreInput.IsAppActive();

	internal static void SetRelativeMouseMode( bool state )
	{
		CoreInput.SetRelativeMouseMode( state );
	}

	internal static bool GetRelativeMouseMode()
		=> CoreInput.GetRelativeMouseMode();

	internal static void SetCursorPosition( int x, int y )
	{
		CoreInput.SetCursorPosition( x, y );
	}

	internal static void SetIMEAllowed( bool allowed )
	{
		CoreInput.SetIMEAllowed( allowed );
	}

	internal static void SetIMETextLocation( int x, int y, int width, int height )
	{
		CoreInput.SetIMETextLocation( x, y, width, height );
	}

	internal static void SetCursorStandard( InputStandardCursor_t cursor )
	{
		CoreInput.SetCursorStandard( cursor );
	}

	//Shows a cursor the game loaded from a file of its own. Only the native runtime can load one - see
	//LoadCursorFromFile. On our own core nothing ever gets into the set of user cursors, so nothing ever asks for one.
	internal static void SetCursorUser( string name )
	{
	}

	//Custom cursor images are not supported on our core yet; false keeps the standard cursors
	internal static bool LoadCursorFromFile( string filename, string name, int hotX, int hotY )
		=> false;

	internal static void ShutdownUserCursors()
	{
	}

	internal static string CodeToString( ButtonCode code )
		=> CoreInput.CodeToString( code );

	internal static ButtonCode StringToButtonCode( string name )
		=> CoreInput.StringToButtonCode( name );

	internal static string GetKeyDisplayName( ButtonCode code )
		=> CoreInput.GetKeyDisplayName( code );

	internal static int ButtonCodeToVirtualKey( ButtonCode code )
		=> CoreInput.ButtonCodeToVirtualKey( code );

	//What the console command bound to a key is, for the function keys the engine runs commands for. Their bindings live in their input service and
	//come from their config. Ours are the game's own binds, which are read elsewhere and never console commands, so this is nothing without them.
	internal static string GetBinding( ButtonCode button )
		=> null;

	//Reads what the window system has waiting. On our own core the window does this as part of its
	//own pump, because it is the same queue and reading it twice would throw half the events away.
	internal static void Pump()
	{
	}
}
