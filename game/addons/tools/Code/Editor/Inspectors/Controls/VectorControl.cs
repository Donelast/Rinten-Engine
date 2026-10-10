namespace Editor.Inspectors;

[StyleSheet( "VectorControl.cs.scss" )]
[CustomEditor( typeof( Vector2 ) )]
[CustomEditor( typeof( Vector3 ) )]
[CustomEditor( typeof( Vector4 ) )]
[CustomEditor( typeof( Vector2Int ) )]
[CustomEditor( typeof( Vector3Int ) )]
public class VectorControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	public VectorControl()
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
		AddAxis( so, "x", Theme.Red );
		AddAxis( so, "y", Theme.Green );
		AddAxis( so, "z", Theme.Blue );
		AddAxis( so, "w", Theme.Yellow );
	}

	void AddAxis( SerializedObject so, string name, Color color )
	{
		var prop = so.GetProperty( name );
		if ( prop is null )
			return;

		var field = AddChild<NumberControl>();
		field.Label = prop.DisplayName;
		field.Color = color;
		field.Property = prop;
	}
}
