using System;

namespace Editor;

public class ExportConfig
{
	public Project Project { get; set; }

	/// <summary>Assemblies can reference asset packages. This is a list of packages that the compiled code references.</summary>
	public HashSet<string> CodePackages = new();

	/// <summary>If the compile process created any assemblies</summary>
	public Dictionary<string, object> AssemblyFiles { get; set; }

	/// <summary>Where are we putting the exported build?</summary>
	[Title( "Export Directory" ), FolderPath]
	public string TargetDir { get; set; }

	//The machine the editor is running on unless somebody says otherwise. An export is a folder of files rather than a compile, so a build for another desktop can be made from here - what changes is which native libraries go in it and what the program is called.
	/// <summary>Which platform the build is for.</summary>
	[Title( "Platform" )]
	public ExportPlatform Platform { get; set; } = ExportPlatform.Host;

	/// <summary>The target .exe name for this export</summary>
	[Title( "Executable Name" )]
	public string ExecutableName { get; set; }

	/// <summary>The icon for the target .exe</summary>
	[Title( "Executable Icon" )] //can't make this a .ico picker yet, we should really just convert from png ourselves though
	public string TargetIcon { get; set; }

	/// <summary>The splash screen to use</summary>
	[Title( "Startup Image" ), ResourceType( "tex" )] //should make this png
	public string StartupImage { get; set; }

	//What an upload to itch.io is: one archive with index.html at its root. Only the web export writes one - a desktop build is a folder people unpack and run, and zipping it here would save nobody a step.
	/// <summary>Whether to zip the finished build beside itself.</summary>
	[Title( "Zip For Upload" )]
	public bool CreateZip { get; set; } = true;

	/// <summary>A build for the one making the game rather than for its players: it says so in its manifest, and the game can ask - see Application.IsDebugBuild - and show what it shows its developer, a frame rate or the log. Off, the same game ships without them.</summary>
	[Title( "Export Debug" )]
	public bool ExportDebug { get; set; }

	/// <summary>Game's build date</summary>
	public DateTime BuildDate { get; set; } = DateTime.Now;
}
