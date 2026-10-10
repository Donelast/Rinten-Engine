using System;

namespace Editor;

public partial class ProjectPublisher
{
	/// <summary>Return true if this asset belongs in a standalone build.</summary>
	public static bool CanPublishFile( Asset a )
	{
		if ( a.AbsolutePath.Contains( "/addons/base/assets/shaders/", StringComparison.OrdinalIgnoreCase ) ) return false;
		if ( a.AbsolutePath.Contains( "/core/shaders/", StringComparison.OrdinalIgnoreCase ) ) return false;

		return true;
	}
}
