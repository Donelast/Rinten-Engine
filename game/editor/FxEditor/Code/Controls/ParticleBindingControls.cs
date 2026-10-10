namespace Editor.FxEditor;

using Editor.Inspectors;
using Rinten.UI;

/// <summary>The row that edits a value which is either written down or the name of one of the effect's parameters.</summary>
public abstract class ParticleBindingControl : BaseControl
{
	SerializedObject _binding;
	ToolbarButton _switch;
	Panel _controls;

	bool _built;
	bool _builtParameter;

	protected ParticleBindingControl() => ShowBackground = false;

	//Which of the effect's three lists this binding names.
	protected abstract ParticleParameterKind Kind { get; }

	//Whether the parameter half offers a multiplier as well as a name.
	protected virtual bool HasMultiplier => true;

	//The letter in front of the number fields of the written-down half.
	protected virtual string Label => "f";

	public override void Rebuild()
	{
		DeleteChildren();

		_binding = null;
		_controls = null;
		_switch = null;
		_built = false;

		if ( Property is null ) return;
		if ( !Property.TryGetAsObject( out _binding ) ) return;

		_switch = AddChild<ToolbarButton>();
		_switch.OnPressed = Toggle;
		_switch.Disabled = IsReadOnly;

		_controls = AddChild<Panel>( "controls" );

		BuildControls();
	}

	public override void Tick()
	{
		base.Tick();

		if ( _binding is null || !_controls.IsValid() ) return;

		if ( _built && UsesParameter == _builtParameter ) return;

		BuildControls();
	}

	protected override void OnReadOnlyChanged()
	{
		if ( _switch.IsValid() )
		{
			_switch.Disabled = IsReadOnly;
			_switch.StateHasChanged();
		}

		foreach ( var control in _controls?.ChildrenOfType<BaseControl>() ?? [] )
			control.ReadOnly = IsReadOnly;
	}

	bool UsesParameter => _binding?.GetProperty( "UseParameter" )?.GetValue<bool>() ?? false;

	void Toggle()
	{
		if ( IsReadOnly || _binding?.GetProperty( "UseParameter" ) is not { } property ) return;

		property.SetValue( !UsesParameter );

		BuildControls();
	}

	void BuildControls()
	{
		_controls.DeleteChildren();

		var parameter = UsesParameter;

		_builtParameter = parameter;
		_built = true;

		if ( _switch.IsValid() )
		{
			_switch.Icon = "tune";
			_switch.ToolTip = parameter
				? "Bound to a parameter - press for a plain value"
				: "Plain value - press to bind a parameter";

			_switch.SetClass( "bound", parameter );
			_switch.StateHasChanged();
		}

		if ( parameter ) BuildParameter();
		else BuildValue();

		BuildSlot();
	}

	//The slot line: one of the effect's slots read per particle, where the effect declares any -
	//which slot, which of its numbers, and whether it multiplies the value or stands in for it.
	void BuildSlot()
	{
		if ( _binding.GetProperty( "SlotName" ) is not { } slot ) return;

		var owner = Property?.Parent?.Targets?.FirstOrDefault();
		var definition = ParticleEditorWindow.DefinitionOf( owner );
		var current = slot.GetValue<string>();

		if ( definition is not { Slots.Count: > 0 } && string.IsNullOrEmpty( current ) ) return;

		var line = _controls.AddChild<Panel>( "line parameter slot" );

		var label = line.AddChild<Rinten.UI.Label>( "hint" );
		label.Text = "slot";

		var picker = new ParticleSlotNameControl { Owner = owner, ReadOnly = IsReadOnly };
		picker.Property = slot;
		line.AddChild( picker );

		if ( _binding.GetProperty( "SlotChannel" ) is { } channel && PanelControlFactory.CreateFor( channel ) is { } control )
		{
			control.ReadOnly = IsReadOnly;
			control.AddClass( "channel" );
			line.AddChild( control );
		}

		if ( _binding.GetProperty( "SlotMode" ) is { } mode )
			ModeToggle( line, mode, "slot" );
	}

	//Multiply or replace, as one button that shows which.
	void ModeToggle( Panel line, SerializedProperty mode, string what )
	{
		var toggle = line.AddChild<ToolbarButton>();
		toggle.AddClass( "mode" );
		toggle.Disabled = IsReadOnly;

		void Read()
		{
			var replace = mode.GetValue<ParticleBindingMode>() == ParticleBindingMode.Replace;

			toggle.Icon = replace ? "drag_handle" : "close";
			toggle.ToolTip = replace
				? $"Replace: the {what} is the value"
				: $"Multiply: value x {what}";
			toggle.StateHasChanged();
		}

		toggle.OnPressed = () =>
		{
			if ( IsReadOnly ) return;

			mode.SetValue( mode.GetValue<ParticleBindingMode>() == ParticleBindingMode.Replace
				? ParticleBindingMode.Multiply
				: ParticleBindingMode.Replace );

			Read();
		};

		Read();
	}

	//The value line, always; and under it the parameter line where one is read - which parameter, how it is applied, and what it is multiplied by.
	void BuildValue()
	{
		var line = _controls.AddChild<Panel>( "line" );

		if ( _binding.GetProperty( "Value" ) is not { } value ) return;

		//Through the factory that knows the editor's own controls as well as the game's: the ParticleFloat editor lives in the tools addon, and
		//BaseControl.CreateFor looks only at the game's type library - which is how a bound row came to say "No Editor Found For ParticleFloat".
		if ( PanelControlFactory.CreateFor( value ) is not { } control ) return;

		control.ReadOnly = IsReadOnly;

		if ( control is ParticleFloatControl number )
			number.Label = Label;

		line.AddChild( control );
	}

	//The named half - which parameter, how it applies, and what it is multiplied by.
	void BuildParameter()
	{
		BuildValue();

		var line = _controls.AddChild<Panel>( "line parameter" );

		if ( _binding.GetProperty( "ParameterName" ) is { } name )
			line.AddChild( Picker( name, Kind ) );

		//Multiply or replace: a float parameter scales what is written, curve and all, unless it is told to stand in for it instead.
		if ( _binding.GetProperty( "Mode" ) is { } mode )
			ModeToggle( line, mode, "parameter" );

		if ( HasMultiplier && _binding.GetProperty( "Multiplier" ) is { } multiplier )
		{
			var field = line.AddChild<NumberControl>();

			field.Label = "x";
			field.ReadOnly = IsReadOnly;
			field.Property = multiplier;
		}

		//A vector may also be scaled by a float parameter - a Speed over a direction with a spread in it.
		if ( _binding.GetProperty( "ScaleParameterName" ) is { } scale )
		{
			var scaled = _controls.AddChild<Panel>( "line parameter" );

			var label = scaled.AddChild<Rinten.UI.Label>( "hint" );
			label.Text = "scale by";

			scaled.AddChild( Picker( scale, ParticleParameterKind.Float ) );
		}
	}

	ParticleParameterControl Picker( SerializedProperty property, ParticleParameterKind kind )
	{
		var picker = new ParticleParameterControl
		{
			Kind = kind,
			Owner = Property?.Parent?.Targets?.FirstOrDefault(),
			ReadOnly = IsReadOnly
		};

		//After the two above: setting the property is what builds the list, and the list is the parameters of whichever effect the owner is in.
		picker.Property = property;

		return picker;
	}
}

/// <summary>A number, or the name of one of the effect's float parameters.</summary>
[StyleSheet( "ParticleBindingControls.cs.scss" )]
[CustomEditor( typeof( ParticleFloatBinding ) )]
public sealed class ParticleFloatBindingControl : ParticleBindingControl
{
	protected override ParticleParameterKind Kind => ParticleParameterKind.Float;
}

/// <summary>A vector, or the name of one of the effect's vector parameters.</summary>
[StyleSheet( "ParticleBindingControls.cs.scss" )]
[CustomEditor( typeof( ParticleVectorBinding ) )]
public sealed class ParticleVectorBindingControl : ParticleBindingControl
{
	protected override ParticleParameterKind Kind => ParticleParameterKind.Vector;
}

//No multiplier: a gradient multiplied by a number is not a thing anyone asked for, and the binding does not carry one.
/// <summary>A colour, or the name of one of the effect's colour parameters.</summary>
[StyleSheet( "ParticleBindingControls.cs.scss" )]
[CustomEditor( typeof( ParticleColorBinding ) )]
public sealed class ParticleColorBindingControl : ParticleBindingControl
{
	protected override ParticleParameterKind Kind => ParticleParameterKind.Color;

	protected override bool HasMultiplier => false;
}
