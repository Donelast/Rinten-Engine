using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.UiEditor;

//What a style value can be picked from rather than typed: the words a keyword property takes,
//and a swatch for a colour. A quiet mark beside the field; the field still takes anything typed.
internal static class StyleChoices
{
	static readonly Dictionary<string, string[]> Words = new( StringComparer.OrdinalIgnoreCase )
	{
		["display"] = new[] { "flex", "block", "grid", "none", "contents" },
		["position"] = new[] { "static", "relative", "absolute", "fixed" },
		["flex-direction"] = new[] { "row", "column", "row-reverse", "column-reverse" },
		["flex-wrap"] = new[] { "nowrap", "wrap", "wrap-reverse" },
		["justify-content"] = new[] { "flex-start", "center", "flex-end", "space-between", "space-around", "space-evenly" },
		["align-items"] = new[] { "flex-start", "center", "flex-end", "stretch", "baseline" },
		["align-content"] = new[] { "flex-start", "center", "flex-end", "stretch", "space-between", "space-around" },
		["align-self"] = new[] { "auto", "flex-start", "center", "flex-end", "stretch" },
		["overflow"] = new[] { "visible", "hidden", "scroll" },
		["overflow-x"] = new[] { "visible", "hidden", "scroll" },
		["overflow-y"] = new[] { "visible", "hidden", "scroll" },
		["text-align"] = new[] { "left", "center", "right" },
		["font-weight"] = new[] { "normal", "bold", "100", "200", "300", "400", "500", "600", "700", "800", "900" },
		["font-style"] = new[] { "normal", "italic" },
		["text-transform"] = new[] { "none", "uppercase", "lowercase", "capitalize" },
		["text-decoration"] = new[] { "none", "underline", "line-through" },
		["white-space"] = new[] { "normal", "nowrap", "pre", "pre-wrap" },
		["text-overflow"] = new[] { "clip", "ellipsis" },
		["cursor"] = new[] { "default", "pointer", "text", "move", "crosshair", "none", "not-allowed", "grab" },
		["pointer-events"] = new[] { "auto", "all", "none" },
		["box-sizing"] = new[] { "border-box", "content-box" },
		["border-style"] = new[] { "solid", "dashed", "dotted", "none" },
		["background-size"] = new[] { "auto", "cover", "contain", "100% 100%" },
		["background-repeat"] = new[] { "no-repeat", "repeat", "repeat-x", "repeat-y" },
		["background-position"] = new[] { "center", "top left", "top", "top right", "left", "right", "bottom left", "bottom", "bottom right" },
		["animation-timing-function"] = new[] { "linear", "ease", "ease-in", "ease-out", "ease-in-out" },
		["animation-iteration-count"] = new[] { "1", "2", "3", "infinite" },
		["animation-direction"] = new[] { "normal", "reverse", "alternate", "alternate-reverse" },
		["animation-fill-mode"] = new[] { "none", "forwards", "backwards", "both" },
		["mix-blend-mode"] = new[] { "normal", "multiply", "screen", "lighten", "darken" },
		["transition"] = new[] { "all 0.1s ease-out", "all 0.2s ease-in-out", "all 0.3s ease" },
		["border-radius"] = new[] { "0", "4px", "8px", "12px", "999px", "50%" },
	};

	/// <summary>The words a property takes, or null when it is typed freely.</summary>
	public static string[] WordsFor( string property ) => Words.GetValueOrDefault( property );

	/// <summary>Whether a property's value is a colour.</summary>
	public static bool IsColour( string property )
		=> property.EndsWith( "color", StringComparison.OrdinalIgnoreCase ) || string.Equals( property, "color", StringComparison.OrdinalIgnoreCase );

	/// <summary>Put the mark beside a field: a triangle that opens the words, or a swatch that opens the picker. Nothing for a property that is typed.</summary>
	public static void Attach( Panel row, string property, TextField field, Action<string> commit )
	{
		if ( IsColour( property ) )
		{
			var swatch = row.AddChild<Panel>( "swatch" );
			swatch.Tooltip = "Pick a colour";

			void Paint()
			{
				if ( Color.TryParse( field.Value ?? "", out var c ) ) swatch.Style.BackgroundColor = c;
				else swatch.Style.BackgroundColor = Color.Transparent;
			}

			Paint();
			field.ValueChanged = _ => Paint();

			swatch.AddEventListener( "onclick", () =>
			{
				var current = Color.TryParse( field.Value ?? "", out var c ) ? c : Color.White;

				Inspectors.ColorPickerPopup.Open( swatch, current, colour =>
				{
					field.Value = colour.Hex;
					Paint();
					commit( field.Value );
				}, hasAlpha: true, hdr: false );
			} );

			return;
		}

		if ( WordsFor( property ) is not { } words ) return;

		var mark = row.AddChild<UILabel>( "material-icon choose" );
		mark.Tokenize = false;
		mark.Text = "arrow_drop_down";
		mark.Tooltip = "Pick a value";

		mark.AddEventListener( "onclick", () =>
		{
			var menu = new Menu( null );

			foreach ( var word in words )
			{
				var chosen = word;
				menu.AddOption( word, null, () => { field.Value = chosen; commit( chosen ); } );
			}

			menu.OpenAtCursor();
		} );
	}
}
