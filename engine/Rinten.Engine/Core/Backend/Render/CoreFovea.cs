namespace Rinten;

//Foveation centre and strength each frame, for the device's shading-rate map and the cluster cull
internal static class CoreFovea
{
	//One frame's foveation for a drawer: how strongly, and the gaze in the world - a unit direction
	//- or none where the eyes are not followed and each eye's own middle stands in for it.
	internal readonly record struct Setting( float Strength, Vector3? Gaze )
	{
		public static readonly Setting Off = new( 0.0f, null );

		public bool On => Strength > 0.0f;
	}

	//This frame's setting for a headset's eyes, from the gaze the headset gave and
	//the anchor the eyes were put in the world by. Off where the game asked for none.
	internal static Setting ForHeadset( in Transform anchor )
	{
		var strength = Rinten.VR.VRSystem.Foveation;
		if ( strength <= 0.0f ) return Setting.Off;

		if ( !CoreHeadset.Gaze.Tracked ) return new Setting( strength, null );

		var world = anchor.ToWorld( CoreHeadset.Gaze.Pose );
		return new Setting( strength, world.Rotation.Forward.Normal );
	}

	//Where a direction lands in an eye's picture, nought to one across and down - the middle of the field it looks along where
	//there is no direction. Clamped to the picture: a gaze past the edge of one eye's field is foveated at its edge, not off it.
	internal static Vector2 CentreIn( in XrEyeView eye, Vector3? gaze )
	{
		var tanLeft = MathF.Tan( eye.AngleLeft );
		var tanRight = MathF.Tan( eye.AngleRight );
		var tanUp = MathF.Tan( eye.AngleUp );
		var tanDown = MathF.Tan( eye.AngleDown );

		//The eye's own axis: tan nought each way, which is not the middle of an off-centre field - it is where the lens is.
		var across = 0.0f;
		var up = 0.0f;

		if ( gaze is { } direction )
		{
			var forward = Vector3.Dot( direction, eye.Rotation.Forward );

			if ( forward > 0.05f )
			{
				across = -Vector3.Dot( direction, eye.Rotation.Left ) / forward;
				up = Vector3.Dot( direction, eye.Rotation.Up ) / forward;
			}
		}

		var u = ( across - tanLeft ) / MathF.Max( tanRight - tanLeft, 0.0001f );
		var v = ( tanUp - up ) / MathF.Max( tanUp - tanDown, 0.0001f );

		return new Vector2( Math.Clamp( u, 0.0f, 1.0f ), Math.Clamp( v, 0.0f, 1.0f ) );
	}

	//The map said to an eye's view: each layer's middle, the first eye's in the first layer and
	//the second's - where the view draws both - in the second. Nothing where the device has no map.
	internal static void Place( ulong view, in Setting setting, XrEyeView? first, XrEyeView? second )
	{
		if ( view == 0 || first is not { } one || !CoreRenderDevice.Foveates ) return;

		var a = CentreIn( one, setting.Gaze );
		var b = second is { } two ? CentreIn( two, setting.Gaze ) : a;

		CoreRenderDevice.SetViewFovea( view, setting.Strength, setting.Gaze is not null, a, b );
	}

	//The camera's words for the cull: the gaze, or the field's own forward where there is none, and the strength. Nought - no foveation - where it is off.
	internal static System.Numerics.Vector4 ForCull( in Setting setting, XrEyeView? field )
	{
		if ( !setting.On || field is not { } eye ) return default;

		var direction = setting.Gaze ?? eye.Rotation.Forward.Normal;

		//A gaze that is not followed foveates less: the middle of the lens is only roughly where the eye is, so less is taken away from around it.
		var strength = setting.Gaze is null ? setting.Strength * 0.5f : setting.Strength;

		return new System.Numerics.Vector4( direction.x, direction.y, direction.z, strength );
	}
}
