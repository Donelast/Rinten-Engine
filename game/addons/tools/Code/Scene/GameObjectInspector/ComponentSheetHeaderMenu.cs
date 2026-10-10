namespace Editor.Inspectors;

static class ComponentSheetHeaderMenu
{
	public static void Fill( Menu menu, ComponentSheetPanel sheet, Component component, SerializedObject targetObject )
	{
		AddViewOptions( menu, sheet );

		if ( sheet.ListPanel is not null )
			AddLayoutOptions( sheet.ListPanel, component, menu );

		if ( targetObject.IsMultipleTargets )
			FillMultiple( menu, component, targetObject );
		else
			FillSingle( menu, sheet, component, targetObject.TypeName );
	}

	static void AddViewOptions( Menu menu, ComponentSheetPanel sheet )
	{
		{
			var legacy = menu.AddOption( "View Legacy Actions", "sentiment_very_dissatisfied" );
			legacy.Checkable = true;
			legacy.Checked = sheet.ViewMode == ComponentViewMode.Events;
			legacy.Toggled = on => sheet.SetViewMode( on ? ComponentViewMode.Events : ComponentViewMode.Default );
		}

		{
			var advanced = menu.AddOption( "View Advanced Properties", "tune" );
			advanced.Checkable = true;
			advanced.Checked = sheet.ShowAdvanced;
			advanced.Toggled = on => sheet.SetShowAdvanced( on );
		}

		menu.AddSeparator();
	}

	static void AddLayoutOptions( ComponentListPanel listPanel, Component component, Menu menu )
	{
		menu.AddOption( "Collapse Others", "unfold_less", () => listPanel.CollapseOthers( component ) )
			.Enabled = listPanel.SheetCount > 1;

		menu.AddOption( "Collapse All", "unfold_less", () => listPanel.CollapseAll() );
		menu.AddOption( "Expand All", "unfold_more", () => listPanel.ExpandAll() );
		menu.AddSeparator();
	}

	static void FillSingle( Menu menu, ComponentSheetPanel sheet, Component component, string title )
	{
		var gameObject = component.GameObject;
		var componentList = gameObject.Components;
		var editable = !component.Flags.Contains( ComponentFlags.NotEditable );

		if ( editable )
		{
			menu.AddOption( "Reset", "restart_alt", () => ResetComponents( component ) );
			menu.AddSeparator();
			AddMoveOptions( menu, component, componentList, sheet.ListPanel );
			menu.AddSeparator();
			menu.AddOption( $"Cut {title}", "content_cut", () => CutComponent( component ) );
		}

		if ( component is SkinnedModelRenderer { Model: { } model } && !string.IsNullOrEmpty( model.ResourcePath ) )
		{
			var path = model.ResourcePath;
			menu.AddOption( "Open Animation Graph", "account_tree", () => EditorEvent.Run( "model.opengraph", path ) );
			menu.AddSeparator();
		}

		menu.AddOption( $"Copy {title}", "copy_all", () => component.CopyToClipboard() );

		if ( editable )
		{
			var hasClipboard = SceneEditor.HasComponentInClipboard();
			menu.AddOption( "Paste Values", "content_paste", () => component.PasteValues() ).Enabled = hasClipboard;
			menu.AddOption( "Paste As New", "content_paste_go", () => gameObject.PasteComponent() ).Enabled = hasClipboard;
		}

		menu.AddSeparator();

		if ( gameObject.IsPrefabInstance )
			AddPrefabOptions( menu, component, gameObject );

		if ( editable )
		{
			menu.AddOption( "Remove Component", "remove", () => DestroyComponents( component ) );
			AddReplaceMenu( menu, component, $"Replace {component.GetType().Name} Component" );
		}

		menu.AddSeparator();
		AddOpenSourceOption( menu, component );
	}

	static void FillMultiple( Menu menu, Component component, SerializedObject targetObject )
	{
		var components = GetMatchingComponents( component, targetObject ).ToArray();
		if ( components.Length == 0 ) return;

		var editable = !components.Any( x => x.Flags.Contains( ComponentFlags.NotEditable ) );

		if ( editable )
		{
			menu.AddOption( "Reset All", "restart_alt", () => ResetComponents( components ) );
			menu.AddSeparator();
			menu.AddOption( "Remove Components", "remove", () => DestroyComponents( components ) );
			AddReplaceMenu( menu, components, "Replace Component(s)" );

			if ( SceneEditor.HasComponentInClipboard() )
			{
				menu.AddOption( "Paste Values", "content_paste", () =>
				{
					foreach ( var c in components )
						c.PasteValues();
				} );
				menu.AddOption( "Paste As New", "content_paste_go", () =>
				{
					foreach ( var c in components )
						c.GameObject.PasteComponent();
				} );
			}
		}

		menu.AddSeparator();
		AddOpenSourceOption( menu, component );
	}

	static IEnumerable<Component> GetMatchingComponents( Component component, SerializedObject targetObject )
	{
		var fromTarget = targetObject.Targets.OfType<Component>().Where( x => x.IsValid() ).ToArray();
		return fromTarget.Length > 0 ? fromTarget : [component];
	}

	static void AddMoveOptions( Menu menu, Component component, ComponentList componentList, ComponentListPanel listPanel )
	{
		var componentIndex = componentList.GetAll().ToList().IndexOf( component );
		var canMoveUp = componentList.Count > 1 && componentIndex > 0;
		var canMoveDown = componentList.Count > 1 && componentIndex < componentList.Count - 1;

		menu.AddOption( "Move to Top", "vertical_align_top", () =>
		{
			MoveComponent( component, -componentIndex, listPanel );
		} ).Enabled = canMoveUp;

		menu.AddOption( "Move Up", "expand_less", () =>
		{
			MoveComponent( component, -1, listPanel );
		} ).Enabled = canMoveUp;

		menu.AddOption( "Move Down", "expand_more", () =>
		{
			MoveComponent( component, +1, listPanel );
		} ).Enabled = canMoveDown;

		menu.AddOption( "Move to Bottom", "vertical_align_bottom", () =>
		{
			MoveComponent( component, componentList.Count - componentIndex - 1, listPanel );
		} ).Enabled = canMoveDown;
	}

	static void MoveComponent( Component component, int delta, ComponentListPanel listPanel )
	{
		var session = SceneEditorSession.Resolve( component.GameObject );
		using var scene = session.Scene.Push();
		using ( session.UndoScope( "Change Component Order" ).WithGameObjectChanges( component.GameObject, GameObjectUndoFlags.Components ).Push() )
			component.Components.Move( component, delta );

		listPanel?.ReorderSheets();
	}

	static void ResetComponents( Component component ) => ResetComponents( [component] );

	static void ResetComponents( IEnumerable<Component> components )
	{
		var list = components.Where( x => x.IsValid() ).ToArray();
		if ( list.Length == 0 ) return;

		var session = SceneEditorSession.Resolve( list[0] );
		using var scene = session.Scene.Push();
		using ( session.UndoScope( "Reset Component(s)" ).WithComponentChanges( list ).Push() )
		{
			foreach ( var c in list )
				c.Reset();
		}
	}

	static void CutComponent( Component component )
	{
		var session = SceneEditorSession.Resolve( component.GameObject );
		using var scene = session.Scene.Push();
		using ( session.UndoScope( $"Cut {component.GetType().Name} Component" ).WithComponentDestructions( component ).Push() )
		{
			component.CopyToClipboard();
			component.Destroy();
		}
	}

	static void DestroyComponents( Component component ) => DestroyComponents( [component] );

	static void DestroyComponents( IEnumerable<Component> components )
	{
		var list = components.Where( x => x.IsValid() ).ToArray();
		if ( list.Length == 0 ) return;

		var session = SceneEditorSession.Resolve( list[0] );
		using var scene = session.Scene.Push();
		using ( session.UndoScope( "Removed Component(s)" ).WithComponentDestructions( list ).Push() )
		{
			foreach ( var c in list )
				c.Destroy();
		}
	}

	static void AddPrefabOptions( Menu menu, Component component, GameObject gameObject )
	{
		var isModified = EditorUtility.Prefabs.IsComponentInstanceModified( component );

		menu.AddOption( "Revert Changes", "history", () =>
		{
			var session = SceneEditorSession.Resolve( gameObject );
			using var scene = session.Scene.Push();
			using ( session.UndoScope( "Revert Changes" ).WithComponentChanges( component ).Push() )
				EditorUtility.Prefabs.RevertComponentInstanceChanges( component );
		} ).Enabled = isModified;

		menu.AddOption( "Apply to Prefab", "save", () =>
		{
			var session = SceneEditorSession.Resolve( gameObject );
			using var scene = session.Scene.Push();
			EditorUtility.Prefabs.ApplyComponentInstanceChangesToPrefab( component );
		} ).Enabled = isModified;

		menu.AddSeparator();
	}

	static void AddReplaceMenu( Menu menu, Component component, string undoName )
		=> AddReplaceMenu( menu, [component], undoName );

	static void AddReplaceMenu( Menu menu, IEnumerable<Component> components, string undoName )
	{
		var list = components.Where( x => x.IsValid() ).ToArray();
		if ( list.Length == 0 ) return;

		var replace = menu.AddMenu( "Replace Component", "find_replace" );
		replace.AddWidget( new ComponentReplaceMenuWidget( replace )
		{
			OnComponentSelect = type =>
			{
				var session = SceneEditorSession.Resolve( list[0] );
				using var scene = session.Scene.Push();
				using ( session.UndoScope( undoName ).WithComponentDestructions( list ).WithComponentCreations().Push() )
				{
					foreach ( var c in list )
					{
						var go = c.GameObject;
						var jso = c.Serialize().AsObject();
						c.Destroy();
						var newComponent = go.Components.Create( type );
						newComponent.DeserializeImmediately( jso );
					}
				}
			}
		} );
	}

	static void AddOpenSourceOption( Menu menu, Component component )
	{
		var t = EditorTypeLibrary.GetType( component.GetType() );
		if ( t?.SourceFile is null ) return;

		var isPackage = component.GetType().Assembly.IsPackage();
		var filename = System.IO.Path.GetFileName( t.SourceFile );
		menu.AddOption( $"Open {filename}", "code", () => CodeEditor.OpenFile( t ) ).Enabled = isPackage;
	}
}
