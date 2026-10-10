namespace Editor.UiEditor;

//What a new panel starts as: a razor and a sheet for each of a few shapes a screen usually takes. {name} is the type's
//name, {element} its element name - the type's name in lower case, which is what the engine calls the root panel of a razor.
internal static class PanelTemplates
{
	public sealed record Template( string Name, string Icon, string What, string Razor, string Scss );

	const string Header =
		"@using Rinten;\n" +
		"@using Rinten.UI;\n" +
		"@inherits {base}\n" +
		"\n";

	const string Code = "\n@code\n{\n}\n";

	public static readonly Template[] All =
	{
		new( "Empty", "crop_square", "A root and a sheet that fills the screen",
			Header + "<root>\n</root>\n" + Code,
			"{element}\n{\n\tposition: absolute;\n\tleft: 0;\n\ttop: 0;\n\tright: 0;\n\tbottom: 0;\n}\n" ),

		new( "HUD", "dashboard", "Fills the screen; a panel pinned in each corner",
			Header +
			"<root>\n" +
			"\t<div class=\"corner top-left\">Top left</div>\n" +
			"\t<div class=\"corner top-right\">Top right</div>\n" +
			"\t<div class=\"corner bottom-left\">Bottom left</div>\n" +
			"\t<div class=\"corner bottom-right\">Bottom right</div>\n" +
			"</root>\n" + Code,
			"{element}\n{\n\tposition: absolute;\n\tleft: 0;\n\ttop: 0;\n\tright: 0;\n\tbottom: 0;\n\tpointer-events: none;\n\tfont-family: Inter;\n\tcolor: white;\n" +
			"\n\t.corner\n\t{\n\t\tposition: absolute;\n\t\tpadding: 8px 12px;\n\t\tborder-radius: 6px;\n\t\tbackground-color: rgba( black, 0.4 );\n\t\tfont-size: 14px;\n" +
			"\n\t\t&.top-left { top: 24px; left: 24px; }\n\t\t&.top-right { top: 24px; right: 24px; }\n\t\t&.bottom-left { bottom: 24px; left: 24px; }\n\t\t&.bottom-right { bottom: 24px; right: 24px; }\n\t}\n}\n" ),

		new( "Menu", "menu", "A title and a column of buttons in the middle",
			Header +
			"<root>\n" +
			"\t<div class=\"card\">\n" +
			"\t\t<div class=\"title\">{name}</div>\n" +
			"\t\t<div class=\"button\">Play</div>\n" +
			"\t\t<div class=\"button\">Settings</div>\n" +
			"\t\t<div class=\"button\">Quit</div>\n" +
			"\t</div>\n" +
			"</root>\n" + Code,
			"{element}\n{\n\tposition: absolute;\n\tleft: 0;\n\ttop: 0;\n\tright: 0;\n\tbottom: 0;\n\talign-items: center;\n\tjustify-content: center;\n\tfont-family: Inter;\n\tcolor: white;\n\tbackground-color: rgba( black, 0.6 );\n" +
			"\n\t.card\n\t{\n\t\tflex-direction: column;\n\t\tgap: 8px;\n\t\twidth: 320px;\n\t\tpadding: 24px;\n\t\tborder-radius: 12px;\n\t\tbackground-color: #1b1f27;\n\t}\n" +
			"\n\t.title\n\t{\n\t\tfont-size: 28px;\n\t\tfont-weight: bold;\n\t\tmargin-bottom: 16px;\n\t}\n" +
			"\n\t.button\n\t{\n\t\tpadding: 10px 16px;\n\t\tborder-radius: 6px;\n\t\tbackground-color: rgba( white, 0.08 );\n\t\tcursor: pointer;\n\t\ttransition: background-color 0.1s ease;\n" +
			"\n\t\t&:hover { background-color: rgba( white, 0.16 ); }\n\t\t&:active { background-color: rgba( white, 0.24 ); }\n\t}\n}\n" ),

		new( "Dialog", "web_asset", "A dimmed screen with a card: a message and two buttons",
			Header +
			"<root>\n" +
			"\t<div class=\"card\">\n" +
			"\t\t<div class=\"title\">Are you sure?</div>\n" +
			"\t\t<div class=\"message\">This cannot be undone.</div>\n" +
			"\t\t<div class=\"actions\">\n" +
			"\t\t\t<div class=\"button\">Cancel</div>\n" +
			"\t\t\t<div class=\"button primary\">Yes</div>\n" +
			"\t\t</div>\n" +
			"\t</div>\n" +
			"</root>\n" + Code,
			"{element}\n{\n\tposition: absolute;\n\tleft: 0;\n\ttop: 0;\n\tright: 0;\n\tbottom: 0;\n\talign-items: center;\n\tjustify-content: center;\n\tfont-family: Inter;\n\tcolor: white;\n\tbackground-color: rgba( black, 0.5 );\n" +
			"\n\t.card\n\t{\n\t\tflex-direction: column;\n\t\tgap: 12px;\n\t\twidth: 380px;\n\t\tpadding: 20px;\n\t\tborder-radius: 10px;\n\t\tbackground-color: #1b1f27;\n\t\tborder: 1px solid rgba( white, 0.1 );\n\t}\n" +
			"\n\t.title { font-size: 20px; font-weight: bold; }\n\t.message { font-size: 14px; color: rgba( white, 0.7 ); }\n" +
			"\n\t.actions\n\t{\n\t\tflex-direction: row;\n\t\tjustify-content: flex-end;\n\t\tgap: 8px;\n\t\tmargin-top: 8px;\n\t}\n" +
			"\n\t.button\n\t{\n\t\tpadding: 8px 16px;\n\t\tborder-radius: 6px;\n\t\tbackground-color: rgba( white, 0.08 );\n\t\tcursor: pointer;\n" +
			"\n\t\t&:hover { background-color: rgba( white, 0.16 ); }\n\t\t&.primary { background-color: #4c8dff; }\n\t\t&.primary:hover { background-color: #6aa0ff; }\n\t}\n}\n" ),

		new( "List", "list", "A column of rows from a list in the code, one per item",
			"@using Rinten;\n" +
			"@using Rinten.UI;\n" +
			"@using System.Collections.Generic;\n" +
			"@inherits {base}\n" +
			"\n" +
			"<root>\n" +
			"\t<div class=\"list\">\n" +
			"\t\t@foreach ( var item in Items )\n" +
			"\t\t{\n" +
			"\t\t\t<div class=\"row\">@item</div>\n" +
			"\t\t}\n" +
			"\t</div>\n" +
			"</root>\n" +
			"\n@code\n{\n" +
			"\t/// <summary>What the rows show; whoever uses the panel fills it.</summary>\n" +
			"\tpublic List<string> Items { get; set; } = new() { \"First\", \"Second\", \"Third\" };\n" +
			"\n\tprotected override int BuildHash() => System.HashCode.Combine( Items?.Count );\n" +
			"}\n",
			"{element}\n{\n\tposition: absolute;\n\tleft: 0;\n\ttop: 0;\n\tright: 0;\n\tbottom: 0;\n\tfont-family: Inter;\n\tcolor: white;\n" +
			"\n\t.list\n\t{\n\t\tflex-direction: column;\n\t\tgap: 4px;\n\t\twidth: 320px;\n\t\tmargin: 24px;\n\t}\n" +
			"\n\t.row\n\t{\n\t\tpadding: 8px 12px;\n\t\tborder-radius: 4px;\n\t\tbackground-color: rgba( white, 0.06 );\n" +
			"\n\t\t&:hover { background-color: rgba( white, 0.12 ); }\n\t}\n}\n" ),
	};

	/// <summary>The two files of a template, with the names filled in.</summary>
	public static (string razor, string scss) Fill( Template template, string name, string baseType )
	{
		var element = name.ToLowerInvariant();

		return (
			template.Razor.Replace( "{base}", baseType ).Replace( "{name}", name ).Replace( "{element}", element ),
			template.Scss.Replace( "{name}", name ).Replace( "{element}", element ) );
	}
}
