namespace Editor.Inspectors;

/// <summary>Sound event preview: reach circle by Distance/Falloff; click to play there, drag to move it</summary>
[StyleSheet( "SoundRadiusPanel.cs.scss" )]
public sealed class SoundRadiusPanel : Rinten.UI.Panel
{
	//How far in from the panel's edge the circle stops, in style pixels.
	const float Inset = 6f;

	//How many rings the falloff is shaded in.
	const int Rings = 16;

	readonly SoundEvent _sound;
	readonly FieldPanel _field;
	readonly Rinten.UI.Label _hint;
	readonly Rinten.UI.Label _radius;

	readonly List<Playing> _playing = new();
	Playing _dragging;

	//What was drawn last, so an unchanged panel is not drawn again.
	int _drawnHash;

	sealed class Playing
	{
		public SoundHandle Handle;
		public Vector3 Position;
	}

	public SoundRadiusPanel( SoundEvent sound )
	{
		_sound = sound;

		AddClass( "sound-radius column" );

		var bar = AddChild<Rinten.UI.Panel>( "bar" );

		var title = bar.AddChild<Rinten.UI.Label>( "title" );
		title.Text = "Preview";
		title.Tokenize = false;

		_hint = bar.AddChild<Rinten.UI.Label>( "hint" );
		_hint.Tokenize = false;

		bar.AddChild( new Rinten.UI.Button( null, "stop", StopAll ) ).AddClass( "stop" );

		_field = AddChild( new FieldPanel( this ) );

		_radius = _field.AddChild<Rinten.UI.Label>( "radius" );
		_radius.Tokenize = false;

		UpdateText();

		//Picking a sound is asking to hear it, unless the preview was told not to - the same switch a sound file's page answers to.
		if ( EditorCookie.Get( "SoundPreview.AutoPlay", true ) )
			Play( Vector3.Zero );
	}

	//Where the event is heard: nowhere past its distance, or everywhere when it is 2D.
	bool Spatial => !_sound.UI && _sound.DistanceAttenuation;

	float Distance => MathF.Max( _sound.Distance, 0.01f );

	//The circle in the field's own pixels: its centre and its radius.
	(Vector2 Centre, float Radius) Circle()
	{
		var size = _field.Box.Rect.Size;
		var radius = MathF.Max( MathF.Min( size.x, size.y ) * 0.5f - Inset * ScaleToScreen, 1f );

		return (size * 0.5f, radius);
	}

	//A point in the field to where it is around you: right is +X, up the panel is in front, -Z.
	Vector3 ToWorld( Vector2 local )
	{
		var (centre, radius) = Circle();
		var d = (local - centre) / radius * Distance;

		return new Vector3( d.x, 0, d.y );
	}

	Vector2 ToField( Vector3 world )
	{
		var (centre, radius) = Circle();

		return centre + new Vector2( world.x, world.z ) / Distance * radius;
	}

	//Whether there is anything in the event to play - a new one has no files yet.
	bool HasSounds => _sound.Sounds?.Any( x => x is not null ) ?? false;

	void Play( Vector3 position )
	{
		if ( !HasSounds ) return;

		var handle = Sound.Play( _sound, position );
		if ( handle is null )
		{
			Log.Warning( $"Couldn't play {_sound.ResourcePath}" );
			return;
		}

		//Around you rather than around a scene: the editor has no listener except over a view the mouse is on.
		handle.ListenLocal = true;

		_dragging = new Playing { Handle = handle, Position = position };
		_playing.Add( _dragging );
	}

	void StopAll()
	{
		foreach ( var playing in _playing )
			playing.Handle?.Stop();

		_playing.Clear();
		_dragging = null;
	}

	void Press( Vector2 local ) => Play( ToWorld( local ) );

	void Drag( Vector2 local )
	{
		if ( _dragging is null ) return;

		var position = ToWorld( local );

		_dragging.Position = position;
		_dragging.Handle.Position = position;
	}

	void Release() => _dragging = null;

	public override void Tick()
	{
		base.Tick();

		_playing.RemoveAll( x => x.Handle is null || !x.Handle.IsPlaying );

		//Let go of outside the field, where its own mouse up never comes.
		if ( _dragging is not null && ( !_playing.Contains( _dragging ) || !Application.MouseButtons.HasFlag( MouseButtons.Left ) ) )
			_dragging = null;

		UpdateText();

		var hash = HashCode.Combine( _sound.Distance, _sound.UI, _sound.DistanceAttenuation, FalloffHash(), _field.Box.Rect.Size, _playing.Count );
		foreach ( var playing in _playing ) hash = HashCode.Combine( hash, playing.Position );

		if ( hash == _drawnHash ) return;

		_drawnHash = hash;
		_field.MarkRenderDirty();
	}

	int FalloffHash()
	{
		var hash = 0;
		foreach ( var frame in _sound.Falloff.Frames ) hash = HashCode.Combine( hash, frame.Time, frame.Value, frame.In, frame.Out );
		return hash;
	}

	void UpdateText()
	{
		_radius.Text = Spatial ? $"{_sound.Distance:0.#} m" : "";

		_hint.Text = !HasSounds ? "No sound files in it to play"
			: _dragging is not null && Spatial ? $"{_dragging.Position.Length:0.#} m away"
			: Spatial ? "Click to hear it from there"
			: "2D - heard the same everywhere";
	}

	//The circle, the falloff shaded across it, you, and each sound playing.
	void DrawField( Vector2 origin )
	{
		var canvas = new CurveDraw.Canvas( origin );
		var (centre, radius) = Circle();
		var scale = ScaleToScreen;

		var blue = Theme.Blue;
		var green = Theme.Green;

		if ( Spatial )
		{
			//Loudest in the middle: each ring as bright as the falloff at its distance.
			var band = radius / Rings;
			for ( var i = 0; i < Rings; i++ )
			{
				var f = ( i + 0.5f ) / Rings;
				CurveDraw.StrokeCircle( canvas, centre, band * ( i + 0.5f ), blue.WithAlpha( ( 0.01f + _sound.Falloff.Evaluate( f ) ) * 0.5f ), band + 1f );
			}

			CurveDraw.StrokeCircle( canvas, centre, radius, blue.WithAlpha( 0.4f ), 1f );

			//Up to the edge, which the distance label sits beside.
			CurveDraw.Line( canvas, centre, centre - new Vector2( 0, radius ), blue.WithAlpha( 0.4f ), 1f );
		}

		foreach ( var playing in _playing )
		{
			var at = ToField( playing.Position );

			CurveDraw.Line( canvas, at, centre, green.WithAlpha( playing == _dragging ? 0.9f : 0.5f ), 2f * scale );
			CurveDraw.FillCircle( canvas, at, 5f * scale, green );
		}

		//You, half a metre across, and never smaller than can be seen.
		var you = MathF.Max( 0.25f / Distance * radius, 4f * scale );
		CurveDraw.FillCircle( canvas, centre, you, Color.White.WithAlpha( 0.3f ) );
		CurveDraw.StrokeCircle( canvas, centre, you, Color.White.WithAlpha( 0.8f ), 2f * scale );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		StopAll();
	}

	//The square the circle is drawn in, which takes the presses.
	sealed class FieldPanel : Rinten.UI.Panel
	{
		readonly SoundRadiusPanel _owner;

		public FieldPanel( SoundRadiusPanel owner )
		{
			_owner = owner;

			AddClass( "field" );

			AddEventListener( "onmousedown", e =>
			{
				if ( e is not MousePanelEvent mouse || mouse.MouseButton != MouseButtons.Left ) return;

				_owner.Press( ScreenPositionToPanelPosition( mouse.PointerPosition ) );
				e.StopPropagation();
			} );

			AddEventListener( "onmousemove", e =>
			{
				if ( e is MousePanelEvent mouse ) _owner.Drag( ScreenPositionToPanelPosition( mouse.PointerPosition ) );
			} );

			AddEventListener( "onmouseup", _ => _owner.Release() );
		}

		//The owner says when the picture changes - see its Tick - so what was drawn is kept until then.
		protected override bool RetainsDrawing => true;

		public override void OnDraw() => _owner.DrawField( Box.Rect.Position );
	}
}
