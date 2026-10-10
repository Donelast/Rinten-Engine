using System.Text.Json;

namespace Editor.Inspectors;

[StyleSheet( "MissingComponentPanel.cs.scss" )]
[CustomEditor( typeof( MissingComponent ) )]
public class MissingComponentPanel : ComponentPanel
{
	public MissingComponentPanel( SerializedObject obj )
	{
		TargetObject = obj;
		AddClass( "missing-component-panel" );

		var c = obj.Targets.OfType<MissingComponent>().FirstOrDefault();
		if ( c is null ) return;

		var label = AddChild<Rinten.UI.Label>( "json" );
		label.Text = c.GetJson().ToJsonString( new JsonSerializerOptions { WriteIndented = true } );
	}
}
