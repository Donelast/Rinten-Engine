using System;
using NativeEngine;


namespace Rinten.Engine.Settings;

/// <summary>User graphics settings</summary>
public partial class RenderSettings
{
	internal static RenderSettings Instance = new RenderSettings();

	internal CookieContainer VideoSettings { get; } = new( "video", true );

	public event Action OnVideoSettingsChanged;
	internal RenderQualityProfiles Config { get; } = new();

	internal RenderSettings()
	{
		Config.SetDefaults( this );
	}

	public int MaxFrameRate
	{
		get => ConVarSystem.GetInt( "fps_max", 144, true );
		set => ConVarSystem.SetInt( "fps_max", value, true );
	}

	public int MaxFrameRateInactive
	{
		get => ConVarSystem.GetInt( "fps_max_inactive", 100, true );
		set => ConVarSystem.SetInt( "fps_max_inactive", value, true );
	}

	public int MaxFrameRateMenu
	{
		get => ConVarSystem.GetInt( "fps_max_menu", 144, true );
		set => ConVarSystem.SetInt( "fps_max_menu", value, true );
	}

	public float DefaultFOV
	{
		get => ConVarSystem.GetFloat( "default_fov", 80, true );
		set => ConVarSystem.SetFloat( "default_fov", value, true );
	}

	public TextureQuality TextureQuality
	{
		get => VideoSettings.Get<TextureQuality>( "texture.quality", TextureQuality.High );
		set
		{
			VideoSettings.Set<TextureQuality>( "texture.quality", value );
			Config.SetGroupConVars( "TextureQuality", value.ToString() );
		}
	}

	public VolumetricFogQuality VolumetricFogQuality
	{
		get => VideoSettings.Get<VolumetricFogQuality>( "volumetricfog.quality", VolumetricFogQuality.High );
		set
		{
			VideoSettings.Set<VolumetricFogQuality>( "volumetricfog.quality", value );
			Config.SetGroupConVars( "VolumetricFogQuality", value.ToString() );
		}
	}

	public PostProcessQuality PostProcessQuality
	{
		get => VideoSettings.Get<PostProcessQuality>( "postprocess.quality", PostProcessQuality.High );
		set
		{
			VideoSettings.Set<PostProcessQuality>( "postprocess.quality", value );
			Config.SetGroupConVars( "PostProcessQuality", value.ToString() );
		}
	}

	public ShadowQuality ShadowQuality
	{
		get => VideoSettings.Get<ShadowQuality>( "shadow.quality", ShadowQuality.High );
		set
		{
			VideoSettings.Set<ShadowQuality>( "shadow.quality", value );
			Config.SetGroupConVars( "ShadowQuality", value.ToString() );
		}
	}

	public float MotionBlurScale
	{
		get => VideoSettings.Get<float>( "motionblur.scale", 1.0f );
		set
		{
			VideoSettings.Set<float>( "motionblur.scale", value );
			//The effect's own switch, by its name - see MotionBlur.UserScale.
			ConVarSystem.SetFloat( "r_motionblur_scale", value, true );
		}
	}

	//The upscaler's settings are its convars, which keep themselves - see DrawSettings.Upscaler and the ones beside it. They were kept in the video
	//cookie and written into the convars only when changed, so a game started again came up with the setting on the screen and native on the card.

	public UpscalerMode UpscalerMode
	{
		get => (UpscalerMode)ConVarSystem.GetInt( "r_upscaling", 0, true );
		set => ConVarSystem.SetInt( "r_upscaling", (int)value, true );
	}

	/// <summary>Render-resolution scale used by Stretch and FSR1 (40-100%).</summary>
	public float UpscalerRenderScale
	{
		get => ConVarSystem.GetFloat( "r_upscaler_render_scale", 0.75f, true );
		set => ConVarSystem.SetFloat( "r_upscaler_render_scale", Math.Clamp( value, 0.4f, 1.0f ), true );
	}

	/// <summary>FSR1 RCAS sharpness in [0..1]. Only used when <see cref="UpscalerMode"/> is FSR1.</summary>
	public float Fsr1Sharpness
	{
		get => ConVarSystem.GetFloat( "r_fsr_rcas_sharpness", 0.25f, true );
		set => ConVarSystem.SetFloat( "r_fsr_rcas_sharpness", Math.Clamp( value, 0.0f, 1.0f ), true );
	}

	/// <summary>FSR3 quality preset (Ultra Performance / Performance / Balanced / Quality / Native), maps to a discrete render-resolution multiplier. Only used when <see cref="UpscalerMode"/> is <see cref="UpscalerMode.FSR3"/>.</summary>
	public Fsr3UpscalerQuality Fsr3UpscalerQuality
	{
		get => (Fsr3UpscalerQuality)ConVarSystem.GetInt( "r_fsr3_quality", (int)Fsr3UpscalerQuality.Quality, true );
		set
		{
			if ( value != Fsr3UpscalerQuality.Off )
				ConVarSystem.SetInt( "r_fsr3_quality", (int)value, true );
		}
	}

	/// <summary>FSR3 RCAS sharpness in [0..1]. Only used when <see cref="UpscalerMode"/> is FSR3.</summary>
	public float Fsr3Sharpness
	{
		get => ConVarSystem.GetFloat( "r_fsr3_sharpness", 0.5f, true );
		set => ConVarSystem.SetFloat( "r_fsr3_sharpness", Math.Clamp( value, 0.0f, 1.0f ), true );
	}

	/// <summary>DLSS quality preset (Ultra Performance / Performance / Balanced / Quality / DLAA), maps to a discrete render-resolution multiplier. Only used when <see cref="UpscalerMode"/> is <see cref="UpscalerMode.DLSS"/>. DLSS has no sharpness control.</summary>
	public DlssQuality DlssQuality
	{
		get => (DlssQuality)ConVarSystem.GetInt( "r_dlss_quality", (int)DlssQuality.Quality, true );
		set
		{
			if ( value != DlssQuality.Off )
				ConVarSystem.SetInt( "r_dlss_quality", (int)value, true );
		}
	}

	/// <summary>Which DLSS network draws: nought is the newest the shipped library has, the rest are NGX's render presets by number - 1 is A.</summary>
	public int DlssPreset
	{
		get => ConVarSystem.GetInt( "r_dlss_preset", 0, true );
		set => ConVarSystem.SetInt( "r_dlss_preset", Math.Clamp( value, 0, 26 ), true );
	}

	/// <summary>How much of the window's resolution the engine's own temporal upscaler draws the world at - one is temporal anti-aliasing at native.</summary>
	public float TemporalRenderScale
	{
		get => ConVarSystem.GetFloat( "r_taa_scale", 1.0f, true );
		set => ConVarSystem.SetFloat( "r_taa_scale", Math.Clamp( value, 0.33f, 1.0f ), true );
	}

	/// <summary>Sharpening after the engine's temporal upscaler, in [0..1].</summary>
	public float TemporalSharpness
	{
		get => ConVarSystem.GetFloat( "r_taa_sharpness", 0.2f, true );
		set => ConVarSystem.SetFloat( "r_taa_sharpness", Math.Clamp( value, 0.0f, 1.0f ), true );
	}

	/// <summary>Keep the processor from running frames ahead of the card - AMD Anti-Lag or NVIDIA Reflex, whichever the card has: 0 off, 1 on, 2 on with Reflex's boost. On Linux with an AMD card this is Mesa's layer, which a restart switches on.</summary>
	public int LowLatency
	{
		get => ConVarSystem.GetInt( "r_low_latency", 0, true );
		set => ConVarSystem.SetInt( "r_low_latency", Math.Clamp( value, 0, 2 ), true );
	}

	/// <summary>Returns whether the given <see cref="UpscalerMode"/> is usable on the current graphics device. The temporal ones need the full frame; DLSS needs an NVIDIA card whose driver has NGX, and the DLSS library beside the game - see CoreDlss.</summary>
	public static bool IsUpscalerModeSupported( UpscalerMode mode ) => CoreUpscale.Supported( mode );

	public void ResetVideoConfig()
	{
		int desktopWidth = 0;
		int desktopHeight = 0;
		uint desktopRefreshRate = 0;
		EnginePlatform.GetDesktopResolution( EnginePlatform.GetDefaultMonitorIndex(), ref desktopWidth, ref desktopHeight, ref desktopRefreshRate );
		ResolutionWidth = desktopWidth;
		ResolutionHeight = desktopHeight;

		//Whatever this build comes up as when nobody has chosen - a reset puts the setting back where
		//it started rather than picking windowed, which on a game is a choice the player did not make.
		Fullscreen = FullscreenByDefault;
		Borderless = true;
		VSync = true;

		//None, which is the only thing this renderer can actually do - no target here is
		//multisampled. Edge smoothing is DrawSettings.Fxaa, and off by default for its own reasons.
		AntiAliasQuality = MultisampleAmount.MultisampleNone;
		MaxFrameRate = 144;
		MaxFrameRateInactive = 60;
		MaxFrameRateMenu = 144;
		DefaultFOV = 75;
		UpscalerMode = UpscalerMode.Off;
		UpscalerRenderScale = 0.75f;
		Fsr1Sharpness = 0.25f;
		Fsr3UpscalerQuality = Fsr3UpscalerQuality.Quality;
		Fsr3Sharpness = 0.5f;
		DlssQuality = DlssQuality.Quality;
		DlssPreset = 0;
		TemporalRenderScale = 1.0f;
		TemporalSharpness = 0.2f;

		VideoSettings.Save();
	}

	public void Apply()
	{
		ApplyVideoMode();

		OnVideoSettingsChanged?.Invoke();

		VideoSettings.Save();
	}

	//We want benchmarks to have all similar settings. Set them here. The only fluctuations we should see are resolution and hardware.
	internal void ApplySettingsForBenchmarks()
	{
		ResetVideoConfig();

		Fullscreen = false;
		Borderless = false;
		VSync = false;
		AntiAliasQuality = MultisampleAmount.MultisampleNone;
		MaxFrameRate = 10000;
		MaxFrameRateInactive = 10000;
		DefaultFOV = 75;
		ResolutionWidth = 1920;
		ResolutionHeight = 1080;
	}

}
