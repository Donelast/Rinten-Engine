using System.Collections.Generic;
using System.Linq;

namespace Editor.TreeBrowser;

/// <summary>Decides whether an asset belongs on screen.</summary>
public interface IAssetFilter
{
	bool Accepts( Asset asset );
}

/// <summary>Every filter has to agree before an asset is shown.</summary>
public sealed class CompositeAssetFilter : IAssetFilter
{
	readonly IReadOnlyList<IAssetFilter> filters;

	public CompositeAssetFilter( params IAssetFilter[] filters ) => this.filters = filters.Where( x => x is not null ).ToList();

	public bool Accepts( Asset asset ) => filters.All( x => x.Accepts( asset ) );
}

/// <summary>Lets everything through.</summary>
public sealed class AllAssetsFilter : IAssetFilter
{
	public static AllAssetsFilter Instance { get; } = new();

	AllAssetsFilter() { }

	public bool Accepts( Asset asset ) => true;
}
