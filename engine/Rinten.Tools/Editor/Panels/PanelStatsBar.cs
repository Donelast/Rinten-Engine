using Rinten.UI;

namespace Editor;

/// <summary>The line of numbers along the bottom of a diagnostic dock.</summary>
public class PanelStatsBar : Panel
{
	public PanelStatsBar()
	{
		AddClass( "panel-stats-bar" );

		EditorStyles.Apply( this, "PanelStatsBar.cs.scss" );
	}

	/// <summary>Adds an entry and hands it back to be written to.</summary>
	public PanelStat AddStat( string name )
	{
		var stat = AddChild<PanelStat>();

		stat.SetName( name );

		return stat;
	}
}

/// <summary>How loudly a stat is saying what it says.</summary>
public enum PanelStatTone
{
	/// <summary>The usual: nothing to see.</summary>
	Plain,

	/// <summary>Worth a look.</summary>
	Warn,

	/// <summary>Wrong.</summary>
	Alarm,

	/// <summary>Better than usual.</summary>
	Good,
}

/// <summary>One name-and-value pair in a <see cref="PanelStatsBar"/>.</summary>
public class PanelStat : Panel
{
	private readonly Rinten.UI.Label name;
	private readonly Rinten.UI.Label value;

	private PanelStatTone tone = PanelStatTone.Plain;

	public PanelStat()
	{
		AddClass( "panel-stat" );

		name = AddChild<Rinten.UI.Label>( "name" );
		name.Tokenize = false;

		value = AddChild<Rinten.UI.Label>( "value" );
		value.Tokenize = false;
	}

	/// <summary>The label the value is read against.</summary>
	public void SetName( string text ) => PanelText.Set( name, text );

	/// <summary>The value itself, and how loud it is.</summary>
	public void Set( string text, PanelStatTone tone = PanelStatTone.Plain )
	{
		PanelText.Set( value, text );
		SetTone( tone );
	}

	public void Set( System.ReadOnlySpan<char> text, PanelStatTone tone = PanelStatTone.Plain )
	{
		PanelText.Set( value, text );
		SetTone( tone );
	}

	public void SetNumber( float number, string format, string suffix = null, PanelStatTone tone = PanelStatTone.Plain )
	{
		PanelText.Number( value, number, format, suffix );
		SetTone( tone );
	}

	public void SetCount( long count, PanelStatTone tone = PanelStatTone.Plain )
	{
		PanelText.Count( value, count );
		SetTone( tone );
	}

	public void SetBytes( long bytes, PanelStatTone tone = PanelStatTone.Plain )
	{
		PanelText.Bytes( value, bytes );
		SetTone( tone );
	}

	/// <summary>Whether this entry is on the bar at all.</summary>
	public void SetShown( bool shown ) => SetClass( "hidden", !shown );

	private void SetTone( PanelStatTone value )
	{
		if ( tone == value ) return;

		SetClass( ClassFor( tone ), false );
		SetClass( ClassFor( value ), true );

		tone = value;
	}

	private static string ClassFor( PanelStatTone tone ) => tone switch
	{
		PanelStatTone.Warn => "warn",
		PanelStatTone.Alarm => "alarm",
		PanelStatTone.Good => "good",
		_ => "plain",
	};
}
