using System.Reflection;

public static partial class RintenSystemExtensions
{
	public static bool IsPackage( this Assembly assembly )
	{
		return assembly.GetName().Name?.StartsWith( "package." ) ?? false;
	}
}
