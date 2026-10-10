using Rinten;
using System;

namespace Editor.TreeBrowser;

public static class BrowserSettings
{
	const string ShowTreeIconsKey = "TreeAssetBrowser.Settings.ShowTreeIcons";
	const string ViewModeKey = "TreeAssetBrowser.Settings.ViewMode";

	public static event Action OnChanged;

	static bool _loaded;
	static bool _showTreeIcons;
	static AssetListViewMode _viewMode = AssetListViewMode.MediumIcons;

	public static bool ShowTreeIcons
	{
		get { EnsureLoaded(); return _showTreeIcons; }
		set
		{
			EnsureLoaded();
			if ( _showTreeIcons == value ) return;
			_showTreeIcons = value;
			ProjectCookie.Set( ShowTreeIconsKey, value );
			OnChanged?.Invoke();
		}
	}

	public static AssetListViewMode ViewMode
	{
		get { EnsureLoaded(); return _viewMode; }
		set
		{
			EnsureLoaded();
			if ( _viewMode == value ) return;
			_viewMode = value;
			ProjectCookie.Set( ViewModeKey, (int)value );
			OnChanged?.Invoke();
		}
	}

	static void EnsureLoaded()
	{
		if ( _loaded ) return;
		_loaded = true;
		_showTreeIcons = ProjectCookie.Get( ShowTreeIconsKey, false );
		_viewMode = (AssetListViewMode)ProjectCookie.Get( ViewModeKey, (int)AssetListViewMode.MediumIcons );
	}
}
