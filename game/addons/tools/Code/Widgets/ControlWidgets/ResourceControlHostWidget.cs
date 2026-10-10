namespace Editor;

using Editor.Inspectors;

/// <summary>Hosts <see cref="ResourceControl"/> inside widget control sheets.</summary>
[CustomEditor( typeof( Resource ) )]
public sealed class ResourceControlHostWidget : ControlWidget
{
	public ResourceControlHostWidget( SerializedProperty property ) : base( property )
	{
		HorizontalSizeMode = SizeMode.CanGrow | SizeMode.Expand;
		MouseTracking = true;
		AcceptDrops = true;
		IsDraggable = true;

		var control = new ResourceControl { Property = property };
		var host = new PanelWidget( this, control );
		Layout = Layout.Row();
		Layout.Add( host );
	}

	public override bool IsControlButton => !IsControlDisabled;
	public override bool SupportsMultiEdit => true;
}
