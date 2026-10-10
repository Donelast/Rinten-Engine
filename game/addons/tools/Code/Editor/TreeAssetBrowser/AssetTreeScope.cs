namespace Editor.TreeBrowser;

/// <summary>Which body of content the browser is looking at.</summary>
public enum AssetTreeScope
{
	/// <summary>This project, its libraries and the engine.</summary>
	Project,

	/// <summary>Games mounted from elsewhere on the machine.</summary>
	Mounts
}
