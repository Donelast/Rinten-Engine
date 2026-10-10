namespace Rinten;

/// <summary>Base for inventory items: networked slot, editor metadata and equip/holster hooks.</summary>
[Expose]
[Icon( "category" )]
[Group( "Game" )]
[Title( "Inventory Item" )]
public partial class BaseInventoryItem : Component, Component.IPressable
{
	/// <summary>Display name shown in inventory UI.</summary>
	[Property, Group( "Inventory" )] public string DisplayName { get; set; } = "Item";

	/// <summary>Icon shown in inventory UI.</summary>
	[Property, Group( "Inventory" )] public Texture DisplayIcon { get; set; }

	/// <summary>Relative worth of this item. Used as the priority when the inventory has to pick a "best" item to fall back to (e.g. after the active item is removed). Higher wins.</summary>
	[Property, Group( "Inventory" )] public int Value { get; set; } = 0;

	/// <summary>The slot this item wants when added without an explicit one. In a hotbar inventory it falls back to the first empty slot when taken, with -1 for no preference; in a buckets inventory the preference always wins.</summary>
	[Property, Group( "Inventory" )] public int PreferredSlot { get; set; } = -1;

	/// <summary>Fixed sort position among items sharing a slot, lowest first. Only matters in a buckets inventory - hotbar slots hold one item.</summary>
	[Property, Group( "Inventory" )] public int SlotOrder { get; set; } = 0;

	/// <summary>The slot this item occupies, or -1 if unassigned. Set by the inventory.</summary>
	[Sync( SyncFlags.FromHost )] public int Slot { get; set; } = -1;

	/// <summary>The inventory this item currently belongs to, or null if it isn't in one. Derived from the hierarchy so it's correct on every peer. The nearest ancestor, never this item's own GameObject - an item that is itself an inventory (a backpack) belongs to its holder.</summary>
	public BaseInventoryComponent Inventory => GetComponentInParent<BaseInventoryComponent>( true, false );

	/// <summary>True when this is the active (deployed) item in its inventory.</summary>
	public bool IsActive => Inventory?.ActiveItem == this;

	//Overridable behaviour. These are protected so they can't be invoked by arbitrary game code - only the
	//item itself and its subclasses. The inventory reaches them via the internal dispatchers further down.

	/// <summary>True when the inventory should avoid auto-switching to this item - e.g. a gun with nothing to fire. <see cref="BaseInventoryComponent.GetBestItem"/> falls back to avoided items only when there's nothing better.</summary>
	public virtual bool ShouldAvoid => false;

	//Can the given inventory pick this item up out of the world? Both the Use prompt and Touch pickup consult it,
	//so a refused item shows no prompt - role-locked weapons, class restrictions, quest gates. Base allows it.
	protected virtual bool OnCanPickup( BaseInventoryComponent inventory ) => true;

	//Return false to stop the inventory making this item active.
	protected virtual bool OnCanSwitchTo() => true;

	//Called before switching away from this item; return false to refuse. Not called on forced holsters
	protected virtual bool OnHolstering( BaseInventoryItem next ) => true;

	//Called on every peer when this becomes the active item. Base does nothing.
	protected virtual void OnEquipped() { }

	//Called on every peer when this stops being the active item. Base does nothing.
	protected virtual void OnHolstered() { }

	//Called each frame on the owning client while this is the active item - read input and drive the item's behaviour here (firing, reloading,
	//aiming, etc). Pumped by the inventory; see BaseInventoryComponent.Pump and BaseInventoryComponent.ManualPumping. Base does nothing.
	protected virtual void OnControl() { }

	//About to be added to inventory. Return false to refuse - optionally consuming this item instead (a duplicate weapon donates
	//its ammo, a stackable merges into its stack). Runs on the host for every add - pickups, loadouts, code. Base allows it.
	protected virtual bool OnAdding( BaseInventoryComponent inventory ) => true;

	//Called on the host when added to an inventory. Base does nothing.
	protected virtual void OnAdded( BaseInventoryComponent inventory ) { }

	//Called on the host when removed from an inventory. Base does nothing.
	protected virtual void OnRemoved( BaseInventoryComponent inventory ) { }

	//Places this item in the world after being dropped. The default unparents it (keeping its world position), enables it, drops network ownership,
	//and gives it a small toss if it has a Rigidbody. Return false to refuse the drop (e.g. bound items). Override to customise placement or behaviour.
	protected virtual bool OnDrop()
	{
		GameObject.SetParent( null, true );
		GameObject.Enabled = true;
		Network.DropOwnership();

		if ( GetComponent<Rigidbody>() is { } body )
		{
			body.Velocity += WorldRotation.Forward * 3.8f + Vector3.Up * 2.5f;
			body.AngularVelocity += Vector3.Random * 4f;
		}

		return true;
	}

	/// <summary>True when the inventory is allowed to make this the active item (see <see cref="OnCanSwitchTo"/>). Queryable by UI - a pure check, nothing happens.</summary>
	public bool CanSwitchTo() => OnCanSwitchTo();

	/// <summary>True when the given inventory is allowed to pick this item up (see <see cref="OnCanPickup"/>). Queryable by UI - a pure check, nothing happens.</summary>
	public bool CanPickup( BaseInventoryComponent inventory ) => OnCanPickup( inventory );

	//Internal dispatch. The inventory (same assembly) calls these to invoke the
	//protected hooks above. Game code drives the inventory, never these directly.

	internal bool Adding( BaseInventoryComponent inventory ) => OnAdding( inventory );
	internal bool Holstering( BaseInventoryItem next ) => OnHolstering( next );
	internal void Equip() => OnEquipped();
	internal void Holster() => OnHolstered();
	internal void Control() => OnControl();
	internal void Added( BaseInventoryComponent inventory ) => OnAdded( inventory );
	internal void Removed( BaseInventoryComponent inventory ) => OnRemoved( inventory );

	//Drops this item out of its inventory into the world via OnDrop, clearing its
	//slot. Returns false if the item refused. Driven by BaseInventoryComponent.Drop.
	internal bool Drop()
	{
		if ( !OnDrop() )
			return false;

		Slot = -1;
		return true;
	}

	//Use pickup. An item lying in the world is pressable - pressing it asks the presser's inventory
	//to take it, when that inventory picks up by Use (see BaseInventoryComponent.PickupMode).

	bool Component.IPressable.CanPress( Component.IPressable.Event e ) => PickupInventoryFor( e.Source ) is not null;

	bool Component.IPressable.Press( Component.IPressable.Event e )
	{
		var inventory = PickupInventoryFor( e.Source );
		if ( inventory is null )
			return false;

		inventory.PickupWorldItem( this );
		return true;
	}

	Component.IPressable.Tooltip? Component.IPressable.GetTooltip( Component.IPressable.Event e )
	{
		if ( PickupInventoryFor( e.Source ) is null )
			return null;

		return new Component.IPressable.Tooltip( DisplayName, "touch_app", "Pick up" );
	}

	//The pressing player's inventory, when this item is in the world and they pick up by Use.
	BaseInventoryComponent PickupInventoryFor( Component source )
	{
		if ( Inventory is not null )
			return null;

		var inventory = source?.GetComponentInParent<BaseInventoryComponent>( true );
		if ( !inventory.IsValid() || inventory.PickupMode != BaseInventoryComponent.PickupBehaviour.Use )
			return null;

		//The item gets a say - a refused item shows no prompt at all.
		if ( !CanPickup( inventory ) )
			return null;

		return inventory;
	}
}
