namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>The row that edits a <see cref="ParticleGradient"/>: the colour, and how it is read.</summary>
[StyleSheet( "ParticleGradientControl.cs.scss" )]
[CustomEditor( typeof( ParticleGradient ) )]
public sealed class ParticleGradientControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	SerializedObject _target;
	Panel _controls;

	ParticleGradient.ValueType _builtType;
	bool _built;

	public ParticleGradientControl()
	{
		ShowBackground = false;

	}

	public override void Rebuild()
	{
		DeleteChildren();

		_target = null;
		_controls = null;
		_built = false;

		if ( Property is null ) return;
		if ( !TryGetTarget( out _target ) ) return;

		_controls = AddChild<Panel>( "controls" );

		AddMode( nameof( ParticleGradient.Type ) );
		AddMode( nameof( ParticleGradient.Evaluation ) );

		BuildControls();
	}

	/// <summary>What the value is made of follows its type, which is changed by the dropdown beside it, by undo, or by whatever else holds this value.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( _target is null || !_controls.IsValid() ) return;

		if ( _built && CurrentType == _builtType ) return;

		BuildControls();
	}

	protected override void OnReadOnlyChanged()
	{
		foreach ( var control in ChildrenOfType<BaseControl>() )
			control.ReadOnly = IsReadOnly;

		foreach ( var control in _controls?.ChildrenOfType<BaseControl>() ?? [] )
			control.ReadOnly = IsReadOnly;
	}

	ParticleGradient.ValueType CurrentType =>
		_target?.GetProperty( nameof( ParticleGradient.Type ) )?.GetValue<ParticleGradient.ValueType>() ?? default;

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

		_builtType = type;
		_built = true;

		switch ( type )
		{
			case ParticleGradient.ValueType.Constant:
				AddValue( nameof( ParticleGradient.ConstantValue ) );
				break;

			case ParticleGradient.ValueType.Range:
				AddValue( nameof( ParticleGradient.ConstantA ) );
				AddValue( nameof( ParticleGradient.ConstantB ) );
				break;

			case ParticleGradient.ValueType.Gradient:
				AddValue( nameof( ParticleGradient.GradientA ) );
				break;
		}
	}

	void AddValue( string name )
	{
		if ( _target.GetProperty( name ) is not { } property ) return;
		if ( PanelControlFactory.CreateFor( property ) is not { } control ) return;

		control.ReadOnly = IsReadOnly;

		_controls.AddChild( control );
	}

	//A dropdown rather than the row of buttons a three-value enum would get on its own - these two sit beside the colour, not in place of it.
	void AddMode( string name )
	{
		if ( _target.GetProperty( name ) is not { } property ) return;

		var control = AddChild<EnumControl>();

		control.AddClass( "mode" );
		control.DisplayMode = EnumControl.EnumDisplayMode.Dropdown;
		control.ReadOnly = IsReadOnly;
		control.Property = property;
	}
}
