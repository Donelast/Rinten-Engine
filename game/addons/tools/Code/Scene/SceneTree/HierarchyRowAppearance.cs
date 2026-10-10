using static Editor.BaseItemWidget;

namespace Editor;

//What a hierarchy row looks like, derived from the game object it represents.
readonly struct HierarchyRowAppearance : System.IEquatable<HierarchyRowAppearance>
{
	public string Icon { get; init; }
	public string OverlayIcon { get; init; }
	public Color IconColor { get; init; }
	public Color TextColor { get; init; }
	public bool PrefabModified { get; init; }
	public bool Dimmed { get; init; }
	public bool Inactive { get; init; }
	public bool ShowHiddenTag { get; init; }
	public bool ShowLoading { get; init; }
	public string NetworkOwner { get; init; }

	//Written out rather than left to the runtime: a struct holding reference fields gets the reflection-driven ValueType.Equals , which is slower than the twenty property writes it exists to avoid.
	/// <summary>Field by field, so a row can tell whether it has anything to write.</summary>
	public bool Equals( HierarchyRowAppearance other ) =>
		PrefabModified == other.PrefabModified &&
		Dimmed == other.Dimmed &&
		Inactive == other.Inactive &&
		ShowHiddenTag == other.ShowHiddenTag &&
		ShowLoading == other.ShowLoading &&
		IconColor == other.IconColor &&
		TextColor == other.TextColor &&
		Icon == other.Icon &&
		OverlayIcon == other.OverlayIcon &&
		NetworkOwner == other.NetworkOwner;

	public override bool Equals( object obj ) => obj is HierarchyRowAppearance other && Equals( other );

	public override int GetHashCode() => System.HashCode.Combine(
		Icon, OverlayIcon, IconColor, TextColor, NetworkOwner,
		System.HashCode.Combine( PrefabModified, Dimmed, Inactive, ShowHiddenTag, ShowLoading ) );

	public static bool operator ==( HierarchyRowAppearance a, HierarchyRowAppearance b ) => a.Equals( b );
	public static bool operator !=( HierarchyRowAppearance a, HierarchyRowAppearance b ) => !a.Equals( b );

	public static HierarchyRowAppearance For( GameObject go, HierarchyTreePanel tree )
	{
		if ( !go.IsValid() || !go.Scene.IsValid() )
			return default;

		var isBone = go.Flags.Contains( GameObjectFlags.Bone );
		var isProceduralBone = go.Flags.Contains( GameObjectFlags.ProceduralBone );
		var isAttachment = go.Flags.Contains( GameObjectFlags.Attachment );
		var isNetworked = go.Scene.IsEditor ? go.NetworkMode == NetworkMode.Object : go.Network.Active;
		var isNetworkRoot = isNetworked && (go.Scene.IsEditor ? go.NetworkMode == NetworkMode.Object : go.IsNetworkRoot);
		var isErrored = go.Flags.Contains( GameObjectFlags.Error );
		var isLoading = go.Flags.Contains( GameObjectFlags.Loading );
		var isTemporary = go.Flags.Contains( GameObjectFlags.NotSaved );
		var isEditorOnly = go.Flags.Contains( GameObjectFlags.EditorOnly );

		var opacity = 0.9f;
		if ( !go.Active ) opacity *= 0.5f;

		var pen = Theme.TextControl;
		var icon = "layers";
		var iconColor = Theme.TextControl.WithAlpha( 0.6f );
		string overlayIcon = null;
		var prefabModified = false;

		if ( go.IsPrefabInstance )
		{
			pen = Theme.Prefab;

			if ( go.IsPrefabInstanceRoot )
			{
				icon = "dataset";
				iconColor = Theme.Prefab;
				prefabModified = EditorUtility.Prefabs.IsInstanceModified( go );
			}

			if ( EditorUtility.Prefabs.IsGameObjectAddedToInstance( go ) )
				overlayIcon = "add_circle";
		}

		if ( isBone )
		{
			icon = "polyline";
			iconColor = Theme.Pink.WithAlpha( 0.8f );
			if ( isProceduralBone ) iconColor = Theme.Blue.WithAlpha( 0.8f );
		}

		if ( isAttachment )
		{
			icon = "push_pin";
			iconColor = Theme.Pink.WithAlpha( 0.8f );
			if ( isProceduralBone ) iconColor = Theme.Blue.WithAlpha( 0.8f );
		}

		if ( isTemporary )
		{
			iconColor = Color.White.WithAlpha( 0.5f );
			pen = iconColor.Lighten( 0.2f ).WithAlpha( 0.8f );
			icon = "no_sim";
		}

		if ( isNetworked )
		{
			icon = "rss_feed";
			iconColor = Theme.Blue.WithAlpha( 0.8f );
			if ( go.Network.IsOwner ) iconColor = Theme.Green.WithAlpha( 0.8f );
			if ( go.IsProxy ) iconColor = Theme.TextControl.WithAlpha( 0.6f );
			if ( !isNetworkRoot ) iconColor = iconColor.WithAlphaMultiplied( 0.4f );
		}

		if ( isErrored )
		{
			icon = "report";
			iconColor = Theme.Red.WithAlpha( 0.8f );
		}

		if ( isEditorOnly )
		{
			icon = "highlight_alt";
			iconColor = Theme.Yellow.WithAlpha( 0.8f );
			pen = Theme.Yellow.WithAlpha( 0.6f );
		}

		var dimmed = false;
		if ( tree?.IsDragging == true )
		{
			var data = tree.CurrentDrag?.Data;
			if ( data?.Object is GameObject[] gos && gos.Any( go.IsAncestor ) )
				dimmed = true;
			else if ( data?.Object is GameObject dragged && go.IsAncestor( dragged ) )
				dimmed = true;
		}

		if ( dimmed ) opacity *= 0.23f;

		iconColor = iconColor.WithAlphaMultiplied( opacity );
		pen = pen.WithAlphaMultiplied( opacity );

		string networkOwner = null;
		if ( isNetworkRoot && go.Network.OwnerId != Guid.Empty )
		{
			var connection = Connection.Find( go.Network.OwnerId );
			networkOwner = connection is null
				? $"Unknown Owner - {go.Network.OwnerId}"
				: connection.DisplayName;
		}

		return new HierarchyRowAppearance
		{
			Icon = icon,
			OverlayIcon = overlayIcon,
			IconColor = iconColor,
			TextColor = pen,
			PrefabModified = prefabModified,
			Dimmed = dimmed,
			Inactive = !go.Active,
			ShowHiddenTag = go.Tags.Has( "hidden" ),
			ShowLoading = isLoading,
			NetworkOwner = networkOwner,
		};
	}

	public static HierarchyRowAppearance ForScene( Scene scene )
	{
		return new HierarchyRowAppearance
		{
			Icon = "perm_media",
			IconColor = Theme.TextControl.WithAlpha( 0.4f ),
			TextColor = Theme.TextControl.WithAlpha( 0.4f ),
		};
	}

	public static HierarchyRowAppearance ForPrefab( PrefabScene scene )
	{
		return new HierarchyRowAppearance
		{
			Icon = "circle",
			IconColor = Theme.Prefab,
			TextColor = Theme.TextControl,
		};
	}
}

