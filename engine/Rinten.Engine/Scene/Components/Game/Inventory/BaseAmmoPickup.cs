namespace Rinten;

/// <summary>Pickup that adds reserve ammo to the collector's pool instead of taking a slot; refused when full.</summary>
[Title( "Ammo Pickup" )]
[Group( "Game" )]
[Icon( "workspaces" )]
[Alias( "Rinten.AmmoPickup" )]
public class BaseAmmoPickup : BaseInventoryItem
{
	/// <summary>The reserve ammo type this grants.</summary>
	[Property] public BaseAmmoResource AmmoType { get; set; }

	/// <summary>Rounds granted on pickup.</summary>
	[Property] public int Amount { get; set; } = 30;

	//No point offering ammo the collector can't carry.
	protected override bool OnCanPickup( BaseInventoryComponent inventory )
	{
		if ( AmmoType is null || Amount <= 0 )
			return false;

		return inventory.GetAmmo( AmmoType ) < AmmoType.MaxReserve;
	}

	//Never joins the inventory - donates into the reserve pool and is consumed. Runs on the
	//host for every add, so a partial fill (pool nearly full) still consumes the pickup.
	protected override bool OnAdding( BaseInventoryComponent inventory )
	{
		if ( AmmoType is not null && inventory.GiveAmmo( AmmoType, Amount ) > 0 )
			GameObject.Destroy();

		return false;
	}
}
