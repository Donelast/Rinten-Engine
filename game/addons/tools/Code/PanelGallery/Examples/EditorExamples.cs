using Editor.Inspectors;
using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

//The big editors that used to be widgets and are panels now: color, curve, gradient, date, icon. Each inline, and each as the popup or window it opens.
static class EditorExamples
{
	const string Page = "Editors";

	//The inline editor the factory picks for a value, with a note under it that follows the value.
	static (Panel Example, SerializedProperty Property) Inline<T>( T value, Func<T, string> describe )
	{
		var example = Example.Column();
		var property = SampleProperty.Create( value );

		var control = PanelControlFactory.CreateFor( property );
		example.AddChild( control );

		var note = Example.Note( example, describe( value ) );
		note.Tooltip = $"Edited through {control.GetType().Name}";

		example.AddChild( new ValueWatch<T>( property, v => note.Text = describe( v ) ) );

		return (example, property);
	}

	[PanelGallery, Category( Page ), Order( 0 )]
	[Title( "Color" ), Icon( "palette" )]
	[Description( "ColorControl inline; ColorPickerPopup over the same property, opened from the button." )]
	static Panel ColorEditor()
	{
		var (example, property) = Inline( new Color( 0.25f, 0.25f, 0.8f ), v => $"Color is {v.Hex}." );

		TextButtonPanel button = null;
		button = Example.Button( Example.Row( example ), "Open picker", "colorize", () => ColorPickerPopup.Open( button, property ) );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 1 )]
	[Title( "Curve" ), Icon( "show_chart" )]
	[Description( "CurveControl inline; CurveEditorWindow over the same property." )]
	static Panel CurveEditor()
	{
		var (example, property) = Inline(
			new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 0.5f, 1.0f ), new Curve.Frame( 1.0f, 0.8f ) ),
			v => $"Curve has {v.Frames.Length} frames." );

		Example.Button( Example.Row( example ), "Open editor window", "open_in_new", () => CurveEditorWindow.Open( property ) );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 2 )]
	[Title( "Gradient" ), Icon( "gradient" )]
	[Description( "GradientControl inline; GradientEditorWindow over the same property." )]
	static Panel GradientEditor()
	{
		var (example, property) = Inline(
			new Gradient( new Gradient.ColorFrame( 0.0f, Color.Cyan ), new Gradient.ColorFrame( 0.4f, Color.Red ), new Gradient.ColorFrame( 1.0f, Color.Yellow ) ),
			v => $"Gradient has {v.Colors.Count} color stops." );

		Example.Button( Example.Row( example ), "Open editor window", "open_in_new",
			() => GradientEditorWindow.Open( property, property.GetValue<Gradient>(), v => property.SetValue( v ) ) );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 3 )]
	[Title( "Date and time" ), Icon( "event" )]
	[Description( "DateTimeControl inline; DateTimeEditorPopup under the button." )]
	static Panel DateAndTimeEditor()
	{
		var (example, property) = Inline( new DateTime( 2006, 11, 29, 14, 30, 0 ), v => $"It is {v:yyyy-MM-dd HH:mm}." );

		TextButtonPanel button = null;
		button = Example.Button( Example.Row( example ), "Open popup", "edit_calendar",
			() => DateTimeEditorPopup.Open( button, property.GetValue<DateTime>(), v => property.SetValue( v ) ) );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 4 )]
	[Title( "Icon picker" ), Icon( "emoji_symbols" )]
	[Description( "IconPickerPopup under the button, writing back into the icon shown." )]
	static Panel IconPickerEditor()
	{
		var example = Example.Column();

		var row = Example.Row( example, "gap-3" );
		var icon = row.AddChild<Label>( "material-icon" );
		icon.Text = "palette";
		icon.Style.FontSize = 24;

		var note = Example.Note( example, "palette" );

		TextButtonPanel button = null;
		button = Example.Button( row, "Pick an icon", "search", () => IconPickerPopup.Open( button, icon.Text, picked =>
		{
			icon.Text = picked;
			note.Text = picked;
		} ) );

		return example;
	}

	[PanelGallery, Category( Page ), Order( 5 )]
	[Title( "Type" ), Icon( "category" )]
	[Description( "TypeControl inline over a System.Type, with its picker popup." )]
	static Panel TypeEditor()
	{
		var (example, _) = Inline( typeof( Vector3 ), v => $"Type is {v?.Name ?? "none"}." );
		return example;
	}

	//Watches a property and says when its value changed, for notes that follow an editor.
	sealed class ValueWatch<T> : Panel
	{
		readonly SerializedProperty property;
		readonly Action<T> changed;

		T last;
		RealTimeSince since;

		public ValueWatch( SerializedProperty property, Action<T> changed )
		{
			this.property = property;
			this.changed = changed;

			AddClass( "hidden" );

			last = property.GetValue<T>();
		}

		public override void Tick()
		{
			base.Tick();

			if ( since < 0.2f ) return;

			since = 0;

			var now = property.GetValue<T>();

			if ( EqualityComparer<T>.Default.Equals( now, last ) ) return;

			last = now;
			changed( now );
		}
	}
}
