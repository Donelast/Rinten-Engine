using Editor.TreeBrowser;

namespace Editor.AssetPickers;

/// <summary>The editor's asset browser in a modal window, letting the user pick assets. Supports limiting display to only certain asset types.</summary>
public class GenericPicker : AssetPicker
{
	/// <summary>The browser this picker is built around.</summary>
	public TreeBrowserWidget Browser { get; protected set; }

	/// <summary>The picked assets.</summary>
	public List<Asset> Assets { get; protected set; }

	internal PickerFooterPanel Footer;

	const float FooterHeight = 48f;

	static GenericPicker _emptyFilterPicker;
	readonly bool _pooled;
	readonly List<AssetType> _assetTypes;
	bool _uiCreated;

	/// <summary>One picker for base Resource fields without a registered AssetType.</summary>
	public static GenericPicker GetEmptyFilterPicker( PickerOptions options )
	{
		if ( _emptyFilterPicker is { Window: { IsValid: true } } existing )
		{
			if ( existing.Browser.IsValid() )
				existing.Browser.Options.EnableMultiselect = options.EnableMultiselect;

			return existing;
		}

		options.EnableMounts = false;
		_emptyFilterPicker = new GenericPicker( PickerParent(), [], options, pooled: true );
		return _emptyFilterPicker;
	}

	static Widget PickerParent() => Rinten.Internal.GlobalToolsNamespace.EditorWindow;

	public GenericPicker( Widget parent, List<AssetType> assetTypes, PickerOptions options, bool pooled = false ) : base( parent, null, options )
	{
		_pooled = pooled;
		_assetTypes = assetTypes;

		Window.Size = new Vector2( 1280, 720 );
		Window.SetModal( true );
		Window.MinimumSize = 200;
		Window.MaximumSize = 10000;
		Window.Title = $"Select {string.Join( ", ", assetTypes?.Select( x => x.FriendlyName ) ?? [] )} - Asset Picker";

		Window.StateCookie = "AssetPicker";
		Window.RestoreFromStateCookie();

		if ( _pooled )
			Window.DeleteOnClose = false;

		Layout = Layout.Column();
	}

	public override void Show()
	{
		EnsureUI();
		Update();
		base.Show();
	}

	void EnsureUI()
	{
		if ( _uiCreated )
			return;

		_uiCreated = true;
		CreateUI( _assetTypes );
	}

	public override void Close()
	{
		if ( _pooled )
		{
			Window.Hide();
			return;
		}

		if ( ReferenceEquals( _emptyFilterPicker, this ) )
			_emptyFilterPicker = null;

		TeardownUI();
		base.Close();
	}

	void TeardownUI()
	{
		if ( !_uiCreated )
			return;

		if ( Browser is { IsValid: true } )
			Browser.Destroy();

		Browser = null;
		Footer = null;
		_uiCreated = false;
	}

	public override void OnDestroyed()
	{
		if ( ReferenceEquals( _emptyFilterPicker, this ) )
			_emptyFilterPicker = null;

		base.OnDestroyed();
	}

	//Browser placed directly in the window with buttons below, no dock manager or status bar
	void CreateUI( List<AssetType> assetTypes )
	{
		Browser = new PickerTreeBrowserHost( this, assetTypes, Options, PickAsset );
		WireBrowser( Browser );

		Layout.Add( Browser, 1 );
		Layout.Add( CreateFooter() );
	}

	PanelWidget CreateFooter()
	{
		Footer = new PickerFooterPanel
		{
			OnConfirm = Select,
			OnCancel = Close
		};

		var host = new PanelWidget( this, Footer );
		host.FixedHeight = FooterHeight;
		return host;
	}

	void WireBrowser( TreeBrowserWidget browser )
	{
		browser.OnAssetHighlighted += Highlight;
		browser.OnSelectionChanged += Highlight;
	}

	public override void SetSelection( Asset asset )
	{
		EnsureUI();

		if ( asset is null )
		{
			Browser.NavigateToFolder( Project.Current.GetAssetsPath() );
			return;
		}

		if ( asset.Package is not null )
		{
			SetSelection( asset.Package );
			return;
		}

		Browser.FocusOnAsset( asset );
	}

	public override void SetSearchText( string value )
	{
		EnsureUI();
		Browser?.SetSearch( value );
	}

	//A double click in the browser picks that asset outright.
	void PickAsset( Asset asset )
	{
		if ( !IsSelectionValid( asset ) )
			return;

		Submit( [asset] );
	}

	void Select()
	{
		if ( Browser is null )
		{
			Close();
			return;
		}

		var assets = Browser.GetSelectedAssets().ToList();
		if ( assets.Count == 0 )
			assets = Assets ?? [];

		if ( assets.Count == 0 || !IsSelectionValid( assets ) )
			return;

		Submit( assets.ToArray() );
	}

	bool IsSelectionValid( List<Asset> assets ) => assets.TrueForAll( IsSelectionValid );

	bool IsSelectionValid( Asset asset )
	{
		if ( _assetTypes is null || _assetTypes.Count == 0 )
			return true;

		return asset is not null && _assetTypes.Contains( asset.AssetType );
	}

	void Highlight( Asset asset )
	{
		var isValid = IsSelectionValid( asset );
		if ( isValid )
		{
			Assets = [asset];
			OnAssetHighlighted?.Invoke( Assets.ToArray() );
			BindSystem.Flush();
		}

		EditorUtility.PlayAssetSound( asset );
		SetConfirmEnabled( isValid );
	}

	void Highlight( IReadOnlyList<Asset> assets )
	{
		if ( assets is null || assets.Count == 0 )
		{
			SetConfirmEnabled( false );
			return;
		}

		var picked = assets.ToList();
		var isValid = IsSelectionValid( picked );
		if ( isValid )
		{
			Assets = picked;
			OnAssetHighlighted?.Invoke( Assets.ToArray() );
			BindSystem.Flush();
		}

		SetConfirmEnabled( isValid );
	}

	void SetConfirmEnabled( bool enabled )
	{
		if ( Footer.IsValid() )
			Footer.ConfirmEnabled = enabled;
	}

	[Event( "assetsystem.openpicker" )]
	static void OpenAssetPicker( AssetSystem.AssetPickerParameters parameters )
	{
		var options = new PickerOptions { AdditionalTypes = parameters.FilterAssetTypes };

		AssetPicker picker;
		if ( parameters.FilterAssetTypes.Select( x => x.ResourceType ).Distinct().Where( x => x != null ).Count() == 1 )
			picker = Create( parameters.ParentWidget, parameters.FilterAssetTypes[0], options );
		else
			picker = new GenericPicker( parameters.ParentWidget, parameters.FilterAssetTypes, options );

		picker.Show();

		var requestedViewMode = (AssetListViewMode)parameters.ViewMode;
		if ( picker is GenericPicker && BrowserSettings.ViewMode != requestedViewMode )
			BrowserSettings.ViewMode = requestedViewMode;

		picker.OnAssetPicked = parameters.AssetSelectedCallback;

		if ( !string.IsNullOrEmpty( parameters.Title ) )
			picker.Title = parameters.Title;

		picker.SetSelection( parameters.InitialSelectedAsset );
		picker.SetSearchText( parameters.InitialSearchText );
	}
}
