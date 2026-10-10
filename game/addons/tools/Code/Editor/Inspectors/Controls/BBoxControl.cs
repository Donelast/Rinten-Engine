namespace Editor.Inspectors;

[StyleSheet( "BBoxControl.cs.scss" )]
[CustomEditor( typeof( BBox ) )]
public class BBoxControl : BaseControl
{

	public BBoxControl() 
	{
		ShowBackground = false;
		MultipleRows = true;
	}
	
	public override void Rebuild()
	{
		if ( Property is null || !Property.TryGetAsObject( out var so ) ) return;

		DeleteChildren();

		AddRow( so, "Mins" );
		AddRow( so, "Maxs" );
	}

	void AddRow( SerializedObject so, string name )
	{
		var prop = so.GetProperty( name );
		if ( prop is null ) return;

		var c = PanelControlFactory.CreateFor( prop );
		if ( c is null ) return;

		AddChild( c );
	}
}
