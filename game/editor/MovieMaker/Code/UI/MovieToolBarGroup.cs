using Rinten.UI;
using System;
using System.Globalization;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>A run of toolbar items that belong together, on a plate of their own.</summary>
public sealed class MovieToolBarGroup : Panel
{
	private Func<bool>? _isVisible;

	/// <summary>Survives <see cref="MovieToolBar.Reset"/> - it belongs to the editor, not to an edit mode.</summary>
	public bool IsPermanent { get; init; }

	/// <summary>Sits at the far end of the bar rather than after the last group.</summary>
	public bool AlignRight { get; init; }

	public MovieToolBarGroup()
	{
		AddClass( "movie-toolbar-group" );
	}

	/// <summary>Shows this group only while <paramref name="predicate"/> holds.</summary>
	public MovieToolBarGroup VisibleWhen( Func<bool> predicate )
	{
		_isVisible = predicate;

		Poll();

		return this;
	}

	public override void Tick()
	{
		base.Tick();

		Poll();
	}

	private void Poll()
	{
		if ( _isVisible is null ) return;

		SetClass( "hidden", !_isVisible() );
	}

	public MovieToolBarAction AddAction( ToolBarItemDisplay display, Action action, Func<bool>? isEnabled = null ) =>
		AddPanel( new MovieToolBarAction( display, action, isEnabled ) );

	public MovieToolBarToggle AddToggle( ToolBarItemDisplay display, Func<bool> getState, Action<bool> setState ) =>
		AddPanel( new MovieToolBarToggle( display, getState, setState ) );

	public Rinten.UI.Label AddLabel( string text )
	{
		var label = AddChild<Rinten.UI.Label>( "movie-toolbar-label" );

		label.Text = text;

		return label;
	}

	/// <summary>A label that says whatever <paramref name="getText"/> says, once a frame.</summary>
	public MovieToolBarReadout AddReadout( Func<string> getText ) =>
		AddPanel( new MovieToolBarReadout( getText ) );

	public MovieToolBarSlider AddSlider( MovieToolBarSlider slider ) => AddPanel( slider );

	/// <summary>A slider with a readout of its value beside it.</summary>
	public MovieToolBarSlider AddSlider( string title, Func<float> getValue, Action<float> setValue,
		float minimum = 0f, float maximum = 1f, float step = 0.01f, Func<string>? getLabel = null )
	{
		var slider = AddPanel( new MovieToolBarSlider( title, getValue, setValue )
		{
			Minimum = minimum,
			Maximum = maximum,
			Step = step
		} );

		AddReadout( getLabel ?? (() => getValue().ToString( CultureInfo.InvariantCulture )) );

		return slider;
	}

	public FunctionSelector<InterpolationMode> AddInterpolationSelector( Func<InterpolationMode> getValue, Action<InterpolationMode> setValue ) =>
		AddPanel( new FunctionSelector<InterpolationMode>( "Interpolation Mode", InterpolationFunctions.ForMode, getValue, setValue ) );

	public FunctionSelector<KeyframeInterpolation> AddKeyframeInterpolationSelector( Func<KeyframeInterpolation> getValue, Action<KeyframeInterpolation> setValue ) =>
		AddPanel( new FunctionSelector<KeyframeInterpolation>( "Keyframe Interpolation", InterpolationFunctions.ForKeyframe, getValue, setValue ) );

	/// <summary>A gap between two things in the same group.</summary>
	public void AddSpacing() => AddChild<Panel>( "spacing" );

	/// <summary>Puts any panel at the end of this group.</summary>
	public TPanel AddPanel<TPanel>( TPanel panel )
		where TPanel : Panel
	{
		panel.Parent = this;

		return panel;
	}
}

/// <summary>A piece of text in a toolbar that keeps up with what it is reading.</summary>
public sealed class MovieToolBarReadout : Rinten.UI.Label
{
	private readonly Func<string> _getText;

	public MovieToolBarReadout( Func<string> getText )
	{
		_getText = getText;

		AddClass( "movie-toolbar-readout" );

		Text = getText();
	}

	public override void Tick()
	{
		base.Tick();

		Text = _getText();
	}
}
