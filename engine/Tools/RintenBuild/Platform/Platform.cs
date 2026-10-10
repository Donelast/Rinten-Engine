using System.Runtime.InteropServices;

namespace Rinten.Build;

//Base functionality shared across all platforms
public abstract class Platform
{
	protected abstract string PlatformBaseName { get; }

	//Full platform ID like "win64", "osxarm64", etc.
	public string PlatformID => GetPlatformID( PlatformBaseName );

	//Name of the script in engine/Core that builds rintencore for this platform and installs it into game/bin.
	public abstract string NativeBuildScript { get; }

	//NuGet runtime identifiers for native assets, most specific first; packages disagree on folders
	public abstract string[] RuntimeIdentifiers { get; }

	//x64, arm64 - the second half of a runtime identifier.
	protected static string RuntimeArchitecture => RuntimeInformation.ProcessArchitecture switch
	{
		Architecture.X64 => "x64",
		Architecture.Arm64 => "arm64",
		Architecture.X86 => "x86",
		Architecture.Arm => "arm",
		_ => "unknown"
	};

	//Creates the appropriate platform implementation for the current OS
	public static Platform Create()
	{
		if ( OperatingSystem.IsWindows() )
		{
			return new WindowsPlatform();
		}
		else if ( OperatingSystem.IsLinux() )
		{
			return new LinuxPlatform();
		}
		else if ( OperatingSystem.IsMacOS() )
		{
			return new MacOSPlatform();
		}

		throw new PlatformNotSupportedException( $"Unsupported platform: {RuntimeInformation.OSDescription}" );
	}

	//Builds a platform ID by combining the base platform name with the current architecture
	protected static string GetPlatformID( string basePlatformName )
	{
		var arch = RuntimeInformation.ProcessArchitecture;
		string architecture = arch switch
		{
			Architecture.X64 => "64",
			Architecture.Arm64 => "arm64",
			Architecture.X86 => "32",
			Architecture.Arm => "arm",
			_ => "unknown"
		};

		return basePlatformName + architecture;
	}
}
