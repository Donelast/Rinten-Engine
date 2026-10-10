namespace Rinten.Build;

//Windows platform implementation
internal class WindowsPlatform : Platform
{
	protected override string PlatformBaseName => "win";

	public override string NativeBuildScript => "build-win64.cmd";

	public override string[] RuntimeIdentifiers => [ $"win-{RuntimeArchitecture}", "win" ];
}
