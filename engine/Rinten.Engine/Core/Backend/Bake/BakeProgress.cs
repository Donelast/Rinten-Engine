using System.Threading;

namespace Rinten.Bake;

//How far a bake has got, written by the cores doing it and read by whoever is
//showing it. Nothing here is locked: a count read a step late is still a count.
internal sealed class BakeProgress
{
	private int done;

	/// <summary>What is being done now.</summary>
	public volatile string Stage = "";

	/// <summary>How many steps the stage has.</summary>
	public volatile int Total;

	public int Done => Volatile.Read( ref done );

	public void Begin( string stage, int total )
	{
		Stage = stage;
		Total = total;
		Volatile.Write( ref done, 0 );
	}

	public void Step() => Interlocked.Increment( ref done );
}
