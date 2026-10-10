namespace Editor.Inspectors;

[StyleSheet( "MarginControl.cs.scss" )]
[CustomEditor( typeof( Margin ) )]
public class MarginControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	public MarginControl()
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
		AddField( so, "Left", "border_left", Theme.Red, "Left" );
		AddField( so, "Top", "border_top", Theme.Blue, "Top" );
		AddField( so, "Right", "border_right", Theme.Green, "Right" );
		AddField( so, "Bottom", "border_bottom", Theme.Yellow, "Bottom" );
	}

	void AddField( SerializedObject so, string name, string icon, Color color, string tooltip )
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
