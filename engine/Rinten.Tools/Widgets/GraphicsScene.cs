using System;

namespace Editor
{

	internal class GraphicsScene : EditorObject
	{
		internal GraphicsView View;

		internal GraphicsScene( GraphicsView parent )
		{
			View = parent;
		}

		public void Add( GraphicsItem t )
		{
			View.Add( t );
		}

		internal void RemoveInternal( GraphicsItem item )
		{
			View.RemoveManaged( item );
		}


		internal void OnItemSelectionChangedInternal( GraphicsItem v, bool selected )
		{
			//The view is the one keeping the selection when it is ours.
			return;
		}
	}
}
