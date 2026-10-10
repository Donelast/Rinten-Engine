using Microsoft.Win32;
using System;
using System.Runtime.InteropServices;

namespace Editor;

internal class FileAssociations
{
	//The name the registry knows a project file by.
	private const string ProgId = "Rinten.ProjectFile";

	//Creates or updates project file associations and removes the old .sbproj one
	internal static void Create()
	{
		//Windows registry and shell32 only; skip elsewhere instead of logging a warning every launch
		if ( !OperatingSystem.IsWindows() ) return;

		var exeFilePath = $"\"{FileSystem.Root.GetFullPath( EngineExecutables.EditorFile )}\"";

		try
		{
			RegistryKey fileTypeKey = Registry.CurrentUser.CreateSubKey( $@"SOFTWARE\Classes\{ProgId}" );
			fileTypeKey.SetValue( "", "Rinten Project File" );
			fileTypeKey.CreateSubKey( "DefaultIcon" ).SetValue( "", exeFilePath );

			RegistryKey shellKey = fileTypeKey.CreateSubKey( "shell" );
			RegistryKey shellOpenKey = shellKey.CreateSubKey( "open" );
			shellOpenKey.SetValue( "", "Open" );
			shellOpenKey.CreateSubKey( "command" ).SetValue( "", exeFilePath + " -project \"%1\"" );

			RegistryKey fileExtensionKey = Registry.CurrentUser.CreateSubKey( $@"SOFTWARE\Classes\{Project.Extension}" );
			fileExtensionKey.SetValue( "", ProgId );

			Forget( ".sbproj" );
			Forget( "Rinten.ProjectFile" );

			//Tell explorer the file association has been changed
			SHChangeNotify( 0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero );
		}
		catch ( Exception ex )
		{
			Log.Warning( ex, "Failed to associate project file extension" );
		}
	}

	//Takes one of ours back out of the registry, where it is there.
	private static void Forget( string name )
	{
		try
		{
			Registry.CurrentUser.DeleteSubKeyTree( $@"SOFTWARE\Classes\{name}", throwOnMissingSubKey: false );
		}
		catch ( Exception )
		{
			//Somebody else's now, or not ours to remove. Either way not worth a word.
		}
	}

	[DllImport( "shell32.dll", CharSet = CharSet.Auto, SetLastError = true )]
	public static extern void SHChangeNotify( uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2 );
}
