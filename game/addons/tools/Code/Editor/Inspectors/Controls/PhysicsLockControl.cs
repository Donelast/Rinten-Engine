namespace Editor.Inspectors;

[StyleSheet( "PhysicsLockControl.cs.scss" )]
[CustomEditor( typeof( PhysicsLock ) )]
public class PhysicsLockControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	public PhysicsLockControl()
	{
		ShowBackground = false;
		MultipleRows = true;
	}

	public override void Rebuild()
	{
		if ( Property is null || !Property.TryGetAsObject( out var so ) )
			return;

		DeleteChildren();

		var linear = AddChild<Rinten.UI.Panel>( "linear" );
		AddField( linear, so, "X" );
		AddField( linear, so, "Y" );
		AddField( linear, so, "Z" );

		var angular = AddChild<Rinten.UI.Panel>( "angular" );
		AddField( angular, so, "Pitch" );
		AddField( angular, so, "Yaw" );
		AddField( angular, so, "Roll" );
	}

	static void AddField( Rinten.UI.Panel row, SerializedObject so, string name )
	{
		var prop = so.GetProperty( name );
		if ( prop is null )
			return;

		var field = row.AddChild<Rinten.UI.Panel>( "field" );

		var control = field.AddChild<BoolControl>();
		control.Property = prop;

		var label = field.AddChild<Rinten.UI.Label>( "label" );
		label.Tokenize = false;
		label.Text = prop.DisplayName;
	}
}
