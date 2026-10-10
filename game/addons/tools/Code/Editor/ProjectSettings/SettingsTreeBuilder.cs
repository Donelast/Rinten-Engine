using Editor.ProjectSettingPages.Categories;

namespace Editor.ProjectSettingsUI;
//Builds SettingsTreeItem trees for a project.
static class SettingsTreeBuilder
{
	public static int GetStructureHash( Project project )
	{
		if ( project is null ) return 0;

		var hash = HashCode.Combine( project, project.Config.Type );

		if ( project.Config.Type == "game" )
		{
			hash = HashCode.Combine( hash, TypeLibrary.GetTypes<GameObjectSystem>().Count() );
		}

		return hash;
	}

	public static IReadOnlyList<SettingsTreeItem> Build( Project project )
	{
		if ( project is null ) return [];

		var categoriesBySection = new Dictionary<string, List<(Type type, string icon, string title, string description)>>();

		void AddCategory( Type categoryType, string sectionName )
		{
			if ( !categoriesBySection.TryGetValue( sectionName, out var list ) )
			{
				list = new();
				categoriesBySection[sectionName] = list;
			}

			var typeInfo = EditorTypeLibrary.GetType( categoryType );
			var icon = typeInfo?.Icon ?? "settings";
			var title = typeInfo?.Title ?? categoryType.Name;
			var description = typeInfo?.Description ?? "";
			list.Add( (categoryType, icon, title, description) );
		}

		AddCategory( typeof( ProjectPage ), "Project" );

		if ( project.Config.Type == "game" )
		{
			AddCategory( typeof( GameCategory ), "Project" );
			AddCategory( typeof( SystemsPage ), "Systems" );
			AddCategory( typeof( PhysicsCategory ), "Physics" );
			AddCategory( typeof( InputCategory ), "Input" );
			AddCategory( typeof( MultiplayerCategory ), "Networking" );
			AddCategory( typeof( PlatformCategory ), "Interface" );
			AddCategory( typeof( CompilerCategory ), "Compiler" );
			AddCategory( typeof( ResourcesCategory ), "Other" );
			AddCategory( typeof( ReferencesCategory ), "Other" );
			AddCategory( typeof( CursorCategory ), "Other" );
		}
		else if ( project.Config.Type == "map" )
		{
			AddCategory( typeof( ReferencesCategory ), "Project" );
		}
		else if ( project.Config.Type is "library" or "tool" )
		{
			AddCategory( typeof( CompilerCategory ), "Compiler" );
		}

		var roots = new List<SettingsTreeItem>();

		foreach ( var (sectionName, categories) in categoriesBySection )
		{
			var sectionIcon = categories[0].icon;
			var section = CreateSection( sectionName, sectionIcon );

			if ( categories.Count == 1 )
			{
				var (type, _, _, _) = categories[0];
				var children = ProjectSettingsCategoryPanel.GetTreeChildren( type );

				section.CategoryTypes = [type];

				if ( children is not null )
				{
					foreach ( var child in children )
					{
						section.AddChild( new SettingsTreeItem
						{
							Title = child.Title,
							Icon = child.Icon,
							Kind = SettingsTreeItemKind.Child,
							CategoryTypes = [type],
							Configure = child.Configure
						} );
					}
				}
			}
			else
			{
				section.CategoryTypes = categories.Select( x => x.type ).ToList();

				foreach ( var (categoryType, icon, title, description) in categories )
				{
					var children = ProjectSettingsCategoryPanel.GetTreeChildren( categoryType );

					if ( children is not null && children.Any() )
					{
						var parent = new SettingsTreeItem
						{
							Title = title,
							Icon = icon,
							Description = description,
							Kind = SettingsTreeItemKind.Category,
							CategoryTypes = [categoryType]
						};

						foreach ( var child in children )
						{
							parent.AddChild( new SettingsTreeItem
							{
								Title = child.Title,
								Icon = child.Icon,
								Kind = SettingsTreeItemKind.Child,
								CategoryTypes = [categoryType],
								Configure = child.Configure
							} );
						}

						section.AddChild( parent );
					}
					else
					{
						section.AddChild( new SettingsTreeItem
						{
							Title = title,
							Icon = icon,
							Description = description,
							Kind = SettingsTreeItemKind.Category,
							CategoryTypes = [categoryType]
						} );
					}
				}
			}

			roots.Add( section );
		}

		return roots;
	}

	static SettingsTreeItem CreateSection( string title, string icon ) => new()
	{
		Title = title,
		Icon = icon,
		Kind = SettingsTreeItemKind.Section
	};
}
