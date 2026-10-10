namespace Editor.MdlEditor;

using Editor.Inspectors;
using Rinten.UI;

//The condition is the plug on the node, not a value: the list exists so inputs can be added, removed and reordered, and the row only has to say what it is.
/// <summary>One input of an And node, which has nothing to edit.</summary>
[StyleSheet( "AndConditionControl.cs.scss" )]
[CustomEditor( typeof( Nodes.AndNode.Condition ) )]
public sealed class AndConditionControl : BaseControl
{
	public override bool IncludeLabel => false;

	public AndConditionControl()
	{
		ShowBackground = false;

		var label = AddChild<Rinten.UI.Label>( "name" );
		label.Text = "And";
	}
}
