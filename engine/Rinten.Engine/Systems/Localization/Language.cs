using Rinten.Engine;

namespace Rinten;

/// <summary>A container for the current language, allowing access to translated phrases and language information.</summary>
public class LanguageContainer
{
	/// <summary>The abbreviation for the language the user wants. This is set by the user in the options menu.</summary>
	public string SelectedCode => Application.LanguageCode;

	/// <summary>Information about the current selected language. Will default to English if the current language isn't found.</summary>
	public Rinten.Localization.LanguageInformation Current { get; internal set; }

	//FileSystem used for localization.
	internal BaseFileSystem FileSystem { get; private set; }

	Rinten.Localization.PhraseCollection lang;
	FileWatch _watcher;

	internal LanguageContainer()
	{
		FileSystem = new AggregateFileSystem();

		_watcher = FileSystem.Watch();
		_watcher.OnChanges += x => Refresh();
	}

	string _previousLanguage;

	internal void Tick()
	{
		var language = Application.LanguageCode;

		language ??= "en";
		language = language.ToLower();

		if ( _previousLanguage == language )
			return;

		_previousLanguage = language;

		//Add english first for fallbacks
		lang = new Localization.PhraseCollection();
		AddFromPath( "en" );

		//Switch to new language
		AddFromPath( language );

		//Notify UI system so we can update text if needed
		GlobalContext.Current.UISystem.OnLanguageChanged();
	}

	void AddFromPath( string shortName )
	{
		if ( string.IsNullOrWhiteSpace( shortName ) ) return;
		if ( shortName.Contains( "." ) ) return;
		if ( shortName.Contains( ":" ) ) return;
		if ( shortName.Contains( "/" ) ) return;

		var language = Rinten.Localization.Languages.Find( shortName );
		if ( language != null ) Current = language;


		foreach ( var file in FileSystem.FindFile( shortName, "*.json" ) )
		{
			AddFile( $"{shortName}/{file}" );
		}

	}

	void AddFile( string path )
	{
		try
		{
			var entries = FileSystem.ReadJson<Dictionary<string, string>>( path );
			if ( entries == null ) return;

			foreach ( var entry in entries )
			{
				lang.Set( entry.Key, entry.Value );
			}

		}
		catch ( Exception e )
		{
			Log.Warning( $"Couldn't read localization file {path}: {e}" );
		}
	}

	//Called when file(s) have changed and we should reload next tick
	internal void Refresh()
	{
		//setting this to null will cause a reload in tick
		_previousLanguage = null;
	}

	/// <summary>Look up a phrase</summary>
	public string GetPhrase( string textToken, Dictionary<string, object> data = null )
	{
		if ( lang == null || Language.DisplayKeys )
			return textToken;

		return lang.GetPhrase( textToken, data );
	}

	internal void Shutdown()
	{
		_watcher?.Dispose();
		_watcher = null;

		FileSystem?.Dispose();
		FileSystem = null;
	}
}

/// <summary>Allows access to translated phrases, allowing the translation of gamemodes etc</summary>
[SkipHotload]
public static class Language
{
	[ConVar( "lang.showkeys", Help = "Show keys/phrases instead of translated text. Useful for debugging localization." )]
	internal static bool DisplayKeys
	{
		get => field;
		set
		{
			field = value;

			//trigger labels etc to update
			GlobalContext.Current.UISystem?.OnLanguageChanged();
		}
	} = false;

	/// <summary>The abbreviation for the language the user wants. This is set by the user in the options menu.</summary>
	public static string SelectedCode => Game.Language.SelectedCode;

	/// <summary>Information about the current selected language. Will default to English if the current language isn't found.</summary>
	public static Rinten.Localization.LanguageInformation Current => Game.Language.Current;

	/// <summary>Look up a phrase</summary>
	public static string GetPhrase( string textToken, Dictionary<string, object> data = null ) => Game.Language.GetPhrase( textToken, data );

}
