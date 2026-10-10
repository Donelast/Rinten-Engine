using System;

namespace Editor;

/// <summary>A platform an exported game can be built for.</summary>
public sealed record ExportPlatform
{
	//The folder under bin/ , which is also what the artifact pipeline and the engine's own native loader name it. Not the .NET identifier - those are different names for the same thing and both are needed.
	/// <summary>What the engine's own build calls this platform.</summary>
	public required string Id { get; init; }

	/// <summary>What this platform is called in the name of a build's folder.</summary>
	public required string Folder { get; init; }

	/// <summary>What a person picking one sees.</summary>
	public required string Title { get; init; }

	/// <summary>The .NET runtime identifier, for publishing the launcher against it.</summary>
	public required string RuntimeIdentifier { get; init; }

	/// <summary>What a program is called at the end on this platform - and nothing at all on the two that do not decorate executables.</summary>
	public required string ExecutableSuffix { get; init; }

	//Only Windows does. A Linux program has no icon of its own - the desktop file beside it carries one - and a Mac keeps it in the bundle, so asking to write one into the executable is meaningless on both.
	/// <summary>Whether an exported program carries an icon inside it.</summary>
	public bool HasExecutableIcon => this == Windows;

	/// <summary>Ships compiled BC blocks instead of pictures; desktops only (phones and web lack BC support)</summary>
	public bool ShipsCompiledPictures => !IsAndroid && !IsWeb;

	/// <summary>Ships its own .NET runtime; only for headsets (SteamOS arm64) that have no installer</summary>
	public bool ShipsRuntime => this == LinuxArm64;

	public static readonly ExportPlatform Windows = new()
	{
		Id = "win64",
		Folder = "win64",
		Title = "Windows (x64)",
		RuntimeIdentifier = "win-x64",
		ExecutableSuffix = ".exe",
	};

	public static readonly ExportPlatform Linux = new()
	{
		Id = "linuxsteamrt64",
		Folder = "linux64",
		Title = "Linux (x64)",
		RuntimeIdentifier = "linux-x64",
		ExecutableSuffix = "",
	};

	/// <summary>SteamOS arm64 (Steam Frame), native; bin folder comes from the linux-arm64 CI artifact</summary>
	public static readonly ExportPlatform LinuxArm64 = new()
	{
		Id = "linuxsteamrtarm64",
		Folder = "linuxarm64",
		Title = "Linux Arm (Steam Frame)",
		RuntimeIdentifier = "linux-arm64",
		ExecutableSuffix = "",
	};

	public static readonly ExportPlatform MacOS = new()
	{
		Id = "osxarm64",
		Folder = "macos",
		Title = "macOS (Apple Silicon)",
		RuntimeIdentifier = "osx-arm64",
		ExecutableSuffix = "",
	};

	/// <summary>A phone, which is an apk rather than a folder somebody runs something out of.</summary>
	public static readonly ExportPlatform Android = new()
	{
		Id = "android-arm64",
		Folder = "android",
		Title = "Android (arm64)",
		RuntimeIdentifier = "android-arm64",
		ExecutableSuffix = ".apk",
	};

	public static readonly ExportPlatform Web = new()
	{
		Id = "wasm32",
		Folder = "web",
		Title = "Web",
		RuntimeIdentifier = "browser-wasm",
		ExecutableSuffix = ".html",
	};

	public static IReadOnlyList<ExportPlatform> All { get; } = new[] { Windows, Linux, LinuxArm64, MacOS, Web, Android };

	/// <summary>Platforms the build page offers; macOS left out until it has a core and launcher</summary>
	public static IReadOnlyList<ExportPlatform> Offered { get; } = new[] { Windows, Linux, LinuxArm64, Web, Android };

	/// <summary>Whether this export is a page rather than a program.</summary>
	public bool IsWeb => this == Web;

	/// <summary>Whether this export is packaged rather than laid out in a folder.</summary>
	public bool IsAndroid => this == Android;

	/// <summary>The platform the editor is running on, which is what an export defaults to - it is the one the person can start afterwards and look at.</summary>
	public static ExportPlatform Host
	{
		get
		{
			if ( OperatingSystem.IsWindows() ) return Windows;
			if ( OperatingSystem.IsMacOS() ) return MacOS;

			return System.Runtime.InteropServices.RuntimeInformation.OSArchitecture ==
				System.Runtime.InteropServices.Architecture.Arm64 ? LinuxArm64 : Linux;
		}
	}

	/// <summary>One by its <see cref="Id"/>, or the host where it is not one of ours.</summary>
	public static ExportPlatform FromId( string id )
	{
		foreach ( var platform in All )
		{
			if ( string.Equals( platform.Id, id, StringComparison.OrdinalIgnoreCase ) )
				return platform;
		}

		return Host;
	}

	/// <summary>Where this platform's native libraries live under the engine.</summary>
	public string BinFolder => System.IO.Path.Combine( "bin", Id );

	/// <summary>A program of ours, as a filename on this platform.</summary>
	public string Executable( string name ) => name + ExecutableSuffix;

	public override string ToString() => Title;
}
