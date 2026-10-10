using System;
using System.Threading;

namespace Editor;

//IProgressSection for the engine, backed by a job on the editor progress bar
internal sealed class ProgressJobSection : IProgressSection
{
	readonly CancellationTokenSource cts = new();

	readonly EditorProgress.Job job;

	public ProgressJobSection()
	{
		job = EditorProgress.Start( "Working", "hourglass_bottom" );
		job.Cancelled = Cancel;
	}

	~ProgressJobSection()
	{
		//The engine hands these out to whatever thread is doing the work, and some of them are let go of rather than disposed.
		MainThread.QueueDispose( this );
	}

	public string Icon
	{
		get => job.Icon;
		set => job.Icon = value;
	}

	public string Title
	{
		get => job.Title;
		set => job.Title = value;
	}

	public string Subtitle
	{
		get => job.Subtitle;
		set => job.Subtitle = value;
	}

	double current;
	double total;

	public double Current
	{
		get => current;
		set { current = value; Report(); }
	}

	public double TotalCount
	{
		get => total;
		set { total = value; Report(); }
	}

	//How far along it is - negative until anything has said how much there is.
	void Report()
	{
		job.Fraction = total > 0 ? (float)Math.Clamp( current / total, 0d, 1d ) : -1f;
	}

	public void Cancel()
	{
		cts.Cancel();

		job.Subtitle = "stopping";
	}

	public CancellationToken GetCancel() => cts.Token;

	public void Dispose()
	{
		GC.SuppressFinalize( this );

		if ( cts.IsCancellationRequested ) job.Fail( "stopped", 2f );
		else job.Finish( null, 0.8f );

		cts.Dispose();
	}
}
