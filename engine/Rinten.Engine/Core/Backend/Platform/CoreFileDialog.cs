using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Rinten;

//Native file dialogs through SDL3; calls block until the user answers
internal static unsafe partial class CoreFileDialog
{
	private const string SDL = "SDL3";

	[StructLayout( LayoutKind.Sequential )]
	private struct SDL_DialogFileFilter
	{
		public IntPtr name;
		public IntPtr pattern;
	}

	private delegate void Callback( IntPtr userdata, IntPtr files, int filter );

	//SDL_FileDialogType.
	private enum DialogType
	{
		OpenFile = 0,
		SaveFile = 1,
		OpenFolder = 2,
	}

	[LibraryImport( SDL, EntryPoint = "SDL_ShowFileDialogWithProperties" )]
	private static partial void SDL_ShowFileDialogWithProperties( DialogType type, IntPtr callback, IntPtr userdata, uint props );

	[LibraryImport( SDL, EntryPoint = "SDL_CreateProperties" )]
	private static partial uint SDL_CreateProperties();

	[LibraryImport( SDL, EntryPoint = "SDL_DestroyProperties" )]
	private static partial void SDL_DestroyProperties( uint props );

	[LibraryImport( SDL, EntryPoint = "SDL_SetStringProperty", StringMarshalling = StringMarshalling.Utf8 )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_SetStringProperty( uint props, string name, string value );

	[LibraryImport( SDL, EntryPoint = "SDL_SetPointerProperty", StringMarshalling = StringMarshalling.Utf8 )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_SetPointerProperty( uint props, string name, IntPtr value );

	[LibraryImport( SDL, EntryPoint = "SDL_SetNumberProperty", StringMarshalling = StringMarshalling.Utf8 )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_SetNumberProperty( uint props, string name, long value );

	[LibraryImport( SDL, EntryPoint = "SDL_SetBooleanProperty", StringMarshalling = StringMarshalling.Utf8 )]
	[return: MarshalAs( UnmanagedType.U1 )]
	private static partial bool SDL_SetBooleanProperty( uint props, string name, [MarshalAs( UnmanagedType.U1 )] bool value );

	[LibraryImport( SDL, EntryPoint = "SDL_PumpEvents" )]
	private static partial void SDL_PumpEvents();

	/// <summary>Files chosen to open, or null where the box was dismissed. The location is the folder to start in, or a file in it to have chosen already.</summary>
	public static List<string> Open( string title, string location, string filterName, string filterPattern, bool many )
		=> Show( DialogType.OpenFile, title, location, filterName, filterPattern, many );

	/// <summary>A name to save under, or null. The location is the folder to start in - or a file in it, whose name the box then offers to save under, which is what a "save as" wants.</summary>
	public static List<string> Save( string title, string location, string filterName, string filterPattern )
		=> Show( DialogType.SaveFile, title, location, filterName, filterPattern, false );

	/// <summary>A folder, or null.</summary>
	public static List<string> Folder( string title, string location )
		=> Show( DialogType.OpenFolder, title, location, null, null, false );

	//Shows a dialog via SDL's properties API (for title and file name) and pumps events until it answers
	private static List<string> Show( DialogType type, string title, string location, string filterName, string filterPattern, bool many )
	{
		List<string> answer = null;
		var finished = false;

		Callback got = ( _, files, _ ) =>
		{
			finished = true;

			if ( files == IntPtr.Zero ) return;

			var chosen = new List<string>();

			//A null-terminated list of strings, as C hands them over.
			for ( var i = 0; ; i++ )
			{
				var pointer = Marshal.ReadIntPtr( files, i * IntPtr.Size );

				if ( pointer == IntPtr.Zero ) break;

				var path = Marshal.PtrToStringUTF8( pointer );

				if ( !string.IsNullOrEmpty( path ) ) chosen.Add( path );
			}

			if ( chosen.Count > 0 ) answer = chosen;
		};

		var handle = GCHandle.Alloc( got );

		var namePtr = IntPtr.Zero;
		var patternPtr = IntPtr.Zero;
		var props = SDL_CreateProperties();

		try
		{
			var filters = stackalloc SDL_DialogFileFilter[1];
			var count = 0;

			if ( !string.IsNullOrEmpty( filterName ) && !string.IsNullOrEmpty( filterPattern ) )
			{
				namePtr = Marshal.StringToCoTaskMemUTF8( filterName );
				patternPtr = Marshal.StringToCoTaskMemUTF8( filterPattern );

				filters[0] = new SDL_DialogFileFilter { name = namePtr, pattern = patternPtr };
				count = 1;
			}

			//Kept until the box has answered, below: SDL reads the filters then, not now.
			if ( count > 0 )
			{
				SDL_SetPointerProperty( props, "SDL.filedialog.filters", (IntPtr)filters );
				SDL_SetNumberProperty( props, "SDL.filedialog.nfilters", count );
			}

			if ( !string.IsNullOrEmpty( title ) ) SDL_SetStringProperty( props, "SDL.filedialog.title", title );
			if ( !string.IsNullOrEmpty( location ) ) SDL_SetStringProperty( props, "SDL.filedialog.location", location );
			if ( many ) SDL_SetBooleanProperty( props, "SDL.filedialog.many", true );

			SDL_ShowFileDialogWithProperties( type, Marshal.GetFunctionPointerForDelegate( got ), IntPtr.Zero, props );

			//Until the box is answered. Two minutes is longer than anybody spends looking for a file,
			//and a wait with no end at all would hang the editor if the platform never called back.
			var waited = 0;

			while ( !finished && waited < 120_000 )
			{
				SDL_PumpEvents();

				Thread.Sleep( 10 );
				waited += 10;
			}
		}
		finally
		{
			handle.Free();

			if ( namePtr != IntPtr.Zero ) Marshal.FreeCoTaskMem( namePtr );
			if ( patternPtr != IntPtr.Zero ) Marshal.FreeCoTaskMem( patternPtr );
			if ( props != 0 ) SDL_DestroyProperties( props );
		}

		return answer;
	}
}
