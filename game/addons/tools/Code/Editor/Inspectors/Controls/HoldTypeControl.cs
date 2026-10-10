namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>The hold type a weapon asks the animgraph for, typed or picked from what the citizen knows.</summary>
[StyleSheet( "TextButtonControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( HoldTypeAttribute )] )]
public sealed class HoldTypeControl : TextButtonControl
{
	//The citizen animgraph's hold types. Typing is still allowed: a graph with its own options is the reason this is a field rather than a dropdown.
	static readonly string[] BuiltIn =
	[
		"none", "pistol", "rifle", "shotgun", "holditem", "melee_punch", "melee_weapons", "rpg", "physgun"
	];

	protected override string ButtonIcon => "expand_more";

	protected override string ButtonToolTip => "Hold Types";

	protected override void OnButtonPressed()
	{
		var current = Current;
		var menu = new Menu();

		foreach ( var name in BuiltIn )
		{
			var value = name;
			var option = menu.AddOption( name, action: () => Assign( value ) );
			option.Checkable = true;
			option.Checked = value == current;
		}

		menu.OpenBelowLeft( ButtonPanel, 2.0f );
	}
}
