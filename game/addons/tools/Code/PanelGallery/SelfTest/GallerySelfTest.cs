using Rinten.UI;
using System.Diagnostics;
using System.Text;

namespace Editor.Gallery;

/// <summary>What became of one example when the self-test built it.</summary>
public sealed record GallerySelfTestResult( GalleryEntry Entry, bool Passed, bool Skipped, string Message, double Milliseconds, int PanelCount );

/// <summary>Builds every gallery example offscreen once and reports which ones fail</summary>
public static class GallerySelfTest
{
	public static IReadOnlyList<GallerySelfTestResult> Run( IEnumerable<GalleryEntry> entries, bool includeWindowBound = false )
	{
		var results = new List<GallerySelfTestResult>();

		foreach ( var entry in entries )
		{
			if ( entry.RequiresWindow && !includeWindowBound )
			{
				results.Add( new( entry, true, true, "needs a window", 0, 0 ) );
				continue;
			}

			results.Add( Build( entry ) );
		}

		return results;
	}

	static GallerySelfTestResult Build( GalleryEntry entry )
	{
		var watch = Stopwatch.StartNew();

		try
		{
			var panel = entry.Create();

			if ( panel is null )
				return new( entry, false, false, "the factory returned null", watch.Elapsed.TotalMilliseconds, 0 );

			var count = CountPanels( panel );

			panel.Delete( true );

			return new( entry, true, false, "", watch.Elapsed.TotalMilliseconds, count );
		}
		catch ( Exception e )
		{
			var inner = e is System.Reflection.TargetInvocationException { InnerException: { } cause } ? cause : e;

			return new( entry, false, false, $"{inner.GetType().Name}: {inner.Message}{Where( inner )}", watch.Elapsed.TotalMilliseconds, 0 );
		}
	}

	//The frame an exception came out of, so a failure line says where and not only what.
	static string Where( Exception e )
	{
		var frame = e.StackTrace?
			.Split( '\n' )
			.Select( x => x.Trim() )
			.FirstOrDefault( x => x.StartsWith( "at " ) );

		return frame is null ? "" : $" ({frame[3..]})";
	}

	//Reached by reflection from the integration test, which cannot see this assembly at compile time.
	/// <summary>Every failure in the default catalog, one line each. Empty is the answer wanted.</summary>
	public static IReadOnlyList<string> Failures()
	{
		return Run( GalleryCatalog.CreateDefault().Entries() )
			.Where( x => !x.Passed )
			.Select( x => $"{x.Entry.Id}: {x.Message}" )
			.ToList();
	}

	public static string Report( IReadOnlyList<GallerySelfTestResult> results )
	{
		var passed = results.Count( x => x.Passed && !x.Skipped );
		var skipped = results.Count( x => x.Skipped );
		var failed = results.Count( x => !x.Passed );

		var sb = new StringBuilder();
		sb.AppendLine( $"Panel Gallery self-test: {passed} built, {failed} failed, {skipped} skipped, {results.Count} examples" );

		foreach ( var result in results.Where( x => !x.Passed ) )
			sb.AppendLine( $"  FAIL {result.Entry.Id}: {result.Message}" );

		return sb.ToString().TrimEnd();
	}

	/// <summary>The panel and everything under it.</summary>
	public static int CountPanels( Panel panel )
	{
		if ( panel is null ) return 0;

		var count = 1;

		foreach ( var child in panel.Children )
			count += CountPanels( child );

		return count;
	}
}
