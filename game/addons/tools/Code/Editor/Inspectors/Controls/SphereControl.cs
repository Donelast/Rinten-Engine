namespace Editor.Inspectors;

[StyleSheet( "SphereControl.cs.scss" )]
[CustomEditor( typeof( Sphere ) )]
public class SphereControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	public SphereControl()
	{
		ShowBackground = false;
	}

	public override void Rebuild()
	{
		DeleteChildren();

		if ( Property is null )
			return;

		if ( Property.TryGetAsObject( out var so ) )
		{
			BuildFields( so );
			return;
		}

		if ( !Property.PropertyType.IsValueType )
			return;

		var boxed = Property.GetValue<object>();
		if ( boxed is null )
			return;

		var tempSo = Game.TypeLibrary.GetSerializedObject( boxed );
		if ( tempSo is null )
			return;

		StructPropertyBridge.Connect( this, tempSo );
		BuildFields( tempSo );
	}

	void BuildFields( SerializedObject so )
	{
		var radius = so.GetProperty( "Radius" );
		if ( radius is not null )
		{
			var field = AddChild<NumberControl>();
			field.AddClass( "radius" );
			field.Icon = "radio_button_checked";
			field.Property = radius;
		}

		var center = so.GetProperty( "Center" );
		if ( center is null )
			return;

		var control = PanelControlFactory.CreateFor( center );
		if ( control is null )
			return;

		AddChild( control );
	}
}
