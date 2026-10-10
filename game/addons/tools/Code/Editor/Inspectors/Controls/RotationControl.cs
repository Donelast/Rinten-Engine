namespace Editor.Inspectors;

/// <summary>Inspector editor for a rotation, edited as pitch, yaw and roll.</summary>
[StyleSheet( "RotationControl.cs.scss" )]
[CustomEditor( typeof( Rotation ) )]
public class RotationControl : BaseControl
{
	//The angles last written here, kept while they still mean the rotation the property holds.
	Angles? _edited;

	//Kept once so the angle fields and the edit hooks share one object, or undo scopes never open
	SerializedObject _self;

	/// <summary>The rotation as three angles, which is what the fields below edit.</summary>
	public Angles EditorAngles
	{
		get
		{
			if ( Property is null )
				return default;

			var rotation = Property.GetValue<Rotation>();

			if ( _edited is { } edited && edited.ToRotation() == rotation )
				return edited;

			return rotation.Angles();
		}
		set
		{
			if ( Property is null )
				return;

			_edited = value;

			Property.SetValue( value.ToRotation() );
		}
	}

	public override bool SupportsMultiEdit => true;

	public RotationControl()
	{
		ShowBackground = false;
	}

	public override void Rebuild()
	{
		DeleteChildren();

		_edited = null;

		if ( Property is null )
			return;

		var anglesProperty = SelfObject?.GetProperty( nameof( EditorAngles ) );
		if ( anglesProperty is null )
			return;

		var angles = AddChild<AnglesControl>();
		angles.Property = anglesProperty;
	}

	SerializedObject SelfObject
	{
		get
		{
			if ( _self is not null )
				return _self;

			_self = EditorTypeLibrary.GetSerializedObject( this );
			if ( _self is null )
				return null;

			//Whatever brackets an edit of one angle brackets the edit of the rotation that angle is
			//part of - otherwise a drag across a handle is one thing to undo for every frame it moved.
			_self.OnPropertyStartEdit += _ => Property?.Parent?.NoteStartEdit( Property );
			_self.OnPropertyFinishEdit += _ => Property?.Parent?.NoteFinishEdit( Property );

			return _self;
		}
	}
}
