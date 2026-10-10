using System.Collections;
using Rinten.UI;

namespace Editor.UiEditor;

/// <summary>The items of a list the document shows - what a <c>@foreach</c> walks - made and changed here, where they can be looked at: one added, one copied, one taken away, one moved, a field of one set. They are design values, kept beside the razor, so nothing in the game is touched.</summary>
public partial class UiEditorWindow
{
	//A member of the document's type that is a list, by name; null when there is no such list.
	internal (string name, Type type, Type item, Func<object, object> get, Action<object, object> set, DesignDataAttribute sample)? ListMember( string name )
	{
		if ( _document?.Type is not { } type || string.IsNullOrEmpty( name ) ) return null;

		foreach ( var member in DesignData.Members( type ) )
		{
			if ( !string.Equals( member.name, name, StringComparison.Ordinal ) ) continue;
			if ( !DesignData.IsList( member.type, out var item ) ) return null;

			return ( member.name, member.type, item, member.get, member.set, member.sample );
		}

		return null;
	}

	//The list member a repeated tag's items come from: the name its foreach walks when that is a list member; else, when the document
	//has one list member, that one - a foreach over a filtered view of it, Entries for Scenes , still adds to the list underneath.
	internal string ListForForeach( RazorMarkup.Element element )
	{
		if ( element?.Control != "foreach" || _document?.Type is not { } type ) return null;

		if ( element.ForEachMember is { } named && ListMember( named ) is not null ) return named;

		var lists = DesignData.Members( type ).Where( x => DesignData.IsList( x.type, out _ ) ).Select( x => x.name ).ToList();
		return lists.Count == 1 ? lists[0] : null;
	}

	//The list's items as they are on the instance, copied into a list that can be changed.
	internal IList ItemsOf( string member )
	{
		if ( ListMember( member ) is not { } list || Instance is null ) return null;

		var made = DesignData.MakeList( list.type, list.item );

		try
		{
			if ( list.get( Instance ) is IEnumerable items )
				foreach ( var item in items ) made.Add( item );
		}
		catch
		{
			//An instance whose getter throws holds nothing.
		}

		return made;
	}

	//Put a list of items on the instance as the member's own kind of list, keep it, and show it.
	internal void KeepItems( string member, IList items )
	{
		if ( ListMember( member ) is not { } list || Instance is null ) return;

		object value = items;

		if ( list.type.IsArray )
		{
			var array = Array.CreateInstance( list.item, items.Count );
			for ( var i = 0; i < items.Count; i++ ) array.SetValue( items[i], i );
			value = array;
		}

		try
		{
			list.set( Instance, value );
		}
		catch ( Exception e )
		{
			Log.Warning( $"UI editor: {member} cannot take {items.Count} items: {e.Message}" );
			return;
		}

		SetDesignValue( member, value, list.type );
		CanvasPanel?.Rerender();
		_data?.NoteItemsChanged( member );
	}

	//A new item for a list: what its factory makes, else a default of the item type.
	internal object SampleItem( string member, int index )
	{
		if ( ListMember( member ) is not { } list ) return null;

		if ( list.sample?.Factory is { } factoryType && Activator.CreateInstance( factoryType ) is IDesignDataFactory factory )
			return factory.Make( index );

		return DesignData.Default( list.item, index );
	}

	//A copy of an item, through JSON, so the copy is its own.
	internal object CloneItem( string member, object item )
	{
		if ( item is null || ListMember( member ) is not { } list ) return item;
		if ( list.item.IsValueType || list.item == typeof( string ) ) return item;

		try
		{
			return Json.Deserialize( Json.Serialize( item ), list.item );
		}
		catch
		{
			return item;
		}
	}

	internal bool InsertItem( string member, int index, object item = null )
	{
		if ( ItemsOf( member ) is not { } items ) return false;

		index = Math.Clamp( index, 0, items.Count );
		items.Insert( index, item ?? SampleItem( member, index ) );
		KeepItems( member, items );
		return true;
	}

	internal bool DuplicateItem( string member, int index )
	{
		if ( ItemsOf( member ) is not { } items || index < 0 || index >= items.Count ) return false;

		items.Insert( index + 1, CloneItem( member, items[index] ) );
		KeepItems( member, items );
		return true;
	}

	internal bool RemoveItem( string member, int index )
	{
		if ( ItemsOf( member ) is not { } items || index < 0 || index >= items.Count ) return false;

		items.RemoveAt( index );
		KeepItems( member, items );
		return true;
	}

	internal bool MoveItem( string member, int index, int by )
	{
		if ( ItemsOf( member ) is not { } items || index < 0 || index >= items.Count ) return false;

		var to = index + by;
		if ( to < 0 || to >= items.Count ) return false;

		var item = items[index];
		items.RemoveAt( index );
		items.Insert( to, item );
		KeepItems( member, items );
		return true;
	}

	//Set one field of one item and keep the list.
	internal void SetItemField( string member, int index, Action<object> set )
	{
		if ( ItemsOf( member ) is not { } items || index < 0 || index >= items.Count ) return;

		var item = items[index];

		//A struct is changed on a copy, which then goes back in its place.
		if ( item is not null && item.GetType().IsValueType )
		{
			set( item );
			items[index] = item;
		}
		else set( item );

		KeepItems( member, items );
	}

	//Which item of its foreach a panel is: its place among the siblings built from the same tag.
	internal int ItemIndexOf( Panel panel )
	{
		if ( !panel.IsValid() || panel.Parent is null || LocateTag( panel ) is not { } element ) return -1;

		var index = 0;

		foreach ( var sibling in panel.Parent.Children )
		{
			if ( ReferenceEquals( sibling, panel ) ) return index;
			if ( ReferenceEquals( LocateTag( sibling ), element ) ) index++;
		}

		return -1;
	}
}
