namespace Editor.Inspectors;

/// <summary>Moves an <see cref="INumericValue"/> by however far its handle was dragged.</summary>
public sealed class NumericDragAdjustable : IDragAdjustable
{
	readonly INumericValue _value;

	/// <summary>How far the number moves for one pixel of drag, before the precision keys.</summary>
	public double UnitsPerPixel { get; set; } = 0.01;

	/// <summary>What the number is rounded to, or zero to leave it where the drag put it.</summary>
	public double Step { get; set; }

	/// <summary>The ends the drag is held between, where the property has them.</summary>
	public double? Minimum { get; set; }

	public double? Maximum { get; set; }

	//Where the drag has got to, before Step rounds it. Carried from frame to frame rather than read back off the value each time. A drag slower
	//than one step a frame would otherwise round to nothing every frame and never move at all, and a stepped number would be impossible to nudge.
	double _target;

	//Whether an edit is open, which it is not until something actually moves.
	bool _editing;

	public NumericDragAdjustable( INumericValue value )
	{
		ArgumentNullException.ThrowIfNull( value );

		_value = value;
	}

	public bool CanAdjust => _value.IsEditable;

	public void BeginAdjust()
	{
		_target = _value.Number;
		_editing = false;
	}

	public void Adjust( float pixels )
	{
		if ( pixels == 0.0f || !_value.IsEditable )
			return;

		_target = Hold( _target + pixels * UnitsPerPixel );

		var next = Hold( Snap( _target ) );
		if ( next == _value.Number )
			return;

		//Opened here rather than when the drag started, so a press that turns out to be a click leaves nothing behind to undo.
		if ( !_editing )
		{
			_value.BeginEdit();
			_editing = true;
		}

		_value.Number = next;
	}

	public void EndAdjust()
	{
		if ( !_editing )
			return;

		_editing = false;
		_value.FinishEdit();
	}

	double Snap( double number )
	{
		if ( Step <= 0.0 )
			return number;

		return Math.Round( number / Step, MidpointRounding.AwayFromZero ) * Step;
	}

	double Hold( double number )
	{
		if ( Minimum is not null && number < Minimum.Value )
			return Minimum.Value;

		if ( Maximum is not null && number > Maximum.Value )
			return Maximum.Value;

		return number;
	}
}
