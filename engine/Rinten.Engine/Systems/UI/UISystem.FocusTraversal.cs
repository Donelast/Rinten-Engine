using Rinten.UI;

namespace Rinten;

partial class UISystem
{
	//Move focus to the next/previous panel in tab order (tree order, positive TabIndex first), wrapping
	internal bool MoveFocus( Panel from, bool backwards )
	{
		var root = from?.FindRootPanel() ?? RootPanels.FirstOrDefault( x => x.IsValid() && !x.ExternallyDriven );
		if ( root is null ) return false;

		var order = new List<Panel>();
		CollectTabOrder( root, from, order );

		//OrderBy is stable, so tree order survives among equals
		order = order.OrderBy( x => x.TabIndex > 0 ? x.TabIndex : int.MaxValue ).ToList();

		var index = from is null ? -1 : order.IndexOf( from );
		var step = backwards ? -1 : 1;

		Panel target;
		if ( index < 0 )
		{
			target = backwards ? order.LastOrDefault() : order.FirstOrDefault();
		}
		else
		{
			target = order[(index + step + order.Count) % order.Count];
		}

		if ( target is null || target == from )
			return false;

		InputFocus.Set( target );
		target.ScrollAncestorsIntoView();
		return true;
	}

	//Walk the tree in order collecting everything Tab can land on. always is included even when it can't, so a move can start from a panel Tab would skip.
	static void CollectTabOrder( Panel panel, Panel always, List<Panel> order )
	{
		if ( panel == always )
		{
			order.Add( panel );
		}
		else if ( !panel.IsVisible )
		{
			return;
		}
		else if ( panel.AcceptsFocus && panel.TabIndex >= 0 )
		{
			order.Add( panel );
		}

		for ( int i = 0; i < panel.ChildrenCount; i++ )
		{
			CollectTabOrder( panel.GetChild( i ), always, order );
		}
	}
}
