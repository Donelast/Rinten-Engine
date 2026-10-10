
namespace Rinten
{
	public enum LogLevel
	{
		Trace,
		Info,
		Warn,
		Error
	}

	public struct LogEvent
	{
		[Title( "Log Level" )]
		[Category( "Meta Data" )]
		[ReadOnly]
		public LogLevel Level { get; set; }

		[Category( "Meta Data" )]
		[ReadOnly]
		public string Logger { get; set; }

		[ReadOnly]
		public string Message { get; set; }

		[ReadOnly]
		[SkipHotload]
		public Exception Exception { get; set; }

		[ReadOnly]
		public string HtmlMessage { get; set; }

		[Title( "Stack Trace" )]
		[ReadOnly]
		[Category( "Stack Trace" )]
		public string Stack { get; set; }

		[Category( "Meta Data" )]
		[ReadOnly]
		public DateTime Time { get; set; }

		[ReadOnly]
		[SkipHotload]
		public object[] Arguments { get; set; }

		[ReadOnly]
		public int Repeats { get; set; }

		[ReadOnly]
		public bool IsDiagnostic { get; set; }
	}
}
