using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace Rinten;

public partial class Package
{
	/// <summary>Small icon badges shown over the package's thumbnail - workshop-approved, updated-since-you-played, favourited, contest-winner, etc. Computed on the backend when the package is served. Never default; empty when there's no flair.</summary>
	public ImmutableArray<PackageFlair> Flair { get; internal set; } = ImmutableArray<PackageFlair>.Empty;

	/// <summary>A single badge shown over a package's thumbnail.</summary>
	public readonly record struct PackageFlair( string Kind, string Icon, string Style, string Tooltip )
	{
		internal static ImmutableArray<PackageFlair> FromDto( List<Rinten.Services.PackageFlair> flair )
		{
			if ( flair is null || flair.Count == 0 )
				return ImmutableArray<PackageFlair>.Empty;

			var array = new PackageFlair[flair.Count];

			for ( int i = 0; i < array.Length; i++ )
			{
				var x = flair[i];
				array[i] = new PackageFlair( x.Kind, x.Icon, x.Style, x.Tooltip );
			}

			return ImmutableCollectionsMarshal.AsImmutableArray( array );
		}
	}
}
