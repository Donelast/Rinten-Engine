using Rinten.Utility;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using static Rinten.Diagnostics.Performance;

namespace Rinten.Diagnostics;

public static partial class PerformanceStats
{
	public sealed class Timings
	{
		internal static ConcurrentDictionary<string, Timings> All = new( StringComparer.OrdinalIgnoreCase );

		//All by the name's identity rather than its text - see Get. Up here, before the named
		//timings below: their initialisers call Get, and a static is made in the order it is written.
		private static readonly Known[] byIdentity = new Known[512];

		private sealed record Known( string Name, Timings Timing );

		//All timings as an array, so per-frame walks don't allocate; copied only when a timing is added
		internal static Timings[] Listed { get; private set; } = [];

		private static readonly object listing = new();

		public static Timings Async { get; } = Get( "Async", "#e9edc9" );
		public static Timings Animation { get; } = Get( "Animation", "#ff70a6" );
		public static Timings Audio { get; } = Get( "Audio", "#bdb2ff" );
		public static Timings Editor { get; } = Get( "Editor", "#7f8188" );
		//public static Timings Io { get; } = Get( "IO", "#b5838d" );
		public static Timings Idle { get; } = Get( "Idle", "#808080" );
		public static Timings Input { get; } = Get( "Input", "#e9ff70" );
		//public static Timings Internal { get; } = Get( "Internal", "#e5e5e5" );
		public static Timings NavMesh { get; } = Get( "NavMesh", "#738D45" );
		public static Timings Network { get; } = Get( "Network", "#809bce" );
		public static Timings Particles { get; } = Get( "Particles", "#f7aef8" );
		public static Timings Physics { get; } = Get( "Physics", "#f37748" );
		public static Timings Render { get; } = Get( "Render", "#8ac926" );
		public static Timings Update { get; } = Get( "Update", "#56cbf9" );
		public static Timings Ui { get; } = Get( "UI", "#b4869f" );
		public static Timings Video { get; } = Get( "Video", "#f5cac3" );
		public static Timings GcPause { get; } = Get( "GcPause", "#00f5d4" );

		/// <summary>Return a list of the main top tier timings we're interested in</summary>
		public static IEnumerable<Timings> GetMain() => _main;

		//The timings of GetMain, to index - enumerated as an enumerable, every walk boxes its enumerator.
		internal static ReadOnlyCollection<Timings> Main => _main;

		private static readonly ReadOnlyCollection<Timings> _main = BuildMain();

		private static ReadOnlyCollection<Timings> BuildMain()
		{
			var list = new List<Timings> { Async, Animation, Audio, GcPause, Idle, Input, NavMesh, Network, Particles, Physics, Render, Update, Ui, Video };
			if ( Application.IsEditor )
				list.Add( Editor );
			return list.AsReadOnly();
		}

		public string Name { get; internal set; }
		public Color Color { get; internal set; }

		Superluminal _superluminal;

		internal static void FlipAll()
		{
			foreach ( var timing in Listed )
			{
				if ( timing.IsManualFlip )
					continue;

				timing.Flip();
			}
		}

		public static Timings Get( string stage, Color? color = default )
		{
			//Fast lookup by string reference first (call sites pass literals), before the case-insensitive hash
			var slot = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode( stage ) & ( byIdentity.Length - 1 );
			var known = byIdentity[slot];

			if ( known is not null && ReferenceEquals( known.Name, stage ) )
				return known.Timing;

			if ( !All.TryGetValue( stage, out var timing ) )
				timing = Add( stage, color );

			//Filled once and never replaced: a name built afresh each time takes a slot at most
			//once, rather than turning one over - and an allocation with it - on every call.
			if ( known is null )
				byIdentity[slot] = new Known( stage, timing );

			return timing;
		}

		//Slow path kept out of Get so its capturing lambda doesn't allocate a closure on every hit
		private static Timings Add( string stage, Color? color )
		{
			var timing = All.GetOrAdd( stage, static ( name, tint ) => new Timings( name, tint ?? Color.White ), color );

			//Two threads can ask for one new name at once. The dictionary settles which of them made it; the array takes that one, once.
			lock ( listing )
			{
				if ( System.Array.IndexOf( Listed, timing ) < 0 ) Listed = [.. Listed, timing];
			}

			return timing;
		}

		internal Timings( string name, Color color )
		{
			Name = name;
			Color = color;
			_superluminal = new Superluminal( Name, color );
		}

		public struct Frame
		{
			public int Calls;
			public float TotalMs;
		}

		public Rinten.Utility.CircularBuffer<Frame> History { get; } = new( 256 );

		internal void Flip()
		{
			lock ( this )
			{
				var f = new Frame();
				f.Calls = calls;
				f.TotalMs = (float)(ticks * (1_000.0 / Stopwatch.Frequency)) + (float)milliseconds;

				History.PushFront( f );

				calls = 0;
				ticks = 0;
				milliseconds = 0;
			}
		}

		public bool IsManualFlip { get; set; }

		int calls;
		long ticks;
		double milliseconds;

		internal Performance.ScopeSection Scope()
		{
			_superluminal.Start();

			Interlocked.Increment( ref calls );

			//The same span, said out loud - so WPA and PerfView show this zone under this name, beside the context
			//switches and the allocations that our own profiler cannot see. Costs one field read when nobody is recording.
			EngineEventSource.Instance.ZoneStart( Name, (int)EngineLoop.LoopFrames );

			return new Performance.ScopeSection()
			{
				Source = this,
				Timer = FastTimer.StartNew(),
				//Only snapshot on main thread — GC.GetTotalPauseDuration() is process-wide, attributing from multiple threads simultaneously would double-subtract.
				GcPauseTicksAtStart = ThreadSafe.IsMainThread ? GC.GetTotalPauseDuration().Ticks : -1,
				Profile = FrameProfiler.Push( Name ),
			};
		}

		internal void ScopeFinished( ScopeSection section )
		{
			var elapsedTicks = section.Timer.ElapsedTicks;

			//Subtract any GC pause that occurred during this scope so per-system timings aren't inflated by GC.
			if ( section.GcPauseTicksAtStart >= 0 )
			{
				var elapsedMs = elapsedTicks * (1_000.0 / Stopwatch.Frequency);
				var gcMs = Math.Min(
					TimeSpan.FromTicks( GC.GetTotalPauseDuration().Ticks - section.GcPauseTicksAtStart ).TotalMilliseconds,
					elapsedMs );

				if ( gcMs > 0 )
					elapsedTicks -= (long)(gcMs * Stopwatch.Frequency / 1_000.0);
			}

			Interlocked.Add( ref ticks, elapsedTicks );

			EngineEventSource.Instance.ZoneStop( Name, elapsedTicks );

			_superluminal.Dispose();
		}

		internal void AddMilliseconds( double ms, int addcalls = 1 )
		{
			milliseconds += ms;
			calls += addcalls;
		}

		public float AverageMs( int frames )
		{
			int count = Math.Min( frames, History.Size );
			if ( count == 0 ) return 0;

			double sum = 0;
			for ( int i = 0; i < count; i++ ) sum += History[i].TotalMs;
			return (float)(sum / count);
		}

		public PeriodMetric GetMetric( int frames )
		{
			int count = Math.Min( frames, History.Size );
			if ( count == 0 ) return default;

			if ( count == 1 )
			{
				var f = History[0];
				return new PeriodMetric( f.TotalMs, f.TotalMs, f.TotalMs, f.Calls );
			}

			float min = float.MaxValue, max = float.MinValue;
			double sum = 0;
			int calls = 0;
			for ( int i = 0; i < count; i++ )
			{
				var f = History[i];
				if ( f.TotalMs < min ) min = f.TotalMs;
				if ( f.TotalMs > max ) max = f.TotalMs;
				sum += f.TotalMs;
				calls += f.Calls;
			}
			return new PeriodMetric( min, max, (float)(sum / count), calls );
		}
	}

}
