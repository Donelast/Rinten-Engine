using Rinten.UI;
using System;
using System.Globalization;

namespace Editor;

/// <summary>Writes numbers into labels allocation-free, only when changed; always invariant culture</summary>
public static class PanelText
{
	//How wide a formatted number may be before it is cut.
	private const int Room = 48;

	/// <summary>Says something on a label, and nothing at all when it already said it.</summary>
	public static void Set( Rinten.UI.Label label, ReadOnlySpan<char> text )
	{
		if ( label is null ) return;

		if ( label.Text is { } current && text.SequenceEqual( current ) ) return;

		label.Text = text.IsEmpty ? string.Empty : new string( text );
	}

	public static void Set( Rinten.UI.Label label, string text ) => Set( label, text.AsSpan() );

	/// <summary>A number with an optional unit after it - "1.23 ms".</summary>
	public static void Number( Rinten.UI.Label label, float value, ReadOnlySpan<char> format, ReadOnlySpan<char> suffix = default )
	{
		Span<char> buffer = stackalloc char[Room];

		if ( !value.TryFormat( buffer, out var written, format, CultureInfo.InvariantCulture ) )
		{
			Set( label, string.Empty );
			return;
		}

		written += Append( buffer, written, suffix );

		Set( label, buffer[..written] );
	}

	/// <summary>A whole number with optional marks either side - "×128", "42 refs".</summary>
	public static void Count( Rinten.UI.Label label, long value, ReadOnlySpan<char> format = default,
		ReadOnlySpan<char> prefix = default, ReadOnlySpan<char> suffix = default )
	{
		Span<char> buffer = stackalloc char[Room];

		var written = Append( buffer, 0, prefix );

		if ( !value.TryFormat( buffer[written..], out var digits, format, CultureInfo.InvariantCulture ) )
		{
			Set( label, string.Empty );
			return;
		}

		written += digits;
		written += Append( buffer, written, suffix );

		Set( label, buffer[..written] );
	}

	/// <summary>Bytes in whichever unit reads best, and nothing at all for none.</summary>
	public static void Bytes( Rinten.UI.Label label, long bytes, bool signed = false )
	{
		Span<char> buffer = stackalloc char[Room];

		Set( label, buffer[..WriteBytes( buffer, bytes, signed )] );
	}

	/// <summary>Writes bytes into a buffer, and says how much of it was used.</summary>
	public static int WriteBytes( Span<char> buffer, long bytes, bool signed = false )
	{
		if ( bytes == 0 ) return 0;

		var written = 0;

		if ( bytes < 0 ) written += Append( buffer, written, "-" );
		else if ( signed ) written += Append( buffer, written, "+" );

		var value = (double)Math.Abs( bytes );

		//A fraction of a byte is noise, so the smallest unit is a whole one.
		var (scale, unit, format) = Math.Abs( bytes ) switch
		{
			>= 1024L * 1024 * 1024 => (1024.0 * 1024.0 * 1024.0, " GB", "F2"),
			>= 1024 * 1024 => (1024.0 * 1024.0, " MB", "F1"),
			>= 1024 => (1024.0, " KB", "F1"),
			_ => (1.0, " B", "F0"),
		};

		if ( !( value / scale ).TryFormat( buffer[written..], out var digits, format, CultureInfo.InvariantCulture ) )
			return 0;

		written += digits;
		written += Append( buffer, written, unit );

		return written;
	}

	private static int Append( Span<char> buffer, int at, ReadOnlySpan<char> text )
	{
		if ( text.IsEmpty || at + text.Length > buffer.Length ) return 0;

		text.CopyTo( buffer[at..] );

		return text.Length;
	}
}
