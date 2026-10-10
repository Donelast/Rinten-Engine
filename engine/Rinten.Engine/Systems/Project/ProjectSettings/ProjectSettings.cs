using Rinten.Audio;

namespace Rinten;

public class ProjectSettings
{
	/// <summary>Get the <see cref="CollisionRules"/> from the active project settings.</summary>
	public static CollisionRules Collision => Get<CollisionRules>( "Collision.config" );

	/// <summary>Get the <see cref="Input"/> from the active project settings.</summary>
	public static InputSettings Input => Get<InputSettings>( "Input.config" );

	/// <summary>Get the <see cref="NetworkingSettings"/> from the active project settings.</summary>
	public static NetworkingSettings Networking => Get<NetworkingSettings>( "Networking.config" );

	//Get the MixerSettings from the active project settings.
	internal static MixerSettings Mixer => Get<MixerSettings>( "Mixer.config" );

	//Get the CursorSettings from the active project settings.
	internal static CursorSettings Cursor => Get<CursorSettings>( "Cursors.config" );

	/// <summary>Get the <see cref="PhysicsSettings"/> from the active project settings.</summary>
	public static PhysicsSettings Physics => Get<PhysicsSettings>( "Physics.config" );

	/// <summary>Get the <see cref="SystemsConfig"/> from the active project settings.</summary>
	public static SystemsConfig Systems => Get<SystemsConfig>( "Systems.config" );

	/// <summary>Get the <see cref="PlatformSettings"/> from the active project settings.</summary>
	public static PlatformSettings Platform => Get<PlatformSettings>( "Platform.config" );

	/// <summary>Get the <see cref="RenderingSettings"/> from the active project settings.</summary>
	public static RenderingSettings Rendering => Get<RenderingSettings>( "Rendering.config" );

	//Reset any stored references to Project Settings.
	internal static void ClearCache()
	{
		_cache.Clear();
		RenderingSettings.Forget();
	}

	static Dictionary<string, ConfigData> _cache = new();

	/// <summary>Gets or creates a default version of this config data. You can safely call this multiple times and it will return the same object. The cache is cleared automatically when the project changes, or when it's hotloaded.</summary>
	public static T Get<T>( string filename ) where T : ConfigData, new()
	{
		if ( _cache.TryGetValue( filename, out var result ) && result is T t )
			return t;

		var txt = EngineFileSystem.ProjectSettings?.ReadAllText( BaseFileSystem.NormalizeFilename( filename ) );
		var config = new T();
		_cache[filename] = config;

		if ( !string.IsNullOrEmpty( txt ) )
		{
			config.Deserialize( txt );
			config.LoadedFromDisk = true;
		}

		return config;
	}
}
