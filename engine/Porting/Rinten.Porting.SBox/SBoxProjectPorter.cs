namespace Rinten.Porting.SBox;

/// <summary>s&amp;box, as one of the engines a project can be brought here from.</summary>
public sealed class SBoxProjectPorter : IProjectPorter
{
	public string SourceName => "s&box";

	/// <summary>Inches, which is what s&amp;box measures in and therefore what every number in a s&amp;box project means.</summary>
	public PortUnits Units => PortUnits.SBox;

	public ForeignBasis Axes => ForeignBasis.Quake;

	/// <summary>A .sbproj in the root is the marker. Assets alone are not enough - a directory of .fbx belongs to whoever's project file sits next to it.</summary>
	public bool Recognises( string sourceRoot )
	{
		if ( string.IsNullOrWhiteSpace( sourceRoot ) ) return false;
		if ( !System.IO.Directory.Exists( sourceRoot ) ) return false;

		return System.IO.Directory
			.EnumerateFiles( sourceRoot, "*.sbproj", System.IO.SearchOption.TopDirectoryOnly )
			.Any();
	}

	public PortReport Port( string sourceRoot, string destination )
		=> SBoxPorter.Port( sourceRoot, destination );
}
