namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>The row that edits a <see cref="ParticleFloat"/>: a mode button, and whatever that mode needs.</summary>
[StyleSheet( "ParticleFloatControl.cs.scss" )]
[CustomEditor( typeof( ParticleFloat ) )]
public sealed class ParticleFloatControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	/// <summary>The colour the numbers and curves are drawn in.</summary>
	public Color HighlightColor { get; set; } = Theme.Green;

	/// <summary>The letter in front of the number fields.</summary>
	public string Label { get; set; } = "f";

	SerializedObject _target;
	ToolbarButton _mode;
	Panel _controls;
	ParticleFloatModePopup _popup;

	ParticleFloat.ValueType _builtType;
	ParticleFloat.EvaluationType _builtEvaluation;
	bool _built;

	public ParticleFloatControl()
	{
		ShowBackground = false;

	}

	public override void Rebuild()
	{
		DeleteChildren();

		_target = null;
		_controls = null;
		_mode = null;
		_built = false;

		if ( Property is null ) return;
		if ( !TryGetTarget( out _target ) ) return;

		_mode = AddChild<ToolbarButton>();
		_mode.OnPressed = OpenModePopup;
		_mode.Disabled = IsReadOnly;

		_controls = AddChild<Panel>( "controls" );

		BuildControls();
	}

	/// <summary>The mode is changed from a popup, by undo, or by whatever else holds this value, so what is on screen is worked out from the value rather than remembered from the pick.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( _target is null || !_controls.IsValid() ) return;

		if ( _built && CurrentType == _builtType && CurrentEvaluation == _builtEvaluation ) return;

		BuildControls();
	}

	protected override void OnReadOnlyChanged()
	{
		if ( _mode.IsValid() )
		{
			_mode.Disabled = IsReadOnly;
			_mode.StateHasChanged();
		}

		foreach ( var control in _controls?.ChildrenOfType<BaseControl>() ?? [] )
			control.ReadOnly = IsReadOnly;
	}

	ParticleFloat.ValueType CurrentType =>
		_target?.GetProperty( nameof( ParticleFloat.Type ) )?.GetValue<ParticleFloat.ValueType>() ?? default;

	ParticleFloat.EvaluationType CurrentEvaluation =>
		_target?.GetProperty( nameof( ParticleFloat.Evaluation ) )?.GetValue<ParticleFloat.EvaluationType>() ?? default;

	//The value as something with properties, editing a copy where it won't hand one out.
	bool TryGetTarget( out SerializedObject target )
	{
		if ( Property.TryGetAsObject( out target ) ) return true;

		var boxed = Property.GetValue<object>();
		if ( boxed is null ) return false;

		var copy = Game.TypeLibrary.GetSerializedObject( boxed );
		if ( copy is null ) return false;

		//A struct is edited on that copy, which goes back to the property whenever one of its fields changes.
		copy.OnPropertyChanged += _ => Property.SetValue( copy.Targets.FirstOrDefault() );

		target = copy;
		return true;
	}

	void BuildControls()
	{
		_controls.DeleteChildren();

		var type = CurrentType;
		var evaluation = CurrentEvaluation;

		_builtType = type;
		_builtEvaluation = evaluation;
		_built = true;

		if ( _mode.IsValid() )
		{
			_mode.Icon = ParticleFloatModePopup.IconFor( type, evaluation );
			_mode.ToolTip = ParticleFloatModePopup.NameFor( type, evaluation );
			_mode.StateHasChanged();
		}

		switch ( type )
		{
			case ParticleFloat.ValueType.Constant:
				AddNumber( nameof( ParticleFloat.ConstantValue ) );
				break;

			case ParticleFloat.ValueType.Range:
				AddNumber( nameof( ParticleFloat.ConstantA ) );
				AddNumber( nameof( ParticleFloat.ConstantB ) );
				break;

			case ParticleFloat.ValueType.Curve:
				AddCurve( nameof( ParticleFloat.CurveA ) );
				break;

			case ParticleFloat.ValueType.CurveRange:
				AddCurveRange( nameof( ParticleFloat.CurveRange ) );
				break;
		}
	}

	void AddNumber( string name )
	{
		if ( _target.GetProperty( name ) is not { } property ) return;

		var field = _controls.AddChild<NumberControl>();

		field.Label = Label;
		field.Color = HighlightColor;
		field.ReadOnly = IsReadOnly;
		field.Property = property;
	}

	void AddCurve( string name )
	{
		if ( _target.GetProperty( name ) is not { } property ) return;

		var field = _controls.AddChild<CurveControl>();

		field.HighlightColor = HighlightColor;
		field.ReadOnly = IsReadOnly;
		field.Property = property;
	}

	void AddCurveRange( string name )
	{
		if ( _target.GetProperty( name ) is not { } property ) return;

		var field = _controls.AddChild<CurveRangeControl>();

		field.HighlightColor = HighlightColor;
		field.ReadOnly = IsReadOnly;
		field.Property = property;
	}

	void OpenModePopup()
	{
		if ( IsReadOnly || _target is null ) return;

		if ( _popup is { IsValid: true } )
		{
			_popup.Delete();
			_popup = null;
			return;
		}

		_popup = ParticleFloatModePopup.Open( _mode, _target );
	}
}
