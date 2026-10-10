namespace Editor.Inspectors;

//The card that follows the handle under the pointer: its time and value, and what the curve does either side.
[StyleSheet( "CurveHandlePopup.cs.scss" )]
sealed class CurveHandlePopup : Panel
{
	CurveTrack track;
	CurveTrack.Handle handle;

	readonly NumberControl timeControl;
	readonly NumberControl valueControl;
	readonly Panel modeRow;
	readonly List<(Curve.HandleMode Mode, ToolbarButton Button)> modeButtons = new();
	readonly SerializedObject serialized;

	public CurveHandlePopup()
	{

		timeControl = AddChild<NumberControl>( "field" );
		timeControl.Icon = "east";
		timeControl.Color = Theme.Red;
		timeControl.ShowHandle = true;

		valueControl = AddChild<NumberControl>( "field" );
		valueControl.Icon = "north";
		valueControl.Color = Theme.Green;
		valueControl.ShowHandle = true;

		modeRow = AddChild<Panel>( "modes" );

		foreach ( var (value, info) in DisplayInfo.ForEnumValues<Curve.HandleMode>() )
		{
			var button = modeRow.AddChild<ToolbarButton>();
			button.Icon = info.Icon;
			button.ToolTip = $"{info.Name} - {info.Description}";
			button.OnPressed = () => SetMode( value );

			modeButtons.Add( (value, button) );
		}

		serialized = EditorTypeLibrary.GetSerializedObject( this );

		timeControl.Property = serialized.GetProperty( nameof( HandleTime ) );
		valueControl.Property = serialized.GetProperty( nameof( HandleValue ) );
	}

	/// <summary>The handle's time, in the curve's own units.</summary>
	[Title( "Time" )]
	public float HandleTime
	{
		get => track is null || handle is null ? 0 : track.GetHandleTime( handle );
		set
		{
			if ( track is null || handle is null ) return;

			track.SetHandleValue( handle, value, track.GetHandleValue( handle ) );
		}
	}

	/// <summary>The handle's value, in the curve's own units.</summary>
	[Title( "Value" )]
	public float HandleValue
	{
		get => track is null || handle is null ? 0 : track.GetHandleValue( handle );
		set
		{
			if ( track is null || handle is null ) return;

			track.SetHandleValue( handle, track.GetHandleTime( handle ), value );
		}
	}

	/// <summary>Points the card at a handle, above it - or below where above is off the chart.</summary>
	public void UpdateFrom( CurveTrack track, CurveTrack.Handle handle, Vector2 anchor, Rect bounds )
	{
		this.track = track;
		this.handle = handle;

		foreach ( var (mode, button) in modeButtons )
		{
			button.Active = handle is not null && handle.Frame.Mode == mode;
		}

		var scale = ScaleFromScreen;
		var size = Box.Rect.Size;

		//Nothing is measured on the first frame; a guess beats a jump.
		if ( size.x <= 1 ) size = new Vector2( 148, 96 ) / MathF.Max( scale, 0.0001f );

		var above = anchor.y - size.y - 14;
		var pointingUp = above < 0;

		var top = pointingUp ? anchor.y + 14 : above;
		var left = anchor.x - (size.x * 0.5f);

		left = left.Clamp( bounds.Left + 4, MathF.Max( bounds.Right - size.x - 4, bounds.Left + 4 ) );
		top = top.Clamp( bounds.Top + 4, MathF.Max( bounds.Bottom - size.y - 4, bounds.Top + 4 ) );

		SetClass( "pointing-up", pointingUp );

		Style.Left = Length.Pixels( left * scale );
		Style.Top = Length.Pixels( top * scale );
	}

	void SetMode( Curve.HandleMode mode )
	{
		if ( track is null || handle is null ) return;

		track.SetHandleMode( handle, mode );

		foreach ( var (value, button) in modeButtons )
		{
			button.Active = value == mode;
		}

		StateHasChanged();
	}

	public override void Tick()
	{
		base.Tick();

		//The handle can be deleted while the card is still up.
		if ( track is not null && handle is not null && !track.Handles.Contains( handle ) )
		{
			handle = null;
			SetClass( "hidden", true );
		}
	}
}
