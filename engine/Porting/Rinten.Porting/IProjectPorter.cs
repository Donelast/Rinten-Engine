namespace Rinten.Porting;

/// <summary>One engine a project can be brought here from.</summary>
public interface IProjectPorter
{
	/// <summary>What this ports from, for menus and logs. "s&amp;box", "Unity".</summary>
	string SourceName { get; }

	/// <summary>What that engine measures in, and what it does to a number.</summary>
	PortUnits Units { get; }

	/// <summary>What that engine calls its axes, and how they turn into ours. Kept apart from <see cref="Units"/> on purpose: a port that muddles a basis change into a scale factor produces a world that is the right size and inside out.</summary>
	ForeignBasis Axes { get; }

	//By the marker files an engine leaves rather than by the assets inside: a directory of .fbx belongs to whoever's project file sits next to it.
	/// <summary>Whether this directory looks like a project of that engine.</summary>
	bool Recognises( string sourceRoot );

	/// <summary>Read the project at <paramref name="sourceRoot"/> and write a new one at <paramref name="destination"/>.</summary>
	PortReport Port( string sourceRoot, string destination );
}
