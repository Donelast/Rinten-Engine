namespace Rinten.Build;

//Linux platform implementation
internal class LinuxPlatform : Platform
{
	protected override string PlatformBaseName => "linuxsteamrt"; //Fucking make this just "linux" when port is more mature

	public override string NativeBuildScript => "build-linux.sh";

	public override string[] RuntimeIdentifiers => [ $"linux-{RuntimeArchitecture}", "linux" ];
}
