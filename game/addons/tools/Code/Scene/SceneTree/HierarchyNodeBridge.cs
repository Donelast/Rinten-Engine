using static Editor.BaseItemWidget;

namespace Editor;

static class HierarchyNodeBridge
{
	public static GameObjectNode Create( object item )
	{
		if ( item is PrefabScene prefab )
			return new PrefabNode( prefab );

		if ( item is Scene scene )
			return new SceneNode( scene );

		if ( item is GameObject go )
			return new GameObjectNode( go );

		return null;
	}

	public static bool ShowContextMenu( object item )
	{
		var node = Create( item );
		return node is not null && node.OnContextMenu();
	}

	public static bool CanDrag( object item ) => Create( item )?.OnDragStart() ?? false;

	public static DropAction OnDragDrop( object target, DragData data, ItemEdge dropEdge, bool isDrop )
		=> Create( target )?.OnPanelDragDrop( data, dropEdge, isDrop ) ?? DropAction.Ignore;

	public static void ApplyRename( object node, string text, object[] selection )
	{
		var nodes = selection.Select( Create ).Where( x => x is not null ).ToList();
		if ( nodes.Count == 0 ) return;

		nodes[0].ApplyRename( text, nodes );
	}
}
