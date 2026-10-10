namespace Editor.Inspectors;

/// <summary>Fallback property sheet when no dedicated <see cref="InspectorPanel"/> exists.</summary>
public class GenericInspectorPanel : InspectorPanel
{
	public GenericInspectorPanel( SerializedObject so ) : base( so )
	{
		AddChild( new ControlSheet { Target = so } );
	}
}
