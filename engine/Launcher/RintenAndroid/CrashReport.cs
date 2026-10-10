using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Android.Content;
using Android.OS;

namespace Rinten.Android;

//Crash report text for a game that failed to start: device, build, error and last log lines
internal static class CrashReport
{
	//How many of the engine's last lines a report carries.
	private const int Lines = 300;

	//What build this is, as the package knows it.
	private static string Version( Context context )
	{
		try
		{
			var info = context?.PackageManager?.GetPackageInfo( context.PackageName, 0 );

			if ( info is null ) return "?";

			//When this build was put on the phone, which is the one thing that says whether what is running is what was just exported.
			var installed = DateTimeOffset.FromUnixTimeMilliseconds( info.LastUpdateTime ).LocalDateTime;

			return $"{info.VersionName} ({info.LongVersionCode}), installed {installed:yyyy-MM-dd HH:mm}";
		}
		catch ( Exception )
		{
			return "?";
		}
	}

	//Which phone, which build, and when - the part of any report that cannot be asked for afterwards, and the same header the run log opens with.
	public static string About( Context context )
	{
		var about = new StringBuilder();

		about.AppendLine( DateTime.Now.ToString( "yyyy-MM-dd HH:mm:ss" ) );
		about.AppendLine( $"package  {context?.PackageName}" );
		about.AppendLine( $"version  {Version( context )}" );
		about.AppendLine( $"device   {Build.Manufacturer} {Build.Model}" );
		about.AppendLine( $"android  {Build.VERSION.Release} (api {(int)Build.VERSION.SdkInt})" );
		about.AppendLine( $"abi      {string.Join( ", ", Build.SupportedAbis ?? new string[0] )}" );
		about.AppendLine();

		return about.ToString();
	}

	//The report itself, as the file says it. Written whole before anything is shown, so a process that dies on the next line still leaves it.
	public static string Text( Context context, string what, Exception e )
	{
		var report = new StringBuilder();

		report.AppendLine( $"{what}" );
		report.AppendLine();
		report.Append( About( context ) );

		if ( e is not null )
		{
			report.AppendLine( e.ToString() );
			report.AppendLine();
		}

		report.AppendLine( $"--- the last {Lines} lines ---" );

		foreach ( var line in Recent() )
			report.AppendLine( line );

		return report.ToString();
	}

	//What the engine said before it stopped - the same messages the editor's console
	//would show, which is where the reason usually is rather than in whatever threw.
	private static string[] Recent()
	{
		try
		{
			var recent = Rinten.Diagnostics.Logging.Recent;
			var lines = new List<string>( Math.Min( recent.Count, Lines ) );

			foreach ( var one in recent )
			{
				lines.Add( $"{one.Time:HH:mm:ss} {one.Level,-5} {one.Logger}: {one.Message}" );
			}

			if ( lines.Count > Lines ) lines.RemoveRange( 0, lines.Count - Lines );

			return lines.ToArray();
		}
		catch ( Exception e )
		{
			return new[] { $"(the engine's own log could not be read: {e.Message})" };
		}
	}
}
