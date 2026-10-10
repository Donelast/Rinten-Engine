namespace Rinten;

//Animation agnostic; hold-type animation belongs in layers on top, e.g. BaseCombatWeapon
/// <summary>Slot-based, host-authoritative inventory of child-GameObject items with one active.</summary>
[Expose]
[Icon( "inventory_2" )]
[Group( "Game" )]
[Title( "Inventory" )]
[Alias( "Rinten.InventoryComponent" )]
public partial class BaseInventoryComponent : Component
{
	/// <summary>How an inventory assigns its slots.</summary>
	public enum InventoryBehaviour
	{
		/// <summary>One item per slot, selected directly.</summary>
		[Title( "Hotbar" ), Icon( "view_column" )]
		[Description( "One item per slot, selected directly - like Rust or Minecraft. An item added without an explicit slot takes its preferred slot if free, otherwise the first empty one, and a full inventory refuses new items." )]
		Hotbar,

		/// <summary>Slots are category buckets that any number of items share.</summary>
		[Title( "Buckets" ), Icon( "view_module" )]
		[Description( "Slots are category buckets that any number of items share, sorted by their SlotOrder - like Half-Life 2's weapon list. Items always land in their preferred slot and buckets never fill up." )]
		Buckets,
	}

	/// <summary>How this inventory assigns its slots - exclusive hotbar slots or shared buckets.</summary>
	[Property] public InventoryBehaviour Behaviour { get; set; } = InventoryBehaviour.Hotbar;

	/// <summary>How many slots this inventory has. Items occupy slots 0..MaxSlots-1.</summary>
	[Property] public int MaxSlots { get; set; } = 6;

	/// <summary>Items by slot then SlotOrder, inactive included; excludes removed and nested-inventory items</summary>
	public IEnumerable<BaseInventoryItem> Items =>
		GetComponentsInChildren<BaseInventoryItem>( true ).Where( x => !x.GameObject.IsDestroyed && x.Inventory == this ).OrderBy( x => x.Slot ).ThenBy( x => x.SlotOrder );

	/// <summary>The item that is currently active (deployed), or null when nothing is held. Setting this is host authoritative - use <see cref="Switch"/>.</summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( OnActiveItemChanged ) )]
	public BaseInventoryItem ActiveItem { get; private set; }

	/// <summary>Fired on every peer when the active item changes, with (old, new). Either may be null.</summary>
	public event Action<BaseInventoryItem, BaseInventoryItem> ActiveItemChanged;

	BaseInventoryItem _equippedItem;

	[Expose]
	private void OnActiveItemChanged( BaseInventoryItem _, BaseInventoryItem newItem )
	{
		//Initial snapshots don't run Change callbacks. Track what was actually equipped locally.
		var oldItem = _equippedItem;
		if ( oldItem == newItem )
			return;

		_equippedItem = newItem;

		if ( oldItem.IsValid() )
		{
			oldItem.GameObject.Enabled = false;
			oldItem.Holster();
		}

		if ( newItem.IsValid() )
		{
			newItem.GameObject.Enabled = true;
			newItem.Equip();
		}

		ActiveItemChanged?.Invoke( oldItem, newItem );
	}
}
