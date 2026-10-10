using Rinten.Utility;

namespace Rinten;

internal static partial class Api
{
	internal static partial class Events
	{
		internal class EventRecord
		{
			public EventRecord( string name )
			{
				Name = name;
				Created = DateTime.UtcNow;
			}

			public string Name { get; set; }
			public DateTimeOffset Created { get; set; }
			public string Version { get; set; }
			public string Mode { get; set; }
			public Dictionary<string, object> Data { get; set; } = new();

			public void SetValue( string key, object value )
			{
				Data[key] = value;
			}

			Dictionary<string, FastTimer> timers;

			public void StartTimer( string name )
			{
				timers ??= new Dictionary<string, FastTimer>();
				timers[name] = FastTimer.StartNew();
			}

			public void FinishTimer( string name )
			{
				if ( timers.TryGetValue( name, out var sw ) )
				{
					SetValue( $"{name}", (int)sw.ElapsedMilliSeconds );
				}
			}

			public IDisposable ScopeTimer( string name )
			{
				var sw = FastTimer.StartNew();
				return new DisposeAction( () => SetValue( $"{name}", (int)sw.ElapsedMilliSeconds ) );
			}

			//Milliseconds are what was recorded, so milliseconds are what is printed; anything under a twentieth of a second is noise beside a start measured in tens of seconds and is left out.
			/// <summary>The steps that took the longest, longest first, into the log.</summary>
			public void WriteTo( Logger log )
			{
				var steps = Data
					.Where( x => x.Value is int ms && ms >= 50 )
					.Select( x => ( x.Key, Ms: (int)x.Value ) )
					.OrderByDescending( x => x.Ms )
					.ToArray();

				if ( steps.Length == 0 ) return;

				var text = new System.Text.StringBuilder( $"{Name}:" );

				foreach ( var (name, ms) in steps )
					text.Append( $"\n  {ms,7:N0} ms  {name}" );

				log.Info( text.ToString() );
			}

			public void Submit( bool andFlush = false )
			{
				Version = Application.Version;
				Mode = Application.IsEditor ? "editor" : "game";
				if ( Application.IsHeadless ) Mode = "app";
				if ( Application.IsDedicatedServer ) Mode = "server";

				Add( this );

				//Log.Info( Json.Serialize( Data ) );

				if ( andFlush )
					Flush();
			}

		}

	}
}
