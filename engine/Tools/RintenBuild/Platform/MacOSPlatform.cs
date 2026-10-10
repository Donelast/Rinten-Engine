namespace Rinten.Build;

//macOS platform implementation
internal class MacOSPlatform : Platform
{
	protected override string PlatformBaseName => "osx";

	public override string NativeBuildScript => "build-macos.sh";

	public override string[] RuntimeIdentifiers => [ $"osx-{RuntimeArchitecture}", "osx" ];
}
