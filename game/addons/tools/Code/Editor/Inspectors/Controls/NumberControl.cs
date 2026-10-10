namespace Editor.Inspectors;

/// <summary>Inspector editor for numeric properties with an optional prefix label or icon.</summary>
[StyleSheet( "NumberControl.cs.scss" )]
[CustomEditor( typeof( float ) )]
[CustomEditor( typeof( double ) )]
[CustomEditor( typeof( decimal ) )]
[CustomEditor( typeof( char ) )]
[CustomEditor( typeof( byte ) )]
[CustomEditor( typeof( sbyte ) )]
[CustomEditor( typeof( short ) )]
[CustomEditor( typeof( ushort ) )]
[CustomEditor( typeof( int ) )]
[CustomEditor( typeof( uint ) )]
[CustomEditor( typeof( long ) )]
[CustomEditor( typeof( ulong ) )]
public class NumberControl : BaseControl, INumericValue
{
	public override bool SupportsMultiEdit => true;

	public string NumberFormat { get; set; }

	public string Label { get; set; }

	public string Icon { get; set; }

	public Color? Color { get; set; }

	public string HandleTooltip { get; set; }

	/// <summary>Shows the prefix label or icon column.</summary>
	public bool ShowHandle { get; set; } = true;

	public float? MinValue { get; set; }

	public float? MaxValue { get; set; }

	protected string _value;

	protected string Placeholder { get; set; }

	protected TextEntry Entry { get; set; }

	public NumberControl()
	{
		ShowBackground = false;
	}

	public virtual string Value
	{
		get => Property is null ? _value : GetNumericValue().ToString( GetNumberFormat() );
		set
		{
			if ( IsReadOnly )
				return;

			if ( Property is not null )
			{
				if ( !TryParseNumeric( value, out var numericValue ) )
					return;

				SetNumericValue( numericValue );
			}

			_value = value;

			//A control with no property is driven through here alone, so the field follows now rather than on a tick.
			SyncEntry();
		}
	}

	public override void Rebuild()
	{
		DeleteChildren();

		if ( Property is not null )
		{
			if ( Property.TryGetAttribute<PlaceholderAttribute>( out var placeholder ) )
				Placeholder = placeholder.Value;
		}

		if ( ShowHandle )
		{
			var handle = ControlHandle.AddTo( this, GetPrefixText(), Icon, GetPrefixColor(), HandleTooltip );
			handle.Adjustable = CreateDragAdjustable();
		}

		var field = AddChild<Rinten.UI.Panel>( "field" );
		Entry = field.AddChild<TextEntry>();
		Entry.Value = Value;
		Entry.OnTextEdited = OnEdited;
		Entry.Placeholder = Placeholder;
		Entry.Disabled = IsReadOnly;
		Entry.Numeric = true;
		Entry.NumberFormat = GetNumberFormat();

		if ( ResolveMinValue() is { } min )
			Entry.MinValue = min;

		if ( ResolveMaxValue() is { } max )
			Entry.MaxValue = max;
	}

	//The drag its handle runs, or null when there is no property to move.
	protected virtual IDragAdjustable CreateDragAdjustable()
	{
		if ( Property is null )
			return null;

		var min = ResolveMinValue();
		var max = ResolveMaxValue();
		var step = ResolveDragStep();

		return new NumericDragAdjustable( this )
		{
			Step = step,
			Minimum = min,
			Maximum = max,
			UnitsPerPixel = ResolveDragSensitivity( step, min, max ),
		};
	}

	//The lowest the number may go, said by the control or by the property.
	protected float? ResolveMinValue()
	{
		if ( MinValue is not null )
			return MinValue.Value;

		if ( Property?.TryGetAttribute<MinMaxAttribute>( out var range ) is true )
			return range.MinValue;

		return null;
	}

	protected float? ResolveMaxValue()
	{
		if ( MaxValue is not null )
			return MaxValue.Value;

		if ( Property?.TryGetAttribute<MinMaxAttribute>( out var range ) is true )
			return range.MaxValue;

		return null;
	}

	//What a dragged number is rounded to: whatever it was marked with, whole numbers for a type that only holds those, nothing otherwise.
	protected virtual float ResolveDragStep()
	{
		if ( Property?.TryGetAttribute<StepAttribute>( out var step ) is true && step.Step > 0.0f )
			return step.Step;

		return IsIntegerProperty() ? 1.0f : 0.0f;
	}

	//Drag speed: ranged spans the field width, stepped moves a step per few px, else hundredths
	protected virtual float ResolveDragSensitivity( float step, float? min, float? max )
	{
		const float pixelsAcrossRange = 400.0f;
		const float pixelsPerStep = 5.0f;

		if ( min is not null && max is not null && max.Value > min.Value )
			return (max.Value - min.Value) / pixelsAcrossRange;

		if ( step > 0.0f )
			return step / pixelsPerStep;

		return 0.01f;
	}

	//When the value moved, not every frame: reading it boxes and formatting it allocates, per number, per frame.
	protected override void OnPropertyValueChanged()
	{
		base.OnPropertyValueChanged();

		SyncEntry();
	}

	void SyncEntry()
	{
		if ( !Entry.IsValid() )
			return;

		var text = Value;
		if ( Entry.Value != text )
			Entry.Value = text;
	}

	void OnEdited( string text ) => Value = text;

	protected override void OnReadOnlyChanged()
	{
		if ( Entry.IsValid() )
			Entry.Disabled = IsReadOnly;
	}

	string GetNumberFormat()
	{
		if ( !string.IsNullOrEmpty( NumberFormat ) )
			return NumberFormat;

		return IsIntegerProperty() ? "0" : "0.###";
	}

	string GetPrefixText()
	{
		if ( !string.IsNullOrEmpty( Label ) )
			return Label;

		return IsIntegerProperty() ? "i" : "f";
	}

	Color GetPrefixColor()
	{
		if ( Color is not null )
			return Color.Value;

		return IsIntegerProperty() ? Theme.Blue : Theme.Green;
	}

	bool IsIntegerProperty()
	{
		if ( Property is null )
			return false;

		var type = Property.PropertyType;
		return type == typeof( char )
			|| type == typeof( byte )
			|| type == typeof( sbyte )
			|| type == typeof( short )
			|| type == typeof( ushort )
			|| type == typeof( int )
			|| type == typeof( uint )
			|| type == typeof( long )
			|| type == typeof( ulong );
	}

	double GetNumericValue()
	{
		var value = Property.GetValue<object>();

		//A char is a number to this control but not to Convert, which throws on it - every tick, from SyncEntry, for as long as the row is shown.
		if ( value is char c ) return c;

		return value is null ? 0 : Convert.ToDouble( value );
	}

	void SetNumericValue( double value )
	{
		if ( Property.PropertyType == typeof( double ) )
			Property.As.Double = value;
		else if ( Property.PropertyType == typeof( decimal ) )
			Property.SetValue( (decimal)value );
		else if ( Property.PropertyType == typeof( float ) )
			Property.As.Float = (float)value;
		else
		{
			var targetType = Property.NullableType ?? Property.PropertyType;

			//Convert has no road from a double to a char; through an int there is one.
			if ( targetType == typeof( char ) )
				Property.SetValue( (char)Math.Clamp( (int)value, char.MinValue, char.MaxValue ) );
			else
				Property.SetValue( Convert.ChangeType( value, targetType ) );
		}
	}

	bool TryParseNumeric( string text, out double value )
	{
		if ( NumericExpression.TryEvaluate( text, out value ) )
			return true;

		if ( Entry is not null )
			text = Entry.FixNumeric();

		return double.TryParse( text, out value );
	}

	//The number itself, offered to whatever drags the handle and to nobody else - explicitly
	//implemented, because the control's own public face for the value is the text in its field.

	bool INumericValue.IsEditable => Property is not null && !IsReadOnly;

	double INumericValue.Number
	{
		get => Property is null ? 0.0 : GetNumericValue();
		set
		{
			if ( Property is null || IsReadOnly )
				return;

			SetNumericValue( value );

			//Now, rather than on the tick that would notice the value had moved: the drag holds the focus, and a focused control leaves its field be.
			SyncEntry();
		}
	}

	void INumericValue.BeginEdit() => Property?.Parent?.NoteStartEdit( Property );

	void INumericValue.FinishEdit() => Property?.Parent?.NoteFinishEdit( Property );
}
