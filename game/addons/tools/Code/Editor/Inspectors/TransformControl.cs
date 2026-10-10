namespace Editor.Inspectors;

[StyleSheet( "TransformControl.cs.scss" )]
[CustomEditor( typeof( Transform ) )]
public class TransformControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	Panel body;

	public TransformControl()
	{
		AddClass( "no-background" );
		AddClass( "multiple-rows" );

		body = AddChild<Panel>( "body" );
	}

	public override void Rebuild()
	{
		body.DeleteChildren();
		if ( Property is null || !Property.TryGetAsObject( out var so ) ) return;

		AddRow( so.GetProperty( "Position" ) );
		AddRow( so.GetProperty( "Rotation" ) );
		AddRow( so.GetProperty( "Scale" ) );
	}

	void AddRow( SerializedProperty prop )
	{
		if ( prop is null ) return;
		var row = body.AddChild<ControlSheetRow>();
		row.Initialize( prop );
	}
}
