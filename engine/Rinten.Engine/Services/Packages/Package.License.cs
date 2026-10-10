namespace Rinten;

public partial class Package
{
	/// <summary>
	/// The license covering this package's art/content assets, identified by name (e.g. "CC0",
	/// "CC_BY"). Null when no license is specified. Matches <c>PackageLicense.Name</c> in the
	/// license catalog, so it can be used to look up the full license details.
	/// </summary>
	public string AssetLicense { get; internal set; }

	// Maps the backend license enum to its catalog name (see Rinten.Services.Licensing.Assets).
	// A switch over literals so it's allocation-free and independent of the enum's member names.
	internal static string LicenseName( Rinten.LicenseType type )
	{
		return type switch
		{
			Rinten.LicenseType.CC0 => "CC0",
			Rinten.LicenseType.CCBYNCND => "CC_BYNCND",
			Rinten.LicenseType.CCBY => "CC_BY",
			Rinten.LicenseType.CCBYSA => "CC_BYSA",
			_ => null,
		};
	}
}
