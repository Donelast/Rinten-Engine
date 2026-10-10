namespace Editor.Inspectors;

[StyleSheet( "AnglesControl.cs.scss" )]
[CustomEditor( typeof( Angles ) )]
public class AnglesControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	public AnglesControl()
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

		if ( Property.PropertyType != typeof( Angles ) )
			return;

		var angles = Property.GetValue<Angles>();
		var anglesSo = Game.TypeLibrary.GetSerializedObject( angles );
		if ( anglesSo is null )
			return;

		WireAnglesSo( anglesSo );
		BuildFields( anglesSo );
	}

	void BuildFields( SerializedObject so )
	{
		AddAxis( so, "pitch", "height", Theme.Red, "Pitch" );
		AddAxis( so, "yaw", "360", Theme.Green, "Yaw" );
		AddAxis( so, "roll", "sync", Theme.Blue, "Roll" );
	}

	void WireAnglesSo( SerializedObject anglesSo )
	{
		StructPropertyBridge.Connect( this, anglesSo );
	}

	void AddAxis( SerializedObject so, string name, string icon, Color color, string tooltip )
	{
		var prop = so.GetProperty( name );
		if ( prop is null )
			return;

		var field = AddChild<NumberControl>();
		field.Icon = icon;
		field.Color = color;
		field.HandleTooltip = tooltip;
		field.Property = prop;
	}
}
