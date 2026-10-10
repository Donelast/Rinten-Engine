namespace Editor;

//Component type picker embedded in a Replace Component submenu.
internal sealed class ComponentReplaceMenuWidget : AdvancedDropdownWidget
{
	const string CategorySeparator = "/";
	const string NoCategoryName = "Uncategorized";

	bool closed;

	public Action<TypeDescription> OnComponentSelect { get; set; }

	public ComponentReplaceMenuWidget( Widget parent ) : base( parent )
	{
		SearchPlaceholderText = "Search Components";
		RootTitle = "Component";
		ContentSize = new Vector2( 300, 400 );
		OnBuildItems = BuildComponentTree;
		SearchScorer = ComponentSearchScore;
		OnSelect = value =>
		{
			if ( value is TypeDescription type )
				OnComponentSelect?.Invoke( type );
		};
		OnFinished = () => closed = true;
		Rebuild();
	}

	protected override void OnMouseReleased( MouseEvent e )
	{
		base.OnMouseReleased( e );
		e.Accepted = !closed;
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		base.OnKeyPress( e );
		e.Accepted = !closed;
	}

	void BuildComponentTree( AdvancedDropdownItem root )
	{
		var types = EditorTypeLibrary.GetTypes<Component>()
			.Where( x => !x.IsAbstract && !x.HasAttribute<HideAttribute>() && !x.HasAttribute<ObsoleteAttribute>() );

		var hideBase = EditorCookie.Get<bool>( "ComponentSelector.HideBase", false );
		if ( hideBase )
			types = types.Where( x => !x.FullName.StartsWith( "Rinten." ) );

		if ( EditorCookie.Get<bool>( "ComponentSelector.FlatView", false ) )
		{
			foreach ( var type in types.OrderBy( x => x.Title.AutoLocalize() ) )
				root.Add( CreateComponentItem( type ) );
			return;
		}

		var categories = types
			.Select( x => string.IsNullOrWhiteSpace( x.Group ) ? NoCategoryName : x.Group )
			.Distinct()
			.OrderBy( x => x.AutoLocalize() )
			.ToArray();

		if ( categories.Length <= 1 )
		{
			foreach ( var type in types.OrderBy( x => x.Title.AutoLocalize() ) )
				root.Add( CreateComponentItem( type ) );
			return;
		}

		var categoryNodes = new Dictionary<string, AdvancedDropdownItem>();

		foreach ( var category in categories )
		{
			var topLevel = category.Split( CategorySeparator ).FirstOrDefault() ?? NoCategoryName;

			if ( !categoryNodes.TryGetValue( topLevel, out var categoryNode ) )
			{
				categoryNode = new AdvancedDropdownItem( topLevel.AutoLocalize() );
				categoryNodes[topLevel] = categoryNode;
				root.Add( categoryNode );
			}

			var parts = category.Split( CategorySeparator );
			if ( parts.Length > 1 )
			{
				var current = categoryNode;
				for ( int i = 1; i < parts.Length; i++ )
				{
					var subName = parts[i].AutoLocalize();
					var existing = current.Children.FirstOrDefault( x => x.Title == subName && x.HasChildren );
					if ( existing is null )
					{
						existing = new AdvancedDropdownItem( subName );
						current.Add( existing );
					}
					current = existing;
				}

				foreach ( var type in types.Where( x => x.Group == category ).OrderBy( x => x.Title.AutoLocalize() ) )
					current.Add( CreateComponentItem( type ) );
			}
			else
			{
				var categoryTypes = types.Where( x =>
					(category == NoCategoryName) ? (x.Group == null) : (x.Group == category) )
					.OrderBy( x => x.Title.AutoLocalize() );

				foreach ( var type in categoryTypes )
					categoryNode.Add( CreateComponentItem( type ) );
			}
		}
	}

	static AdvancedDropdownItem CreateComponentItem( TypeDescription type ) => new()
	{
		Title = type.Title.AutoLocalize(),
		Icon = type.Icon,
		Description = type.Description,
		Tooltip = $"<b>{type.FullName}</b><br/>{type.Description}",
		Value = type,
		PaintIcon = !string.IsNullOrEmpty( type.Icon )
			? ( rect, opacity ) => type.PaintComponentIcon( rect, opacity )
			: null
	};

	static int ComponentSearchScore( AdvancedDropdownItem item, string[] parts )
	{
		if ( item.Value is not TypeDescription type ) return 0;

		var score = 0;
		var t = type.Title.AutoLocalize().Replace( " ", "" );
		var c = type.ClassName.Replace( " ", "" );
		var d = type.Description.Replace( " ", "" );

		foreach ( var w in parts )
		{
			if ( t.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 10;
			if ( c.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 5;
			if ( d.Contains( w, StringComparison.OrdinalIgnoreCase ) ) score += 1;
		}

		return score;
	}
}
