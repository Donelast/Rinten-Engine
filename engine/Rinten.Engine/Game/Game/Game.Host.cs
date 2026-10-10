using Facepunch.ActionGraphs;
using Rinten.Engine;
using System.Reflection;

namespace Rinten;

public static partial class Game
{
	/// <summary>The game's TypeLibrary: sandboxed reflection to find and create types by name or id</summary>
	public static Rinten.Internal.TypeLibrary TypeLibrary
	{
		get => GlobalContext.Current.TypeLibrary;
		internal set => GlobalContext.Current.TypeLibrary = value;
	}

	/// <summary>Allows access to the cookies for the current game. The cookies are used to store persistent data across game sessions, such as user preferences or session data. Internally the cookies are encoded to JSON and stored in a file on disk.</summary>
	public static CookieContainer Cookies
	{
		get => GlobalContext.Current.Cookies;
		internal set => GlobalContext.Current.Cookies = value;
	}

	/// <summary>Lets you get translated phrases from the localization system</summary>
	public static LanguageContainer Language
	{
		get => GlobalContext.Current.Language;
		internal set => GlobalContext.Current.Language = value;
	}

	//A library of node definitions for action graphs.
	internal static NodeLibrary NodeLibrary
	{
		get => GlobalContext.Current.NodeLibrary;
		set => GlobalContext.Current.NodeLibrary = value;
	}

	private static void AddNodesFromAssembly( Assembly asm )
	{
		var result = NodeLibrary.AddAssembly( asm );

		foreach ( var error in result.Errors )
		{
			Log.Error( $"{error.Key}: {error.Value}" );
		}
	}

	//Returns true only when current code is running in the menu.
	internal static bool IsMenu => GlobalContext.Current == GlobalContext.Menu;

}
