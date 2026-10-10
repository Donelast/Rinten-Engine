using System;

namespace Editor.TreeBrowser;

/// <summary>Decides what activating an asset in the browser does.</summary>
public interface IAssetActivationPolicy
{
	void Activate( Asset asset );
}

/// <summary>Dock behaviour: hand the asset to its editor.</summary>
public sealed class OpenInEditorActivation : IAssetActivationPolicy
{
	public static OpenInEditorActivation Instance { get; } = new();

	OpenInEditorActivation() { }

	public void Activate( Asset asset ) => asset?.OpenInEditor();
}

/// <summary>Picker behaviour: hand the asset back to whoever opened the browser.</summary>
public sealed class CallbackActivation : IAssetActivationPolicy
{
	readonly Action<Asset> picked;

	public CallbackActivation( Action<Asset> picked ) => this.picked = picked;

	public void Activate( Asset asset )
	{
		if ( asset is not null )
			picked?.Invoke( asset );
	}
}
