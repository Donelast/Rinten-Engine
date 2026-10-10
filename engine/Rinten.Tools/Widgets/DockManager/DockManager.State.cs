using System;

namespace Editor;

public partial class DockManager
{
	/// <summary>Called when the layout state is loaded, e.g. when the default layout is applied or a saved layout is restored.</summary>
	public Action OnLayoutLoaded { get; set; }

	/// <summary>A string representing the entire state of the dock manager (position of all docks, etc). Setting this restores the layout and invokes <see cref="OnLayoutLoaded"/>.</summary>
	public string State
	{
		get => SaveStateManaged();
		set => RestoreState( value );
	}

	/// <summary>Restore a layout previously captured from <see cref="State"/>. Returns false if the state couldn't be restored, e.g. it was saved by an incompatible version.</summary>
	public bool RestoreState( string state )
	{
		//The other half of the workspace timing in EditorWorkspaces.MakeCurrent: restoring a layout builds every dock in it, and a switch
		//that takes a second could be spending it here or in what the docks do once they exist. Two numbers say which; one said neither.
		var started = tracing ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;

		if ( !RestoreStateManaged( state ) ) return false;

		var built = System.Diagnostics.Stopwatch.GetTimestamp();

		OnLayoutLoaded?.Invoke();

		if ( tracing )
		{
			var frequency = System.Diagnostics.Stopwatch.Frequency / 1000.0;

			Log.Info( $"layout: docks built in {( built - started ) / frequency:F0} ms, " +
				$"loaded in {( System.Diagnostics.Stopwatch.GetTimestamp() - built ) / frequency:F0} ms" );
		}

		return true;
	}

	private static readonly bool tracing = Rinten.Utility.CommandLine.HasSwitch( "-uitrace" );
}
