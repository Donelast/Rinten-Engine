using NativeEngine;
using System.IO;

namespace Rinten.Engine.Settings;

//Render quality profiles adjust rendering features to a profile level
class RenderQualityProfiles
{
	Dictionary<string, Dictionary<string, Dictionary<string, string>>> Profiles { get; }

	public RenderQualityProfiles()
	{
		Profiles = EngineFileSystem.CoreContent.ReadJsonOrDefault<Dictionary<string, Dictionary<string, Dictionary<string, string>>>>( Path.Combine( "cfg", "quality_profiles.json" ), new() );
	}

	public void SetDefaults( RenderSettings settings )
	{
		//Set all our convars based on our set profiles as we load
		SetGroupConVars( "TextureQuality", settings.TextureQuality.ToString() );
		SetGroupConVars( "PostProcessQuality", settings.PostProcessQuality.ToString() );
		SetGroupConVars( "VolumetricFogQuality", settings.VolumetricFogQuality.ToString() );
		SetGroupConVars( "ShadowQuality", settings.ShadowQuality.ToString() );
	}

	/// <summary>Set all the convars for a group based on the level</summary>
	public void SetGroupConVars( string group, string level )
	{
		if ( !Profiles.ContainsKey( group ) )
			return;

		if ( !Profiles[group].ContainsKey( level ) )
			return;

		foreach ( var convar in Profiles[group][level] )
		{
			//Usually the convar is just not registered yet (pre-scan); stash the value as its default
			if ( ConVarSystem.Find( convar.Key ) is null )
			{
				//After the scan there is nothing left to register it: this is a name in the file that no convar answers to, which
				//is how three fog sizes and an occlusion quality sat here being written by every profile and read by nobody.
				if ( ConVarSystem.Scanned )
					Log.Warning( $"{group}/{level} sets {convar.Key}, which nothing registers" );
				else
					ConVarSystem.Deferred[convar.Key] = convar.Value;

				continue;
			}

			ConVarSystem.SetValue( convar.Key, convar.Value, true );
		}
	}
}
