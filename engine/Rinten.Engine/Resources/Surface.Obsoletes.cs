using System.ComponentModel;

namespace Rinten;

public partial class Surface
{
	[Obsolete]
	public float Dampening { get; set; } = 0.0f;

	[Obsolete]
	public struct ImpactEffectData
	{
		//Spawn one of these particles on impact.

		[ResourceType( "fx" )] public List<string> Regular { get; set; }

		//Spawn one of these particles when hit by a bullet.
		[ResourceType( "fx" )] public List<string> Bullet { get; set; }

		//Use one of these as the bullet impact decal.
		[ResourceType( "decal" )] public List<string> BulletDecal { get; set; }

		//Spawn one of these particles on impact.
		[ResourceType( "fx" )] public List<string> SoftParticles { get; set; }

		//Use one of these as a physics impact decal.
		[ResourceType( "decal" )] public List<string> SoftDecal { get; set; }

		//Spawn one of these particles on impact.
		[ResourceType( "fx" )] public List<string> HardParticles { get; set; }


		//Use one of these as a physics impact decal.
		[ResourceType( "decal" )] public List<string> HardDecal { get; set; }
	}

	/// <summary>Impact effects of this surface material.</summary>
	[Obsolete, Hide, EditorBrowsable( EditorBrowsableState.Never )]
	public ImpactEffectData ImpactEffects { get; set; }

	[Obsolete]
	public struct ScrapeEffectData
	{
		/// <summary>Similar to friction but only affects whether a scrape is rough or smooth.</summary>
		[DefaultValue( 0.25f )]
		public float RoughnessFactor { get; set; }

		/// <summary>Surface roughness greater than this results in rough scrapes.</summary>
		[DefaultValue( 0.5f )]
		public float RoughThreshold { get; set; }

		//Spawn one of these particle effects during a smooth scrape.
		[Obsolete]
		[ResourceType( "fx" )] public List<string> SmoothParticles { get; set; }

		//Spawn one of these particle effects during a rough scrape.
		[Obsolete]
		[ResourceType( "fx" )] public List<string> RoughParticles { get; set; }

		//Use one of these particles during a smooth scrape.
		[ResourceType( "decal" )] public List<string> SmoothDecal { get; set; }

		//Use one of these particles during a rough scrape.
		[ResourceType( "decal" )] public List<string> RoughDecal { get; set; }

		public ScrapeEffectData()
		{
			RoughnessFactor = 0.25f;
			RoughThreshold = 0.5f;
		}
	}

	/// <summary>Scrape effects of this surface material.</summary>
	[Obsolete, Hide, EditorBrowsable( EditorBrowsableState.Never )]
	public ScrapeEffectData ScrapeEffects { get; set; }

	[Obsolete]
	public struct OldSoundData
	{
		//Left footstep sound.
		[ResourceType( "sound" )] public string FootLeft { get; set; }

		//Right footstep sound.
		[ResourceType( "sound" )] public string FootRight { get; set; }

		//Jump sound for this surface.
		[ResourceType( "sound" )] public string FootLaunch { get; set; }

		//Landing sound for this surface.
		[ResourceType( "sound" )] public string FootLand { get; set; }

		//Bullet impact sound for this surface.
		[ResourceType( "sound" )] public string Bullet { get; set; }

		//Hard, high velocity impact sound.
		[ResourceType( "sound" )] public string ImpactHard { get; set; }

		//Soft, low velocity impact sound.
		[ResourceType( "sound" )] public string ImpactSoft { get; set; }

		//Rough scraping sound when scraping against another surface.
		[ResourceType( "sound" )] public string ScrapeRough { get; set; }

		//Smooth scraping sound when scraping against another surface.
		[ResourceType( "sound" )] public string ScrapeSmooth { get; set; }
	}

	/// <summary>Sounds associated with this surface material.</summary>
	[Obsolete, Hide, EditorBrowsable( EditorBrowsableState.Never )]
	public OldSoundData Sounds { get; set; }
}
