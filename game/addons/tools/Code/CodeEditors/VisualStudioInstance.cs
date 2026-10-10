using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace Editor.CodeEditors;

//Opens files in a running Visual Studio via its DTE in the running object table, no vsopen.exe
[SupportedOSPlatform( "windows" )]
internal static class VisualStudioInstance
{
	//Busy: a call landing mid-build or on an open dialog is refused rather than queued.
	private const int CallRejected = unchecked((int)0x80010001);
	private const int RetryLater = unchecked((int)0x8001010A);

	//How long to keep offering a refused call, on the thread that asked.
	private const int Attempts = 4;
	private const int Wait = 60;

	//vsViewKindPrimary - whichever editor the file's type normally opens in.
	private const string PrimaryView = "{00000000-0000-0000-0000-000000000000}";

	/// <summary>Opens the file in a running Visual Studio, false where there is none to open it in.</summary>
	public static bool TryOpenFile( string path, int? line, int? column, string solution )
	{
		if ( !OperatingSystem.IsWindows() )
			return false;

		//Finding it is part of the attempt: reading a solution's name is a call into Visual Studio too, and is refused just the same.
		return Attempt( () =>
		{
			if ( Find( solution ) is not { } dte )
				return false;

			Call( Get( dte, "ItemOperations" ), "OpenFile", path, PrimaryView );

			var selection = Get( Get( dte, "ActiveDocument" ), "Selection" );

			if ( column is not null )
				Call( selection, "MoveToLineAndOffset", line ?? 1, column.Value, false );
			else if ( line is not null )
				Call( selection, "GotoLine", line.Value, false );

			Activate( dte );
			return true;
		} );
	}

	/// <summary>Brings the Visual Studio that already has this solution open to the front, false where none has.</summary>
	public static bool TryFocusSolution( string solution )
	{
		if ( !OperatingSystem.IsWindows() || string.IsNullOrEmpty( solution ) )
			return false;

		return Attempt( () =>
		{
			if ( Find( solution, exactly: true ) is not { } dte )
				return false;

			Activate( dte );
			return true;
		} );
	}

	private static void Activate( object dte )
	{
		var window = Get( dte, "MainWindow" );

		Call( window, "Activate" );
	}

	//The instance to talk to: the one with this solution open, or any of them where none has it and exactly does not insist.
	private static object Find( string solution, bool exactly = false )
	{
		object first = null;

		foreach ( var dte in Instances() )
		{
			first ??= dte;

			if ( string.IsNullOrEmpty( solution ) )
				break;

			string open = null;

			//Busy is for the attempt above to sit out; anything else means this one has nothing to say.
			try
			{
				open = Get( Get( dte, "Solution" ), "FullName" ) as string;
			}
			catch ( Exception e ) when ( !IsBusy( e ) )
			{
				continue;
			}

			if ( string.IsNullOrEmpty( open ) )
				continue;

			//FindSolutionFromPath answers with every solution in the folder, joined.
			foreach ( var one in solution.Split( ';', StringSplitOptions.RemoveEmptyEntries ) )
			{
				if ( string.Equals( one.Trim(), open, StringComparison.OrdinalIgnoreCase ) )
					return dte;
			}
		}

		return exactly ? null : first;
	}

	private static IEnumerable<object> Instances()
	{
		if ( GetRunningObjectTable( 0, out var table ) != 0 || table is null )
			yield break;

		if ( CreateBindCtx( 0, out var context ) != 0 || context is null )
			yield break;

		IEnumMoniker monikers;

		try
		{
			table.EnumRunning( out monikers );
			monikers.Reset();
		}
		catch ( Exception )
		{
			yield break;
		}

		var one = new IMoniker[1];

		while ( Next( monikers, one ) )
		{
			string name = null;
			object instance = null;

			try
			{
				one[0].GetDisplayName( context, null, out name );

				if ( name is null || !name.Contains( "VisualStudio.DTE", StringComparison.OrdinalIgnoreCase ) )
					continue;

				table.GetObject( one[0], out instance );
			}
			catch ( Exception )
			{
				continue;
			}

			if ( instance is not null )
				yield return instance;
		}
	}

	//The next moniker, in a shape a foreach with a catch in it can use.
	private static bool Next( IEnumMoniker monikers, IMoniker[] one )
	{
		try
		{
			return monikers.Next( 1, one, IntPtr.Zero ) == 0 && one[0] is not null;
		}
		catch ( Exception )
		{
			return false;
		}
	}

	//Runs something against Visual Studio, offering it again while it says it is busy.
	private static bool Attempt( Func<bool> action )
	{
		for ( var attempt = 1; ; attempt++ )
		{
			try
			{
				return action();
			}
			catch ( Exception e )
			{
				if ( attempt >= Attempts || !IsBusy( e ) )
					return false;

				System.Threading.Thread.Sleep( Wait );
			}
		}
	}

	//InvokeMember hands back what the member threw wrapped, so the reason is looked for all the way down.
	private static bool IsBusy( Exception e )
	{
		for ( var inner = e; inner is not null; inner = inner.InnerException )
		{
			if ( inner is COMException com && (com.HResult == CallRejected || com.HResult == RetryLater) )
				return true;
		}

		return false;
	}

	private static object Get( object target, string name )
		=> target.GetType().InvokeMember( name, BindingFlags.GetProperty, null, target, null );

	private static object Call( object target, string name, params object[] args )
		=> target.GetType().InvokeMember( name, BindingFlags.InvokeMethod, null, target, args );

	[DllImport( "ole32.dll" )]
	private static extern int GetRunningObjectTable( int reserved, out IRunningObjectTable table );

	[DllImport( "ole32.dll" )]
	private static extern int CreateBindCtx( int reserved, out IBindCtx context );
}
