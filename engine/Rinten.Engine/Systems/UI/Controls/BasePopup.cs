namespace Rinten.UI;

/// <summary>A panel that gets deleted automatically when clicked away from</summary>
public abstract class BasePopup : Panel
{
	static List<BasePopup> AllPopups = new();

	/// <summary>Stay open, even when CloseAll popups is called.</summary>
	public bool StayOpen { get; set; }

	/// <summary>Panel that opened this popup; clicks on it do not dismiss via CloseAll.</summary>
	public Panel AnchorPanel
	{
		get => anchorPanel;
		set
		{
			anchorPanel = value;
			OpenedFrom = value?.AncestorsAndSelf.OfType<BasePopup>().FirstOrDefault();
		}
	}

	Panel anchorPanel;

	/// <summary>Popup that contained the anchor when this popup was opened.</summary>
	public BasePopup OpenedFrom { get; set; }

	//Lets open overlay menus keep popups they were opened from.
	internal static Action<HashSet<BasePopup>> SparePopupsForInteraction;

	public static void CloseAll( Panel exceptThisOne = null )
	{
		if ( AllPopups.Count == 0 )
			return;

		AllPopups.RemoveAll( x => !x.IsValid );

		var spared = exceptThisOne is Panel clicked ? BuildSparedSet( clicked ) : new HashSet<BasePopup>();

		SparePopupsForInteraction?.Invoke( spared );

		foreach ( var panel in AllPopups.ToArray() )
		{
			if ( spared.Contains( panel ) ) continue;
			if ( panel.StayOpen && panel.Parent.IsValid() ) continue;
			if ( exceptThisOne is Panel panelClicked && ClickIsOnAnchor( panel, panelClicked ) ) continue;

			try
			{
				AllPopups.Remove( panel );
				panel.Delete();
			}
			catch
			{
				//ignored
			}
		}
	}

	static HashSet<BasePopup> BuildSparedSet( Panel clicked )
	{
		var spared = new HashSet<BasePopup>();

		foreach ( var popup in clicked.AncestorsAndSelf.OfType<BasePopup>() )
			spared.Add( popup );

		bool changed;
		do
		{
			changed = false;

			foreach ( var popup in spared.ToArray() )
			{
				if ( popup.OpenedFrom is { IsValid: true } host && spared.Add( host ) )
					changed = true;

				var anchor = popup.AnchorPanel;
				if ( anchor is not { IsValid: true } ) continue;

				foreach ( var candidate in AllPopups )
				{
					if ( !candidate.IsValid || spared.Contains( candidate ) ) continue;

					if ( IsDescendant( candidate, anchor ) && spared.Add( candidate ) )
						changed = true;
				}
			}
		}
		while ( changed );

		return spared;
	}

	internal static void SparePopupsContaining( Panel panel, HashSet<BasePopup> spared )
	{
		if ( panel is not { IsValid: true } )
			return;

		AllPopups.RemoveAll( x => !x.IsValid );

		foreach ( var popup in AllPopups )
		{
			if ( !popup.IsValid || spared.Contains( popup ) )
				continue;

			if ( IsDescendant( popup, panel ) )
				spared.Add( popup );
		}
	}

	static bool IsDescendant( Panel ancestor, Panel descendant )
	{
		foreach ( var panel in descendant.AncestorsAndSelf )
		{
			if ( panel == ancestor ) return true;
		}

		return false;
	}

	static bool ClickIsOnAnchor( BasePopup popup, Panel clicked )
	{
		var anchor = popup.AnchorPanel;

		if ( anchor is null || !anchor.IsValid() )
			return false;

		foreach ( var panel in clicked.AncestorsAndSelf )
		{
			if ( panel == anchor )
				return true;
		}

		return false;
	}

	public BasePopup()
	{
		AllPopups.Add( this );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		AllPopups.Remove( this );
	}
}
