using static Rinten.Build.Constants;

namespace Rinten.Build.Steps;

//Runs install-desktop.sh to add a menu entry, icon and .rinproj file type; no-op if up to date
internal class DesktopEntry
{
	private const string Script = "install-desktop.sh";

	//Set this to any value to keep a build out of your home directory.
	private const string OptOut = "RINTEN_NO_DESKTOP_ENTRY";

	private readonly bool remove;
	private readonly bool status;

	//Whether a failure should fail the build. It should not, from a build.
	private readonly bool required;

	internal DesktopEntry( bool remove = false, bool status = false, bool required = true )
	{
		this.remove = remove;
		this.status = status;
		this.required = required;
	}

	internal ExitCode Run()
	{
		//Everything below is a Linux desktop convention - XDG data directories, hicolor, shared-mime-info.
		//Windows has its shortcuts made by an installer and macOS has the bundle, neither of which is this.
		if ( !OperatingSystem.IsLinux() )
			return Skip( "not Linux" );

		//A build machine has no menu to put anything in, and writing into a runner's home directory is twenty files nobody will ever look at.
		if ( !required && Utility.IsCi() )
			return Skip( "CI" );

		if ( !required && Environment.GetEnvironmentVariable( OptOut ) is not null )
			return Skip( $"{OptOut} is set" );

		//The script falls back to $HOME/.local/share, so no HOME is no install. A container build is the usual way to get here.
		if ( !required && string.IsNullOrEmpty( Environment.GetEnvironmentVariable( "HOME" ) ) )
			return Skip( "no HOME" );

		var script = Path.Combine( Directory.GetCurrentDirectory(), Script );

		if ( !File.Exists( script ) )
			return Failed( $"{Script} is not here - run this from the repository root." );

		var arguments = remove ? "--remove" : status ? "--status" : "";

		//Through bash by name rather than as an executable: the file's mode is one more thing a checkout can arrive without, and this way it does not matter.
		if ( !Utility.RunProcess( "/bin/bash", $"\"{script}\" {arguments}".TrimEnd() ) )
			return Failed( "the desktop entry could not be installed." );

		return ExitCode.Success;
	}

	private ExitCode Skip( string why )
	{
		if ( required )
			Log.Warning( $"Desktop entry: nothing to do - {why}." );

		return ExitCode.Success;
	}

	private ExitCode Failed( string what )
	{
		//A menu entry is not worth a red build. Said out loud and stepped over, unless somebody asked for this step by name.
		if ( required )
		{
			Log.Error( $"Desktop entry: {what}" );
			return ExitCode.Failure;
		}

		Log.Warning( $"Desktop entry: {what}" );
		return ExitCode.Success;
	}
}
