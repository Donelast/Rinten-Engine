namespace Rinten;

//Resolves upscaler settings for this device and frame: which runs, render size, jitter source
//The single source both render sizing and CoreUpscaler ask, so they never disagree
internal static class CoreUpscale
{
	internal enum Kind
	{
		Native = 0,
		Stretch = 1,
		Fsr1 = 2,
		Fsr3 = 3,
		Dlss = 4,
		Temporal = 5,
	}

	//What the settings ask for, before anything is known about the device.
	internal static Kind Asked => (Kind)Math.Clamp( Rinten.Rendering.DrawSettings.Upscaler, 0, 5 );

	//Whether this upscaler draws each frame from somewhere else and gathers them.
	internal static bool IsTemporal( Kind kind ) => kind is Kind.Fsr3 or Kind.Dlss or Kind.Temporal;

	//The upscaler that actually runs: the requested one, else the nearest fallback, logged once
	internal static Kind Effective( bool headset )
	{
		var asked = Asked;

		if ( !IsTemporal( asked ) ) return asked;

		if ( headset || !CoreNative.Available || CoreRenderDevice.PortableFrame ) return Kind.Native;

		var got = asked;

		if ( got == Kind.Dlss && !CoreDlss.Available ) got = Kind.Fsr3;
		if ( got == Kind.Fsr3 && !CoreFsr3.Available ) got = Kind.Temporal;

		if ( got != asked && saidFallback != asked )
		{
			saidFallback = asked;
			Log.Info( $"upscaler {asked} is not available here ({( asked == Kind.Dlss ? CoreDlss.WhyNot : CoreFsr3.WhyNot )}) - {got} instead" );
		}

		return got;
	}

	private static Kind saidFallback = Kind.Native;

	//Whether a setting on the settings screen would do what it says on this device.
	internal static bool Supported( Engine.Settings.UpscalerMode mode ) => (Kind)(int)mode switch
	{
		Kind.Dlss => CoreDlss.Available,
		Kind.Fsr3 => CoreFsr3.Available,
		Kind.Temporal => !CoreRenderDevice.PortableFrame,
		_ => true,
	};

	//How much of the window's resolution the world is drawn at under this upscaler - one where it draws natively.
	internal static float RenderScale( Kind kind ) => kind switch
	{
		Kind.Stretch or Kind.Fsr1 => Rinten.Rendering.DrawSettings.UpscalerScale,
		Kind.Fsr3 => PresetScale( Rinten.Rendering.DrawSettings.Fsr3Quality ),
		Kind.Dlss => PresetScale( Rinten.Rendering.DrawSettings.DlssQuality ),
		Kind.Temporal => Rinten.Rendering.DrawSettings.TemporalScale,
		_ => 1.0f,
	};

	//The scale a preset names - the same five numbers FSR and DLSS both publish: ultra
	//performance a third, performance a half, balanced 1/1.7, quality 1/1.5, and native.
	internal static float PresetScale( int preset ) => preset switch
	{
		0 => 1.0f / 3.0f,
		1 => 0.5f,
		2 => 1.0f / 1.7f,
		3 => 1.0f / 1.5f,
		_ => 1.0f,
	};

	//The render scale a window's world is drawn at for the settings as they are now. A headset is not asked here - see VRSystem.RenderScale.
	internal static float WindowScale => RenderScale( Effective( headset: false ) );

	//How many different places a run of jitter visits before it repeats: eight at native, and more the more the world is
	//upscaled, so every output pixel is covered by several samples. FSR's own rule, which DLSS's guide asks for in the same words.
	internal static int PhaseCount( int renderWidth, int displayWidth )
	{
		if ( renderWidth <= 0 || displayWidth <= 0 ) return 8;

		//Truncated, as FSR's own ffxFsr3UpscalerGetJitterPhaseCount does - its passes read the count they were told and the sequence has to match it.
		var ratio = displayWidth / (float)renderWidth;
		return Math.Max( 1, (int)( 8.0f * ratio * ratio ) );
	}

	//Where frame index of a run is drawn from, in the world's pixels, each axis between minus and plus a half:
	//Halton's sequence in bases two and three, which covers a pixel evenly however far into it one stops.
	internal static Vector2 Jitter( int index, int phases )
	{
		var n = ( index % Math.Max( phases, 1 ) ) + 1;
		return new Vector2( Halton( n, 2 ) - 0.5f, Halton( n, 3 ) - 0.5f );
	}

	private static float Halton( int index, int radix )
	{
		var result = 0.0f;
		var fraction = 1.0f / radix;

		while ( index > 0 )
		{
			result += ( index % radix ) * fraction;
			index /= radix;
			fraction /= radix;
		}

		return result;
	}

	//The clip-space translation that draws the world jitter pixels right and down of where it would stand - after the projection, so it moves
	//the picture and not the camera. Vulkan's clip space runs y down the screen, as a picture's pixels do, so both signs are the jitter's.
	internal static System.Numerics.Matrix4x4 JitterMatrix( Vector2 jitter, int renderWidth, int renderHeight )
	{
		var shift = System.Numerics.Matrix4x4.Identity;

		if ( renderWidth <= 0 || renderHeight <= 0 ) return shift;

		shift.M41 = 2.0f * jitter.x / renderWidth;
		shift.M42 = 2.0f * jitter.y / renderHeight;

		return shift;
	}

	//Texture mip bias under upscaling: log2 of the downscale, one level finer for temporal upscalers
	internal static float MipBias( Kind kind, int renderWidth, int displayWidth )
	{
		if ( kind == Kind.Native || renderWidth <= 0 || displayWidth <= 0 ) return 0.0f;

		var bias = MathF.Log2( renderWidth / (float)displayWidth );

		if ( IsTemporal( kind ) ) bias += Rinten.Rendering.DrawSettings.UpscalerMipBias - 1.0f;

		return Math.Clamp( bias, -3.0f, 0.0f );
	}

	//A sharpness between nought and one as RCAS takes it: two to the minus how many stops softer than the sharpest it does - two stops at nought.
	internal static float RcasStrength( float sharpness )
		=> MathF.Pow( 2.0f, -2.0f * ( 1.0f - Math.Clamp( sharpness, 0.0f, 1.0f ) ) );
}
