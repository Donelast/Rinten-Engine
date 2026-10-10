namespace Rinten;

public static partial class RintenSystemExtensions
{
	public static string ToRelativeTimeString( this TimeSpan span )
	{
		if ( span.TotalMinutes < 30 ) return "just now";
		if ( span.TotalHours < 6 ) return "recently";
		if ( span.TotalHours < 24 ) return "today";
		if ( span.TotalHours < 48 ) return "yesterday";
		if ( span.TotalDays < 8 ) return "this week";
		if ( span.TotalDays < 15 ) return "last week";
		if ( span.TotalDays < 30 ) return "this month";
		if ( span.TotalDays < 60 ) return "last month";
		if ( span.TotalDays < 365 ) return "this year";
		if ( span.TotalDays < 365 * 2 ) return "last year";
		return "ages ago";
	}

	public static string ToRemainingTimeString( this TimeSpan span )
	{
		if ( span == TimeSpan.Zero )
			return "";

		if ( span == TimeSpan.MaxValue )
			return "Calculating...";

		if ( span.TotalDays >= 1 )
			return $"{span.Days} day{(span.Days > 1 ? "s" : "")} remaining";
		if ( span.TotalHours >= 1 )
			return $"{span.Hours} hour{(span.Hours > 1 ? "s" : "")} remaining";
		if ( span.TotalMinutes >= 1 )
			return $"{span.Minutes} minute{(span.Minutes > 1 ? "s" : "")} remaining";

		return $"{span.Seconds} second{(span.Seconds > 1 ? "s" : "")} remaining";
	}
}
