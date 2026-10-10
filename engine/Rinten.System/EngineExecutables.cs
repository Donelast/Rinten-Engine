using System;

namespace Rinten;

/// <summary>What the engine's own programs are called on the disk.</summary>
public static class EngineExecutables
{
	/// <summary>The editor - what a project is opened with.</summary>
	public const string Editor = "Rinten";

	/// <summary>The dedicated server.</summary>
	public const string Server = "Rinten-Server";

	/// <summary>What an exported game is built around.</summary>
	public const string Standalone = "Rinten-Standalone";

	/// <summary>What a program is called at the end here: <c>.exe</c> on Windows, nothing elsewhere.</summary>
	public static string Suffix => OperatingSystem.IsWindows() ? ".exe" : string.Empty;

	/// <summary>The editor's filename on this machine.</summary>
	public static string EditorFile => Editor + Suffix;

	/// <summary>The server's filename on this machine.</summary>
	public static string ServerFile => Server + Suffix;

	/// <summary>The standalone player's filename on this machine.</summary>
	public static string StandaloneFile => Standalone + Suffix;
}
