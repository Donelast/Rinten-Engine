namespace Editor.Inspectors;

[StyleSheet( "RectControl.cs.scss" )]
[CustomEditor( typeof( Rect ) )]
[CustomEditor( typeof( RectInt ) )]
public class RectControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	public RectControl()
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
		AddField( so, "Left", "X", Theme.Red );
		AddField( so, "Top", "Y", Theme.Blue );
		AddField( so, "Width", "W", Theme.Green );
		AddField( so, "Height", "H", Theme.Yellow );
	}

	void AddField( SerializedObject so, string name, string label, Color color )
	{
		var prop = so.GetProperty( name );
		if ( prop is null )
			return;

		var field = AddChild<NumberControl>();
		field.Label = label;
		field.Color = color;
		field.Property = prop;
	}
}
