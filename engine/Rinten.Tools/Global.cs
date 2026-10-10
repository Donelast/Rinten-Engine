using System;
using System.Reflection;

namespace Rinten;

/// <summary>Utility info for tools usage.</summary>
public static class Global
{
	internal static Assembly Assembly { get; set; }

	/// <summary>Is the local client in game or not.</summary>
	public static bool InGame { get; internal set; }

	/// <summary>Name of the map the local client is on.</summary>
	public static string MapName { get; internal set; } = "";

	/// <summary>Identity of the gamemode the local client is currently playing.</summary>
	public static string GameIdent { get; internal set; } = "";

	/// <summary>Front facing identity for the backend - its host, or a placeholder when none is configured. There is no backend by default.</summary>
	public static string BackendTitle =>
		Uri.TryCreate( BackendUrl, UriKind.Absolute, out var uri ) ? uri.Host : "the backend";

	/// <summary>Url for the backend, or empty when there isn't one.</summary>
	public static string BackendUrl => Backend.Url ?? "";

	/// <summary>Are we connected to the API? If not, we're in offline mode.</summary>
	public static bool IsApiConnected => Api.IsConnected;
}
