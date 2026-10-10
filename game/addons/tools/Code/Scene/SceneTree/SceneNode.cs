namespace Editor;

partial class SceneNode : GameObjectNode
{
	public SceneNode( Scene scene ) : base( scene )
	{
	}

	public override bool OnContextMenu()
	{
		var m = new ContextMenu( MenuHost );

		m.AddSeparator();

		GameObjectNode.AddGameObjectMenuItems( m, this );

		m.OpenAtCursor( false );

		return true;
	}

	public override bool OnDragStart() => false;
}
