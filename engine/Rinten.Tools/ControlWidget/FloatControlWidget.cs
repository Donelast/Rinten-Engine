using System;

namespace Editor;

[CustomEditor( typeof( float ) )]
[CustomEditor( typeof( decimal ) )]
[CustomEditor( typeof( double ) )]
public partial class FloatControlWidget : StringControlWidget
{
	public Color HighlightColor { get; set; }
	public string Icon { get; set; }
	public string Label { get; set; }

	/// <summary>Unit suffix (e.g. m) painted in grey beside the number, never part of the editable text</summary>
	public string Unit { get; set; }

	//The room the unit needs beside the number, or nothing if there is none.
	private float UnitWidth => string.IsNullOrEmpty( Unit ) ? 0.0f : EditorText.Measure( Unit ).x + 6.0f;

	/// <summary>If true we can draw a slider</summary>
	public bool HasRange { get; set; }

	/// <summary>The range, min and max</summary>
	public Vector2 Range { get; set; }

	/// <summary>The step size between range</summary>
	public float RangeStep { get; set; }

	/// <summary>True if the range is clamped between min and max</summary>
	public bool RangeClamped { get; set; }

	//The editor's one slider (Editor.SliderTrack), hung beside the number when the range asks for it.
	private PanelWidget SliderWidget;
	private SliderTrack Slider;

	IconButton _stepUpButton;
	IconButton _stepDownButton;

	public FloatControlWidget( SerializedProperty property ) : base( property )
	{
		Cursor = CursorShape.SizeH;
		Label = "f";
		HighlightColor = Theme.Green;
		MinimumWidth = 60;

		MakeRanged( property );
	}

	internal void MakeRanged( SerializedProperty property )
	{
		var attributes = property.GetAttributes();

		var stepAttribute = attributes.OfType<StepAttribute>().FirstOrDefault();
		if ( stepAttribute is not null )
		{
			RangeStep = stepAttribute.Step;

			CreateStepButtons();
		}

		var clampAttribute = attributes.OfType<RangeAttribute>().FirstOrDefault();
		if ( clampAttribute is not null )
		{
			MakeRanged(
				new Vector2( clampAttribute.Min, clampAttribute.Max ),
				RangeStep,
				clampAttribute.Clamped,
				clampAttribute.Slider
			);
		}
	}

	void CreateStepButtons()
	{
		_stepUpButton ??= new IconButton( "expand_less", () => StepValue( +1 ), this );
		_stepUpButton.Background = Color.Transparent;
		_stepDownButton ??= new IconButton( "expand_more", () => StepValue( -1 ), this );
		_stepDownButton.Background = Color.Transparent;

		UpdateStepButtonState();
	}

	void StepValue( int direction )
	{
		if ( ReadOnly || !SerializedProperty.IsEditable )
			return;

		var old = SerializedProperty.As.Float;
		var value = ApplyStep( old, direction );

		if ( value == old )
			return;

		PropertyStartEdit();

		SerializedProperty.As.Float = value;
		LineEdit.Text = ValueToString();

		SignalValuesChanged();
		UpdateStepButtonState();

		PropertyFinishEdit();
	}

	float ApplyStep( float value, int direction )
	{
		var step = RangeStep > 0 ? RangeStep : 0.01f;

		value += direction * step;

		if ( HasRange && RangeClamped )
			value = value.Clamp( Range.x, Range.y );

		return step > 0 ? value.SnapToGrid( step ) : value;
	}

	void UpdateStepButtonState()
	{
		if ( _stepUpButton is null )
			return;

		var value = SerializedProperty.As.Float;
		var canStepUp = true;
		var canStepDown = true;

		if ( HasRange && RangeClamped )
		{
			canStepUp = value < Range.y;
			canStepDown = value > Range.x;
		}

		_stepUpButton.Enabled = canStepUp && !ReadOnly;
		_stepDownButton.Enabled = canStepDown && !ReadOnly;
	}

	public void MakeRanged( Vector2 range, float step, bool clamped, bool slider )
	{
		HasRange = true;
		Range = range;
		RangeStep = step;
		RangeClamped = clamped;

		if ( !slider )
			return;

		if ( SliderWidget is null )
		{
			//No ruler: a widget row has the height of one line and no more.
			Slider = new SliderTrack { ShowScale = false };
			SliderWidget = new PanelWidget( this, Slider );
		}

		Slider.Min = Range.x;
		Slider.Max = Range.y;
		Slider.Step = RangeStep;
		Slider.Value = SerializedProperty.As.Float;
		Slider.OnValueChanged = value =>
		{
			if ( ReadOnly ) return;
			SerializedProperty.As.Float = value;
			LineEdit.Blur();
		};
		Slider.OnEditStarted = PropertyStartEdit;
		Slider.OnEditFinished = PropertyFinishEdit;
	}

	//The number's box beside a slider: as wide as the inspector's (SliderControl's .entry), at the left.
	const float SliderEntryWidth = 52;

	//With a slider, the well is the number's box alone and the slider stands on nothing - the inspector's slider, drawn the same.
	protected override Rect FieldRect => SliderWidget is not null
		? new Rect( 0, 0, MathF.Min( SliderEntryWidth, LocalRect.Width ), LocalRect.Height )
		: LocalRect;

	protected override void DoLayout()
	{
		var rect = LocalRect;

		//A slider is laid out as the inspector's is: the number in its box on the left, then the groove across the rest, and no letter in front.
		if ( SliderWidget is not null )
		{
			var entry = FieldRect;

			SliderWidget.Position = new Vector2( entry.Right + 1, 0 );
			SliderWidget.Size = new Vector2( MathF.Max( 0, LocalRect.Width - entry.Width - 1 ), LocalRect.Height );

			rect = entry;
		}
		else if ( Icon is not null || Label is not null )
		{
			rect.Left += Theme.RowHeight;
		}

		//Before the buttons, so they sit inside the number's half rather than under the unit.
		rect.Right -= UnitWidth;

		if ( _stepUpButton is not null )
		{
			var buttonWidth = Theme.RowHeight * 0.5f;
			var padding = 2.0f;

			var upRect = new Rect(
				rect.Right - buttonWidth - padding,
				rect.Top,
				buttonWidth,
				rect.Height * 0.5f
			);

			var downRect = new Rect(
				rect.Right - buttonWidth - padding,
				rect.Center.y,
				buttonWidth,
				rect.Height * 0.5f
			);

			_stepUpButton.Position = upRect.Position;
			_stepUpButton.FixedSize = upRect.Size;

			_stepDownButton.Position = downRect.Position;
			_stepDownButton.FixedSize = downRect.Size;

			rect.Right -= buttonWidth + padding + 2;
		}

		LineEdit.Position = rect.Position;
		LineEdit.Size = rect.Size;
	}

	internal static string ValueToStringImpl( SerializedProperty property )
	{
		var value = property.As.Double;

		//Ensure positive zero
		if ( value == 0.0d ) value = 0.0d;

		return value.ToString( "0.###" );
	}

	internal static object StringToValueImpl( string text, SerializedProperty property )
	{
		Type underlyingType = Nullable.GetUnderlyingType( property.PropertyType ) ?? property.PropertyType;
		return Convert.ChangeType( text.ToDoubleEval( property.As.Double ), underlyingType );
	}

	protected override string ValueToString() => ValueToStringImpl( SerializedProperty );

	protected override object StringToValue( string text ) => StringToValueImpl( text, SerializedProperty );

	protected override void PaintControl()
	{

		var h = Size.y;
		bool hovered = IsUnderMouse;
		if ( !Enabled ) hovered = false;

		if ( !string.IsNullOrEmpty( Unit ) )
		{
			Paint.SetDefaultFont();
			Paint.SetPen( Theme.InkDim.WithAlphaMultiplied( IsControlDisabled ? 0.5f : 1.0f ) );
			Paint.DrawText( FieldRect.Shrink( 0, 0, 4, 0 ), Unit, TextFlag.RightCenter );
		}

		//Beside a slider there is no letter: the inspector's slider has none.
		if ( Icon == null && Label == null || SliderWidget is not null )
			return;

		//icon box
		Paint.ClearPen();
		Paint.SetBrush( HighlightColor.Darken( hovered ? 0.7f : 0.8f ).Desaturate( 0.8f ).WithAlphaMultiplied( IsControlDisabled ? 0.5f : 1.0f ) );
		Paint.DrawRect( new Rect( 0, 0, h, h ).Shrink( 2 ), Theme.ControlRadius - 1.0f );

		Paint.SetPen( HighlightColor.Darken( hovered ? 0.0f : 0.1f ).Desaturate( hovered ? 0.0f : 0.2f ).WithAlphaMultiplied( IsControlDisabled ? 0.5f : 1.0f ) );

		if ( string.IsNullOrEmpty( Label ) )
		{
			Paint.DrawIcon( new Rect( 0, 0, h, h ), Icon, h - 6, TextFlag.Center );
		}
		else
		{
			Paint.SetHeadingFont( 9, 500 );
			Paint.DrawText( new Rect( 0, 0, h, h ), Label, TextFlag.Center );
		}
	}

	float dragValue = default;
	bool dragging = false;
	bool dragPressed = false;

	protected override void OnMousePress( MouseEvent e )
	{
		base.OnMousePress( e );

		if ( e.RightMouseButton && !dragPressed )
		{
			LineEdit.Focus();
			LineEdit.SelectAll();
		}

		if ( e.LeftMouseButton && !ReadOnly && e.LocalPosition.x < Height )
		{
			LineEdit.Blur();

			dragPressed = true;
			dragValue = SerializedProperty.As.Float;
			e.Accepted = true;
			PropertyStartEdit();
			return;
		}
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );

		if ( e.LeftMouseButton && !ReadOnly && dragging )
		{
			LineEdit.Focus();
			e.Accepted = true;
		}

		if ( e.LeftMouseButton )
		{
			dragPressed = false;
			Cursor = CursorShape.SizeH;
			dragging = false;
			PropertyFinishEdit();
		}
	}

	protected virtual void OnDragValue( decimal add ) { /*Value = add;*/ }
	protected override void OnMouseMove( MouseEvent e )
	{
		base.OnMouseMove( e );

		if ( !dragPressed )
			return;

		if ( e.ButtonState.Contains( MouseButtons.Left ) )
		{
			dragging = true;
			Update();

			var dragStep = (RangeStep > 0f) ? RangeStep : (HasRange ? (Range.y - Range.x) / 1000.0f : 0.01f);
			var delta = Application.CursorDelta.x * dragStep;
			if ( e.ButtonState.Contains( MouseButtons.Right ) ) delta *= 0.1f;

			dragValue += delta;
			if ( HasRange && RangeClamped ) dragValue = dragValue.Clamp( Range.x, Range.y );

			var steppedValue = (RangeStep != 0.0f) ? dragValue.SnapToGrid( RangeStep ) : dragValue;
			SerializedProperty.As.Float = steppedValue;

			LineEdit.Text = ValueToString();
			SignalValuesChanged();

			Application.CursorPosition = ScreenPosition + Theme.RowHeight * 0.5f;
			Cursor = CursorShape.Blank;

			e.Accepted = true;

			return;
		}
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		Slider?.Value = SerializedProperty.As.Float;

		UpdateStepButtonState();
	}

	bool _keyEditing;

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( ReadOnly || !SerializedProperty.IsEditable )
		{
			base.OnKeyPress( e );
			return;
		}

		if ( LineEdit.IsFocused && !LineEdit.HasSelectedText &&
			(e.Key == KeyCode.Up || e.Key == KeyCode.Down) )
		{
			if ( !_keyEditing )
			{
				PropertyStartEdit();
				_keyEditing = true;
			}

			SerializedProperty.As.Float = ApplyStep( SerializedProperty.As.Float, (e.Key == KeyCode.Up) ? +1 : -1 );
			LineEdit.Text = ValueToString();

			SignalValuesChanged();

			e.Accepted = true;
			return;
		}

		base.OnKeyPress( e );
	}

	protected override void OnKeyRelease( KeyEvent e )
	{
		if ( _keyEditing && (e.Key == KeyCode.Up || e.Key == KeyCode.Down) && !e.IsAutoRepeat )
		{
			PropertyFinishEdit();
			_keyEditing = false;
		}

		base.OnKeyRelease( e );
	}
}
