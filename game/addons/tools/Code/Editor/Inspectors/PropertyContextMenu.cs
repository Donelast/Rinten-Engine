using Editor;

namespace Editor.Inspectors;

//The one menu a property opens on a right click, wherever it is being edited.
static class PropertyContextMenu
{
	/// <summary>Opens it at the pointer, in the window the anchor is in. False when there was nothing to put in it.</summary>
	public static bool Open( Panel anchor, SerializedProperty property, BaseControl control )
	{
		if ( property is null ) return false;

		var menu = new ContextMenu();

		control?.OnPropertyContextMenu( menu );
		menu.AddSeparator();

		//Everything that writes is still listed for a property that cannot be edited, and greyed out: the menu
		//says the same thing wherever it is opened, and what is missing from it never has to be guessed at.
		var readOnly = IsReadOnly( property, control );

		//A method has no value to copy, paste, reset or apply - only the code it is in.
		if ( !property.IsMethod )
		{
			if ( control is not null )
			{
				menu.AddOption( $"Copy {property.DisplayName}", "content_copy",
					() => EditorUtility.Clipboard.Copy( control.ToClipboardString() ) );

				menu.AddOption( $"Paste as {property.DisplayName}", "content_paste",
					() => control.FromClipboardString( EditorUtility.Clipboard.Paste() ) ).Enabled = !readOnly;
			}

			menu.AddOption( "Reset to Default", "restart_alt", () => ResetToDefault( property ) )
				.Enabled = !readOnly;

			AddPrefabOptions( menu, property, readOnly );
			AddSceneOption( menu, property, readOnly );
		}

		AddSourceOption( menu, property );

		if ( !menu.HasOptions ) return false;

		menu.OpenAtCursor( anchor );
		return true;
	}

	//Read-only per the control if there is one (rows may lock it), else per the property itself
	static bool IsReadOnly( SerializedProperty property, BaseControl control )
	{
		if ( control is not null ) return control.IsReadOnly;

		return IsReadOnly( property );
	}

	//Whether the property refuses to be written, asked the way a row asks it.
	internal static bool IsReadOnly( SerializedProperty property )
	{
		return property is null
			|| !property.IsEditable
			|| property.HasAttribute<ReadOnlyAttribute>()
			|| !property.ShouldEnable();
	}

	static void ResetToDefault( SerializedProperty property )
	{
		property.Parent?.NoteStartEdit( property );
		property.SetValue( property.GetDefault() );
		property.Parent?.NoteFinishEdit( property );
	}

	static bool IsEditingComponent( SerializedProperty property )
		=> EditedComponents( property ).Any();

	static bool IsEditingGameObject( SerializedProperty property )
		=> EditedGameObjects( property ).Any();

	static IEnumerable<Component> EditedComponents( SerializedProperty property )
		=> property.Parent?.Targets?.OfType<Component>().Where( x => x.IsValid() ) ?? Enumerable.Empty<Component>();

	static IEnumerable<GameObject> EditedGameObjects( SerializedProperty property )
	{
		var targets = property.Parent?.Targets;
		if ( targets is null ) return Enumerable.Empty<GameObject>();

		var objects = targets.OfType<GameObject>().Where( x => x.IsValid() );
		var transforms = targets.OfType<GameTransform>().Where( x => x.GameObject.IsValid() ).Select( x => x.GameObject );

		return objects.Concat( transforms );
	}

	static void AddPrefabOptions( Menu menu, SerializedProperty property, bool readOnly )
	{
		var owner = property.GetContainingGameObject();
		if ( owner?.IsPrefabInstance != true ) return;
		if ( !IsEditingComponent( property ) && !IsEditingGameObject( property ) ) return;

		menu.AddSeparator();

		var isModified = !readOnly && EditorUtility.Prefabs.IsPropertyOverridden( property );

		menu.AddOption( "Revert Change", "history", () =>
		{
			var gameObjects = EditedGameObjects( property ).ToArray();
			var components = EditedComponents( property ).ToArray();

			var session = SceneEditorSession.Resolve( owner );
			if ( session is null ) return;

			using var scene = session.Scene.Push();
			using ( session.UndoScope( "Revert Change" )
				.WithComponentChanges( components )
				.WithGameObjectChanges( gameObjects, GameObjectUndoFlags.Properties )
				.Push() )
			{
				EditorUtility.Prefabs.RevertPropertyChange( property );
			}
		} ).Enabled = isModified;

		menu.AddOption( "Apply to Prefab", "save",
			() => EditorUtility.Prefabs.ApplyPropertyChange( property ) ).Enabled = isModified;
	}

	static void AddSourceOption( Menu menu, SerializedProperty property )
	{
		if ( !CodeEditor.CanOpenFile( property.SourceFile ) ) return;

		menu.AddSeparator();
		menu.AddOption( "Jump to code", "code", () => CodeEditor.OpenFile( property.SourceFile, property.SourceLine ) );
	}

	//Puts what is being edited in a running game back into the scene it came from.
	static void AddSceneOption( Menu menu, SerializedProperty property, bool readOnly )
	{
		var component = EditedComponents( property ).FirstOrDefault();
		if ( !component.IsValid() || !component.GameObject.IsValid() ) return;

		var session = SceneEditorSession.Resolve( component.GameObject.Scene );
		if ( session is null ) return;

		var target = session.Scene.Directory.FindComponentByGuid( component.Id );
		if ( !target.IsValid() ) return;

		//The editor's own component is the one being edited - there is nowhere to apply it to.
		if ( ReferenceEquals( target, component ) ) return;

		var editorProperty = target.GetSerialized().GetProperty( property.Name );
		if ( editorProperty is null ) return;

		menu.AddSeparator();

		var option = menu.AddOption( "Apply to Scene", "save", () =>
		{
			using var scope = session.Scene.Push();
			using ( session.UndoScope( "Apply to Scene" ).WithComponentChanges( target ).Push() )
			{
				editorProperty.SetValue<object>( property.GetValue<object>() );
			}
		} );

		option.Enabled = !readOnly
			&& Json.Serialize( editorProperty.GetValue<object>() ) != Json.Serialize( property.GetValue<object>() );
	}
}
