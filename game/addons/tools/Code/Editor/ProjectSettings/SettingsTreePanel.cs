namespace Editor.ProjectSettingsUI;
[StyleSheet( "SettingsTreePanel.cs.scss" )]
public sealed class SettingsTreePanel : TreePanel<SettingsTreeItem>
{
	public Project Project { get; set; }

	IReadOnlyList<SettingsTreeItem> roots = [];
	int cachedRevision = -1;

	protected override int Revision => SettingsTreeBuilder.GetStructureHash( Project );

	protected override IEnumerable<SettingsTreeItem> Roots
	{
		get
		{
			var revision = Revision;
			if ( cachedRevision != revision )
			{
				cachedRevision = revision;
				roots = SettingsTreeBuilder.Build( Project );
			}

			return roots;
		}
	}

	protected override IEnumerable<SettingsTreeItem> GetChildren( SettingsTreeItem node ) => node.Children;

	//A section names a group of settings, so it is a heading.
	protected override TreeItemInfo GetInfo( SettingsTreeItem node )
		=> new( node.Title, node.Icon, node.HasChildren ) { IsHeading = node.Kind == SettingsTreeItemKind.Section, Tooltip = node.Description };

	public Action<IReadOnlyList<SettingsTreeItem>> SelectionChanged;

	protected override void OnSelectionChanged( IReadOnlyList<SettingsTreeItem> selected )
	{
		base.OnSelectionChanged( selected );
		SelectionChanged?.Invoke( selected );
	}

	public void SelectFirstCategory()
	{
		foreach ( var root in Roots )
		{
			if ( TrySelectFirst( root ) )
				return;
		}
	}

	//So a menu entry elsewhere in the editor can land on the page it is about - Build Project from the File menu is the one that does - rather than on whatever the tree happens to open with.
	/// <summary>Opens the page of that name, or the first one where there is no such page.</summary>
	public void SelectCategory( string title )
	{
		if ( !string.IsNullOrWhiteSpace( title ) )
		{
			foreach ( var root in Roots )
			{
				if ( TrySelectNamed( root, title ) )
					return;
			}
		}

		SelectFirstCategory();
	}

	bool TrySelectNamed( SettingsTreeItem node, string title )
	{
		Open( node );

		if ( node.CategoryTypes.Count > 0 && string.Equals( node.Title, title, System.StringComparison.OrdinalIgnoreCase ) )
		{
			Select( [node] );
			return true;
		}

		foreach ( var child in node.Children )
		{
			if ( TrySelectNamed( child, title ) )
				return true;
		}

		return false;
	}

	bool TrySelectFirst( SettingsTreeItem node )
	{
		Open( node );

		foreach ( var child in node.Children )
		{
			if ( TrySelectFirst( child ) )
				return true;
		}

		if ( node.CategoryTypes.Count > 0 && node.Kind != SettingsTreeItemKind.Section )
		{
			Select( [node] );
			return true;
		}

		if ( node.Kind == SettingsTreeItemKind.Section && node.CategoryTypes.Count > 0 && !node.HasChildren )
		{
			Select( [node] );
			return true;
		}

		return false;
	}
}
