namespace Editor.Inspectors;

using Rinten.UI;

//The handful of things a ParticleFloat can be, as a list to pick from.
[StyleSheet( "ParticleFloatControl.cs.scss" )]
sealed class ParticleFloatModePopup : Popup
{
	//One way of getting a number out, as a pair of the value's own settings.
	record Mode( string Icon, string Title, string Description, ParticleFloat.ValueType Type, ParticleFloat.EvaluationType Evaluation );

	static Mode[] Modes { get; } =
	[
		new( "radio_button_unchecked", "Constant", "Value is constant. It stays the same. It doesn't change",
			ParticleFloat.ValueType.Constant, ParticleFloat.EvaluationType.Life ),

		new( "casino", "Random", "Choose a value between two constants every frame",
			ParticleFloat.ValueType.Range, ParticleFloat.EvaluationType.Frame ),

		new( "hdr_strong", "Range", "Choose a value between two constants",
			ParticleFloat.ValueType.Range, ParticleFloat.EvaluationType.Seed ),

		new( "animation", "Lerp", "Lerp between two values over the lifetime",
			ParticleFloat.ValueType.Range, ParticleFloat.EvaluationType.Life ),

		new( "show_chart", "Curve", "Get the value by querying a curve over the lifetime",
			ParticleFloat.ValueType.Curve, ParticleFloat.EvaluationType.Life ),

		new( "area_chart", "Curve with Range", "Choose a path between two curves over the lifetime",
			ParticleFloat.ValueType.CurveRange, ParticleFloat.EvaluationType.Life )
	];

	public static ParticleFloatModePopup Open( Panel source, SerializedObject target )
	{
		CloseAll();

		var popup = new ParticleFloatModePopup();

		popup.AddClass( "particle-float-mode-popup" );
		popup.SetPositioning( source, PositionMode.BelowRight, 2 );
		popup.CloseWhenParentIsHidden = true;
		popup.Build( target );
		popup.SyncAnchor();

		return popup;
	}

	/// <summary>The icon that says which mode a value is in, on the button that opens this.</summary>
	public static string IconFor( ParticleFloat.ValueType type, ParticleFloat.EvaluationType evaluation )
	{
		if ( type == ParticleFloat.ValueType.Constant ) return "radio_button_unchecked";

		return evaluation switch
		{
			ParticleFloat.EvaluationType.Seed => "scatter_plot",
			ParticleFloat.EvaluationType.Life => "play_arrow",
			ParticleFloat.EvaluationType.Frame => "casino",
			_ => "people"
		};
	}

	/// <summary>What a type and evaluation add up to, in words.</summary>
	public static string NameFor( ParticleFloat.ValueType type, ParticleFloat.EvaluationType evaluation )
	{
		switch ( type )
		{
			case ParticleFloat.ValueType.Constant:
				return "Constant value";

			case ParticleFloat.ValueType.Curve:
				return evaluation switch
				{
					ParticleFloat.EvaluationType.Seed => "Random from curve",
					ParticleFloat.EvaluationType.Frame => "Random from curve (per frame)",
					ParticleFloat.EvaluationType.Life => "Curve over lifetime",
					_ => "Unknown"
				};

			case ParticleFloat.ValueType.CurveRange:
				return evaluation switch
				{
					ParticleFloat.EvaluationType.Seed => "Random from range",
					ParticleFloat.EvaluationType.Frame => "Random from range (per frame)",
					ParticleFloat.EvaluationType.Life => "Path between curves over lifetime",
					_ => "Unknown"
				};

			case ParticleFloat.ValueType.Range:
				return evaluation switch
				{
					ParticleFloat.EvaluationType.Seed => "Random between range",
					ParticleFloat.EvaluationType.Frame => "Random between range (per frame)",
					ParticleFloat.EvaluationType.Life => "Lerp between range over lifetime",
					_ => "Unknown"
				};
		}

		return "Unknown Combo";
	}

	void Build( SerializedObject target )
	{
		var type = target.GetProperty( nameof( ParticleFloat.Type ) );
		var evaluation = target.GetProperty( nameof( ParticleFloat.Evaluation ) );

		if ( type is null || evaluation is null ) return;

		var currentType = type.GetValue<ParticleFloat.ValueType>();
		var currentEvaluation = evaluation.GetValue<ParticleFloat.EvaluationType>();

		foreach ( var mode in Modes )
		{
			var row = AddChild<Panel>( "mode" );
			var picked = mode;

			row.SetClass( "active", picked.Type == currentType && picked.Evaluation == currentEvaluation );
			row.AddEventListener( "onclick", () => Pick( type, evaluation, picked ) );

			row.AddChild<IconPanel>( "icon" ).Text = picked.Icon;

			var text = row.AddChild<Panel>( "text" );

			var title = text.AddChild<Rinten.UI.Label>( "title" );
			title.Tokenize = false;
			title.Text = picked.Title;

			var description = text.AddChild<Rinten.UI.Label>( "description" );
			description.Tokenize = false;
			description.Text = picked.Description;
		}
	}

	void Pick( SerializedProperty type, SerializedProperty evaluation, Mode mode )
	{
		type.SetValue( mode.Type );
		evaluation.SetValue( mode.Evaluation );

		Delete();
	}
}
