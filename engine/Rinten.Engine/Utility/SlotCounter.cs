namespace Rinten.Utility;

//Per-thread counter slots on separate cache lines, summed for the total; avoids contention
internal sealed class SlotCounter
{
	//How many slots there are - a power of two, so the thread's id folds with a mask.
	private const int Slots = 32;

	//How far apart two slots are, in longs: a cache line is eight of them.
	private const int Stride = 8;

	private readonly long[] counts = new long[Slots * Stride];

	//This thread's slot.
	private int At => ( System.Environment.CurrentManagedThreadId & ( Slots - 1 ) ) * Stride;

	//Counts one, on this thread's own line.
	internal void Add() => counts[At]++;

	//Counts several.
	internal void Add( long many ) => counts[At] += many;

	//Everything counted since the last Clear.
	internal long Total
	{
		get
		{
			var sum = 0L;

			for ( var i = 0; i < counts.Length; i += Stride ) sum += counts[i];

			return sum;
		}
	}

	internal void Clear() => System.Array.Clear( counts );
}
