namespace Rinten;

public partial class BaseInventoryComponent
{
	/// <summary>Returns the item in the given slot, or null if the slot is empty. When items share the slot (a buckets inventory), the lowest <see cref="BaseInventoryItem.SlotOrder"/> wins.</summary>
	public BaseInventoryItem GetSlot( int slot )
	{
		if ( slot < 0 || slot >= MaxSlots )
			return null;

		//Straight scan - finding one slot doesn't need Items' sort. HUDs call this every frame.
		BaseInventoryItem first = null;

		foreach ( var item in GetComponentsInChildren<BaseInventoryItem>( true ) )
		{
			if ( item.Slot != slot || item.GameObject.IsDestroyed || item.Inventory != this )
				continue;

			if ( first is null || item.SlotOrder < first.SlotOrder )
				first = item;
		}

		return first;
	}

	/// <summary>Returns every item in the given slot, ordered by <see cref="BaseInventoryItem.SlotOrder"/>. One or none in a hotbar inventory; the bucket's contents in a buckets one.</summary>
	public IEnumerable<BaseInventoryItem> GetSlotItems( int slot ) => Items.Where( x => x.Slot == slot );

	/// <summary>Returns the first empty slot index, or -1 if the inventory is full.</summary>
	public int FindEmptySlot()
	{
		if ( MaxSlots <= 0 )
			return -1;

		//One pass over the items, marking the taken slots.
		Span<bool> taken = MaxSlots <= 64 ? stackalloc bool[MaxSlots] : new bool[MaxSlots];

		foreach ( var item in GetComponentsInChildren<BaseInventoryItem>( true ) )
		{
			if ( item.GameObject.IsDestroyed || item.Inventory != this )
				continue;

			if ( item.Slot >= 0 && item.Slot < MaxSlots )
				taken[item.Slot] = true;
		}

		for ( int i = 0; i < MaxSlots; i++ )
		{
			if ( !taken[i] )
				return i;
		}

		return -1;
	}

	/// <summary>Returns the first item of the given type in the inventory (lowest slot wins), or null.</summary>
	public T GetItem<T>()
	{
		//Straight scan - HUDs call these every frame, don't pay Items' sort.
		BaseInventoryItem first = null;

		foreach ( var item in GetComponentsInChildren<BaseInventoryItem>( true ) )
		{
			if ( item is not T || item.GameObject.IsDestroyed || item.Inventory != this )
				continue;

			if ( first is null || item.Slot < first.Slot || (item.Slot == first.Slot && item.SlotOrder < first.SlotOrder) )
				first = item;
		}

		return first is T found ? found : default;
	}

	/// <summary>Returns whether the inventory contains an item of the given type.</summary>
	public bool HasItem<T>()
	{
		foreach ( var item in GetComponentsInChildren<BaseInventoryItem>( true ) )
		{
			if ( item is T && !item.GameObject.IsDestroyed && item.Inventory == this )
				return true;
		}

		return false;
	}

	/// <summary>Highest-value switchable item, avoiding ShouldAvoid ones if possible; runs on the host</summary>
	public virtual BaseInventoryItem GetBestItem()
	{
		var candidates = Items.Where( x => x.CanSwitchTo() ).ToList();

		return candidates.Where( x => !x.ShouldAvoid ).OrderByDescending( x => x.Value ).FirstOrDefault()
			?? candidates.OrderByDescending( x => x.Value ).FirstOrDefault();
	}
}
