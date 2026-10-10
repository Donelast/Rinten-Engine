namespace Editor.Inspectors;

using Rinten.UI;

[StyleSheet( "NormalControl.cs.scss" )]
[CustomEditor( typeof( Vector2 ), WithAllAttributes = [typeof( NormalAttribute )] )]
[CustomEditor( typeof( Vector3 ), WithAllAttributes = [typeof( NormalAttribute )] )]
[CustomEditor( typeof( Vector4 ), WithAllAttributes = [typeof( NormalAttribute )] )]
public sealed class NormalControl : BaseControl
{
	VectorControl vector;
	ToolbarButton button;
	Popup normalPopup;

	public override bool SupportsMultiEdit => true;

	public NormalControl()
	{
		ShowBackground = false;

		vector = AddChild<VectorControl>();

		button = AddChild<ToolbarButton>();
		button.Icon = "north_east";
		button.ToolTip = "Choose normal";
		button.OnPressed = TogglePopup;
	}

	public override void Rebuild()
	{
		if ( vector is not null )
			vector.Property = Property;

		if ( button is not null )
			button.Disabled = IsReadOnly;
	}

	public override void Tick()
	{
		base.Tick();

		if ( normalPopup is not null && !normalPopup.IsValid )
			normalPopup = null;
	}

	protected override void OnReadOnlyChanged()
	{
		if ( button is not null )
			button.Disabled = IsReadOnly;
	}

	void TogglePopup()
	{
		if ( Property is null || IsReadOnly ) return;

		if ( normalPopup is { IsValid: true } )
		{
			normalPopup.Delete();
			normalPopup = null;
			return;
		}

		var popup = new Popup();
		normalPopup = popup;
		popup.SetPositioning( button, Popup.PositionMode.BelowRight, 4 );
		popup.CloseWhenParentIsHidden = true;
		PopulatePopup( popup );
		popup.SyncAnchor();
	}

	void PopulatePopup( Popup popup )
	{
		//The letters are the basis this engine works in now - X right, Y up, Z back - and the vectors come off
		//Vector3 rather than being written out here, so a name and the direction behind it cannot drift apart again.
		if ( Property?.PropertyType == typeof( Vector2 ) )
		{
			popup.AddOption( "Right (+X)", () => SetNormal( Vector2.Right ) );
			popup.AddOption( "Left (-X)", () => SetNormal( Vector2.Left ) );
			popup.AddOption( "Up (+Y)", () => SetNormal( Vector2.Up ) );
			popup.AddOption( "Down (-Y)", () => SetNormal( Vector2.Down ) );
			return;
		}

		if ( Property?.PropertyType == typeof( Vector4 ) )
		{
			popup.AddOption( "Forward (-Z)", () => SetNormal( new Vector4( Vector3.Forward ) ) );
			popup.AddOption( "Backward (+Z)", () => SetNormal( new Vector4( Vector3.Backward ) ) );
			popup.AddOption( "Right (+X)", () => SetNormal( new Vector4( Vector3.Right ) ) );
			popup.AddOption( "Left (-X)", () => SetNormal( new Vector4( Vector3.Left ) ) );
			popup.AddOption( "Up (+Y)", () => SetNormal( new Vector4( Vector3.Up ) ) );
			popup.AddOption( "Down (-Y)", () => SetNormal( new Vector4( Vector3.Down ) ) );
			popup.AddOption( "+W", () => SetNormal( new Vector4( 0, 0, 0, 1 ) ) );
			popup.AddOption( "-W", () => SetNormal( new Vector4( 0, 0, 0, -1 ) ) );
			return;
		}

		popup.AddOption( "Forward (-Z)", () => SetNormal( Vector3.Forward ) );
		popup.AddOption( "Backward (+Z)", () => SetNormal( Vector3.Backward ) );
		popup.AddOption( "Right (+X)", () => SetNormal( Vector3.Right ) );
		popup.AddOption( "Left (-X)", () => SetNormal( Vector3.Left ) );
		popup.AddOption( "Up (+Y)", () => SetNormal( Vector3.Up ) );
		popup.AddOption( "Down (-Y)", () => SetNormal( Vector3.Down ) );
	}

	void SetNormal( Vector2 v ) => Property?.SetValue( v.Normal );

	void SetNormal( Vector3 v ) => Property?.SetValue( v.Normal );

	void SetNormal( Vector4 v ) => Property?.SetValue( v.IsNearZeroLength ? Vector4.Zero : v / v.Length );
}
