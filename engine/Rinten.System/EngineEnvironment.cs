namespace Rinten;

/// <summary>The environment the engine leaves behind it for everything else to find.</summary>
public static class EngineEnvironment
{
	//Written by the launcher, read by the tests, the build tool and the crash tooling - none of which have any other way of knowing where the engine is.
	/// <summary>The user variable holding the folder the engine was last run from.</summary>
	public const string Folder = "RINTEN_ENGINE";

	//It stays in a person's profile until something takes it out, pointing at this engine under a name that is no longer ours - so the launcher removes it rather than leaving two variables that disagree.
	/// <summary>What the same variable used to be called.</summary>
	public const string FormerFolder = "FACEPUNCH_ENGINE";

	/// <summary>Where the engine is, as the last run of it left the answer. Null when nothing has said.</summary>
	public static string EngineFolder
	{
		get
		{
			var inProcess = System.Environment.GetEnvironmentVariable( Folder );

			if ( !string.IsNullOrWhiteSpace( inProcess ) ) return inProcess;

			if ( !System.OperatingSystem.IsWindows() ) return null;

			var saved = System.Environment.GetEnvironmentVariable(
				Folder, System.EnvironmentVariableTarget.User );

			return string.IsNullOrWhiteSpace( saved ) ? null : saved;
		}

		set
		{
			//This process either way: everything the launcher starts inherits it, which is the part that has to hold on every platform.
			System.Environment.SetEnvironmentVariable( Folder, value );

			if ( !System.OperatingSystem.IsWindows() ) return;

			if ( System.Environment.GetEnvironmentVariable( Folder, System.EnvironmentVariableTarget.User ) != value )
				System.Environment.SetEnvironmentVariable( Folder, value, System.EnvironmentVariableTarget.User );

			//And the name this used to go by, out of the profile - it points at this same engine under a name that is no longer ours.
			if ( System.Environment.GetEnvironmentVariable( FormerFolder, System.EnvironmentVariableTarget.User ) is not null )
				System.Environment.SetEnvironmentVariable( FormerFolder, null, System.EnvironmentVariableTarget.User );
		}
	}
}
