using Editor.Inspectors;
using Rinten.UI;
using System;

namespace Editor.MovieMaker;

#nullable enable

//The editor's one slider (SliderControl, whose groove is SliderTrack) without its ruler, reading its value from a getter each frame and writing it through a setter - it had a groove and a drag of its own.
/// <summary>A value you drag sideways, for a toolbar. Right click puts it back where it started.</summary>
public sealed class MovieToolBarSlider : SliderControl
{
	private readonly Func<float> _getValue;

	public float Minimum { get => Min; init => Min = value; }
	public float Maximum { get => Max; init => Max = value; }

	/// <summary>What right clicking sets it to, if anything.</summary>
	public float? ResetValue { get; init; }

	public MovieToolBarSlider( string toolTip, Func<float> getValue, Action<float> setValue )
	{
		_getValue = getValue;

		Min = 0f;
		Max = 1f;
		Step = 0.01f;
		ShowScale = false;
		ShowBackground = false;
		OnValueChanged = setValue;

		AddClass( "toolbar-slider movie-toolbar-slider" );

		Tooltip = toolTip;
	}

	public override void Tick()
	{
		base.Tick();

		//Somebody else can change it - a shortcut, an undo - so it is read back every frame, except while it is being dragged.
		if ( !IsDragging ) Value = _getValue();
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		if ( ResetValue is not { } reset ) return;

		OnValueChanged?.Invoke( reset );

		e.StopPropagation();
	}
}
