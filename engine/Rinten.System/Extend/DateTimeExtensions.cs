namespace Rinten;

public static partial class RintenSystemExtensions
{
	static DateTime epoch = new DateTime( 1970, 1, 1, 0, 0, 0, DateTimeKind.Utc );

	/// <summary>Returns the UNIX time stamp - number of seconds since 1st of January, 1970.</summary>
	public static int GetEpoch( this DateTime d )
	{
		var t = d - epoch;
		return (int)(t.TotalSeconds);
	}

	/// <summary>Converts UNIX time stamp to a DateTime object.</summary>
	public static DateTime ToDateTime( this int seconds )
	{
		return epoch.AddSeconds( seconds );
	}

	public static DateTime ToDateTime( this long seconds )
	{
		return epoch.AddSeconds( seconds );
	}

	public static string ToRelativeTimeString( this DateTime dateTime )
	{
		var span = DateTime.Now - dateTime;
		return span.ToRelativeTimeString();
	}

	/// <summary>Convert date into a human readable relative time string.</summary>
	public static string Humanize( this DateTime dateTime )
	{
		return Humanizer.DateHumanizeExtensions.Humanize( dateTime );
	}

	/// <summary>Convert date into a human readable relative time string.</summary>
	public static string Humanize( this DateTimeOffset dateTime )
	{
		return Humanizer.DateHumanizeExtensions.Humanize( dateTime );
	}

	/// <summary>Convert date into a human readable relative time string.</summary>
	public static string Humanize( this TimeOnly dateTime )
	{
		return Humanizer.DateHumanizeExtensions.Humanize( dateTime );
	}

	/// <summary>Convert date into a human readable relative time string.</summary>
	public static string Humanize( this DateOnly dateTime )
	{
		return Humanizer.DateHumanizeExtensions.Humanize( dateTime );
	}

	/// <summary>Convert date into a human readable relative time string.</summary>
	public static string Humanize( this TimeSpan dateTime, int precision = 1 )
	{
		return Humanizer.TimeSpanHumanizeExtensions.Humanize( dateTime, precision, minUnit: Humanizer.Localisation.TimeUnit.Second );
	}

}
