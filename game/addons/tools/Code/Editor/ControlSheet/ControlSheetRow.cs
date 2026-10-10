using Rinten.UI;

namespace Editor;

//Represents a single row in a control sheet UI, providing editing and validation functionality for a serialized property.
partial class ControlSheetRow : Widget
{
	public ControlWidget ControlWidget { get; private set; }

	//Finds the owning sheet by walking parent layouts.
	internal static ControlSheet SheetOf( Widget widget )
	{
		for ( var w = widget; w is not null; w = w.Parent )
		{
			if ( w.Layout is ControlSheet sheet )
				return sheet;
		}

		return null;
	}

	SerializedProperty property;
	bool showPropertyNameInTooltip;
	ControlSheetLabel label;
	Layout validateResultContainer;
	float labelColumn;
	bool showsLabel;
	Widget propertyButton;
	Panel prefabOverride;

	const float NullableButtonGap = 6.0f;

	public ControlSheetRow( SerializedProperty property, ControlWidget editor )
	{
		this.property = property;
		FocusMode = FocusMode.Click;
		ControlWidget = editor;

		property.OnFinishEdit += OnPropertyFinishEdit;

		Layout = Layout.Column();
		Layout.Spacing = 0;
	}

	public static ControlWidget CreateEditor( SerializedProperty property )
	{
		if ( property.PropertyType.IsAssignableTo( typeof( Resource ) ) )
		{
			return new ResourceWrapperControlWidget( property );
		}

		try
		{
			return ControlWidget.Create( property );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Error creating ControlWidget for {property.Name}" );
		}

		return default;
	}

	public static ControlSheetRow Create( SerializedProperty property, bool showPropertyNameInTooltip = false )
	{
		ControlWidget editor = CreateEditor( property );
		if ( !editor.IsValid() ) return null;

		return Create( property, editor, showPropertyNameInTooltip );
	}

	public static ControlSheetRow Create( SerializedProperty property, ControlWidget editor, bool showPropertyNameInTooltip = false )
	{
		if ( !editor.IsValid() ) return null;

		var row = new ControlSheetRow( property, editor );
		row.showPropertyNameInTooltip = showPropertyNameInTooltip;
		//Room around the control rather than up against it. A row is the height of what is in it plus
		//this, and the inspector reads as a list rather than a stack when there is a little of it.
		row.ContentMargins = new Margin( 12, 0, 1, 0 );

		row.Rebuild();

		return row;
	}

	public bool UpdateVisibility()
	{
		var ss = property.ShouldShow();
		if ( ss == !Hidden )
			return false;

		Hidden = !ss;
		return true;
	}

	public bool UpdateEditability()
	{
		if ( property.HasAttribute<ReadOnlyAttribute>() )
			return false;

		var readOnly = !property.ShouldEnable();
		if ( ControlWidget.ReadOnly == readOnly )
			return false;

		ControlWidget.ReadOnly = readOnly;

		if ( propertyButton.IsValid() )
			propertyButton.Enabled = !readOnly;

		return true;
	}

	public override void ChildValuesChanged( Widget source )
	{
		base.ChildValuesChanged( source );

		if ( ControlSheetRow.SheetOf( this ) is { } sheet )
			sheet.RefreshEditability();
	}

	public void OnPropertyFinishEdit( SerializedProperty property )
	{
		UpdateValidation();
	}

	public override void SyncSurfaceContent()
	{
		if ( prefabOverride is null && ContentPanel is not null )
		{
			prefabOverride = new Panel();
			prefabOverride.AddClass( "prefab-override" );
			prefabOverride.AddClass( "hidden" );
			ContentPanel.AddChild( prefabOverride );
		}

		if ( prefabOverride is null || property is null ) return;

		var component = property.Parent?.Targets?.OfType<Component>().FirstOrDefault();
		var show = EditorUtility.Prefabs.IsPropertyOverridden( property )
			|| EditorUtility.Prefabs.IsComponentAddedToInstance( component );

		prefabOverride.SetClass( "hidden", !show );
	}

	public void Rebuild()
	{
		if ( property is null )
			return;

		float spaceAbove = 0;

		if ( property.TryGetAttribute( out SpaceAttribute spaceAttr ) )
			spaceAbove = spaceAttr.Height;

		var hasLabel = ControlWidget.IncludeLabel;
		var isExpanded = ControlWidget.IsWideMode;
		if ( property.TryGetAttribute<WideModeAttribute>( out var wideMode ) )
		{
			isExpanded = true;
			hasLabel = ControlWidget.IncludeLabel && wideMode.HasLabel;
		}

		Layout.Margin = new Margin( 0, spaceAbove, 1, 0 );

		if ( property.TryGetAttribute( out InfoBoxAttribute infoAttribute ) )
			Layout.Add( new InfoBoxWidget( infoAttribute.Message, infoAttribute.Tint, infoAttribute.Icon ) );

		if ( property.TryGetAttribute( out HeaderAttribute headerAttribute ) )
			Layout.Add( new Label.Header( headerAttribute.Title ) );

		ToolTip = ControlSheetFormatter.GetPropertyToolTip( property, showPropertyNameInTooltip );
		HorizontalSizeMode = SizeMode.CanShrink;
		VerticalSizeMode = SizeMode.CanShrink;

		label = new ControlSheetLabel( property );

		ControlWidget.HorizontalSizeMode = SizeMode.Flexible;

		if ( property.IsNullable )
			ControlWidget.Enabled = !property.IsNull;

		showsLabel = hasLabel;

		if ( hasLabel )
		{
			label.ContentMargins = isExpanded ? new( 0, 0, 0, 4 ) : new( 0, 0, 4, 0 );
			label.MinimumWidth = label.WantedSize().x;
			labelColumn = MathF.Max( labelColumn, label.WantedSize().x );

			if ( isExpanded )
			{
				if ( property.IsNullable )
				{
					var labelRow = Layout.Row();
					labelRow.Spacing = NullableButtonGap;
					propertyButton = labelRow.Add( new PropertyButton( property, ControlWidget ) );
					propertyButton.Enabled = ControlWidget.Enabled;
					labelRow.Add( label );
					Layout.Add( labelRow );
					labelColumn = MathF.Max( labelColumn, label.WantedSize().x + Theme.RowHeight + NullableButtonGap );
				}
				else
					Layout.Add( label );

				Layout.Add( ControlWidget );
			}
			else
			{
				var row = Layout.Row();
				row.Spacing = property.IsNullable ? NullableButtonGap : 0;

				if ( property.IsNullable )
				{
					propertyButton = row.Add( new PropertyButton( property, ControlWidget ) );
					propertyButton.Enabled = ControlWidget.Enabled;
					row.Add( label );
					labelColumn = MathF.Max( labelColumn, label.WantedSize().x + Theme.RowHeight + NullableButtonGap );
				}
				else
					row.Add( label );

				row.Add( ControlWidget, 1 );
				Layout.Add( row );
			}
		}
		else
			Layout.Add( ControlWidget );

		var validateAttributes = property.GetAttributes<ValidateAttribute>().ToList();
		if ( validateAttributes.Any() )
			validateResultContainer = Layout.AddColumn();
		else if ( validateResultContainer.IsValid() )
		{
			validateResultContainer.Destroy();
			validateResultContainer = null;
		}

		UpdateValidation();
	}

	internal float LabelWantedWidth => showsLabel
		? label.WantedSize().x + (property.IsNullable ? Theme.RowHeight + NullableButtonGap : 0 )
		: 0;

	internal void FitLabelColumn( float width )
	{
		if ( !showsLabel || !label.IsValid() ) return;

		labelColumn = width;
		var labelWidth = width - (property.IsNullable ? Theme.RowHeight + NullableButtonGap : 0 );
		label.MinimumWidth = MathF.Max( label.WantedSize().x, labelWidth );
	}

	private void UpdateValidation()
	{
		var validateAttributes = property.GetAttributes<ValidateAttribute>().ToList();
		if ( validateAttributes.Any() && validateResultContainer.IsValid() )
		{
			var owner = property.Parent.Targets.FirstOrDefault();
			var typeDesc = TypeLibrary.GetType( owner.GetType() );
			var propertyValue = property.GetValue<object>();

			validateResultContainer.Clear( true );

			//Process each validation attribute
			foreach ( var validateAttribute in validateAttributes )
			{
				var validationResult = validateAttribute.Validate( owner, typeDesc, propertyValue );

				//Only display non-success results
				if ( !validationResult.Success )
				{
					var tint = validationResult.Status switch
					{
						LogLevel.Warn => EditorTint.Yellow,
						LogLevel.Error => EditorTint.Red,
						LogLevel.Info => EditorTint.Blue,
						LogLevel.Trace => EditorTint.Blue,
						_ => EditorTint.Blue
					};

					var icon = validationResult.Status switch
					{
						LogLevel.Warn => "warning",
						LogLevel.Error => "error",
						LogLevel.Info => "info",
						LogLevel.Trace => "info",
						_ => "info"
					};

					validateResultContainer.Add( new InfoBoxWidget( validationResult.Message, tint, icon ) );
				}
			}
		}
	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		e.Accepted = true;

		var menu = new ContextMenu( this );

		foreach ( var control in ControlsAt( e.ScreenPosition ) )
		{
			control.OnLabelContextMenu( menu );
		}

		menu.AddOption( $"Copy {property.DisplayName}", "content_copy", () =>
		{
			string str = ControlWidget.ToClipboardString();
			EditorUtility.Clipboard.Copy( str );
		} );

		menu.AddOption( $"Paste as {property.DisplayName}", "content_paste", () =>
		{
			string str = EditorUtility.Clipboard.Paste();
			ControlWidget.FromClipboardString( str );
		} );


		menu.AddOption( "Reset to Default", "restart_alt", () =>
		{
			property.Parent.NoteStartEdit( property );
			property.SetValue( property.GetDefault() );
			property.Parent.NoteFinishEdit( property );
		} );

		bool isPrefab = property.GetContainingGameObject()?.IsPrefabInstance ?? false;
		if ( isPrefab && (IsEditingComponent || IsEditingGameObject) )
		{
			menu.AddSeparator();

			var isPropertyModified = EditorUtility.Prefabs.IsPropertyOverridden( property );

			object editedObject = EditedComponents.FirstOrDefault();
			editedObject ??= EditedGameObjects.FirstOrDefault();
			var prefabName = EditorUtility.Prefabs.GetOuterMostPrefabName( editedObject ) ?? "";

			var revertActionName = "Revert Change";
			menu.AddOption( revertActionName, "history", () =>
			{
				var session = SceneEditorSession.Resolve( EditedGameObjects.FirstOrDefault() );
				using var scene = session.Scene.Push();
				using ( session.UndoScope( revertActionName ).WithComponentChanges( EditedComponents ).WithGameObjectChanges( EditedGameObjects, GameObjectUndoFlags.Properties ).Push() )
				{
					EditorUtility.Prefabs.RevertPropertyChange( property );
				}
			} ).Enabled = isPropertyModified;

			menu.AddOption( "Apply to Prefab", "save", () =>
			{
				EditorUtility.Prefabs.ApplyPropertyChange( property );
			} ).Enabled = isPropertyModified;
		}

		if ( CodeEditor.CanOpenFile( property.SourceFile ) )
		{
			menu.AddSeparator();

			var filename = System.IO.Path.GetFileName( property.SourceFile );
			menu.AddOption( $"Jump to code", "code", action: () => CodeEditor.OpenFile( property.SourceFile, property.SourceLine ) );
		}

		AddComponentOptions( menu );

		menu.OpenAt( e.ScreenPosition, false );
	}

	//Controls under the click, innermost first, ending with the row's own control
	private List<ControlWidget> ControlsAt( Vector2 screenPosition )
	{
		var found = new List<ControlWidget>();

		Walk( this );

		if ( ControlWidget.IsValid() && !found.Contains( ControlWidget ) )
			found.Add( ControlWidget );

		return found;

		//Children before the widget itself, so the innermost comes out first.
		void Walk( Widget widget )
		{
			if ( !widget.IsValid() ) return;
			if ( !widget.ScreenRect.IsInside( screenPosition ) ) return;

			foreach ( var child in widget.Children )
			{
				Walk( child );
			}

			if ( widget is ControlWidget control && !found.Contains( control ) )
				found.Add( control );
		}
	}

	bool IsEditingComponent => property.Parent?.Targets?.OfType<Component>().FirstOrDefault( x => x.IsValid() ) is not null;
	bool IsEditingGameObject => property.Parent?.Targets?.OfType<GameObject>().FirstOrDefault( x => x.IsValid() ) is not null || property.Parent?.Targets?.OfType<GameTransform>().FirstOrDefault() is not null;

	IEnumerable<Component> EditedComponents => property.Parent?.Targets?.OfType<Component>().Where( x => x.IsValid() ) ?? Enumerable.Empty<Component>();
	IEnumerable<GameObject> EditedGameObjects => (property.Parent?.Targets?.OfType<GameObject>().Where( x => x.IsValid() ) ?? Enumerable.Empty<GameObject>()).Concat( property.Parent?.Targets?.OfType<GameTransform>().Where( x => x.GameObject.IsValid() ).Select( x => x.GameObject ) ?? Enumerable.Empty<GameObject>() );

	void AddComponentOptions( Menu menu )
	{
		//Are we editing the property of a component?
		if ( !IsEditingComponent )
			return;

		var component = EditedComponents.FirstOrDefault();

		//Only show if we're editing in a game session
		var session = SceneEditorSession.Resolve( component?.GameObject?.Scene );
		if ( session is null )
			return;

		//try to find the version of this component in the editor session
		var targetComponent = session.Scene.Directory.FindComponentByGuid( component.Id );
		if ( !targetComponent.IsValid() ) return;

		//get a serialized version of this property from that session
		var so = targetComponent.GetSerialized();
		var prop = so.GetProperty( property.Name );

		//add option to apply this value to that scene
		menu.AddSeparator();
		var setter = menu.AddOption( "Apply to Scene", "save", () =>
		{
			using var scope = session.Scene.Push();

			using ( session.UndoScope( "Apply to Scene" ).WithComponentChanges( targetComponent ).Push() )
			{
				prop.SetValue<object>( property.GetValue<object>() );
			}
		} );
		setter.Enabled = Json.Serialize( prop.GetValue<object>() ) != Json.Serialize( property.GetValue<object>() );
	}
}

internal class InfoBoxWidget : Widget
{
	private string message;
	private EditorTint tint;
	private string icon;
	Panel background;

	public InfoBoxWidget( string message, EditorTint tint, string icon )
	{
		this.message = message;
		this.tint = tint;
		this.icon = icon;

		SetSizeMode( SizeMode.Flexible, SizeMode.Expand );

		Layout = Layout.Row();
		Layout.Margin = new Margin( 12, 12, 12, 20 );

		Layout.AddSpacingCell( 32 );
		Layout.Add( new Label( message ) { WordWrap = true } );
	}

	public override void SyncSurfaceContent()
	{
		if ( ContentPanel is null ) return;

		background ??= new Panel();
		background.AddClass( "infobox-bg" );
		background.AddClass( TintClass( tint ) );
		ContentPanel.AddChild( background );
	}

	static string TintClass( EditorTint tint ) => tint switch
	{
		EditorTint.Yellow => "tint-yellow",
		EditorTint.Red => "tint-red",
		_ => "tint-blue",
	};

	protected override Vector2 SizeHint()
	{
		return 70;
	}

	protected override void OnPaint()
	{
		if ( !Paint.Snapshot ) return;

		Paint.SetPen( Theme.GetTint( tint ).Desaturate( 0.5f ).Lighten( 5 ) );
		Paint.DrawIcon( new Rect( 10, 18 ), icon, 18, TextFlag.Center );
	}
}

//A button to the left of the property, allows toggling NULL state on nullabe values.
file class PropertyButton : Widget
{
	SerializedProperty property;
	ControlWidget controlWidget;
	SerializedObject serializedObject;
	Panel stateDot;

	public PropertyButton( SerializedProperty property, ControlWidget controlWidget )
	{
		this.property = property;
		this.controlWidget = controlWidget;
		serializedObject = property.Parent;
		serializedObject.OnPropertyChanged += OnParentPropertyChanged;

		FixedHeight = Theme.RowHeight;
		FixedWidth = Theme.RowHeight;
		HorizontalSizeMode = SizeMode.Default;
		Cursor = CursorShape.Finger;
		ToolTip = "Has Value";
	}

	public override void SyncSurfaceContent()
	{
		if ( ContentPanel is null ) return;

		stateDot ??= new Panel();
		stateDot.AddClass( "state-dot" );
		ContentPanel.AddChild( stateDot );

		var isMultiple = property.IsMultipleDifferentValues;
		stateDot.SetClass( "null", property.IsNull && !isMultiple );
		stateDot.SetClass( "has-value", !property.IsNull && !isMultiple );
		stateDot.SetClass( "multiple", isMultiple );
	}

	void OnParentPropertyChanged( SerializedProperty changed )
	{
		if ( changed is null || changed == property )
			Update();
	}

	public override void OnDestroyed()
	{
		if ( serializedObject is not null )
			serializedObject.OnPropertyChanged -= OnParentPropertyChanged;

		base.OnDestroyed();
	}

	protected override void OnPaint()
	{
		if ( !Paint.Snapshot ) return;

		var icon = "eject";
		var size = 15;
		Color color = Theme.TextControl.WithAlpha( 0.3f );
		Paint.TextAntialiasing = true;

		var isMultiple = property.IsMultipleDifferentValues;
		if ( property.IsNull && !isMultiple )
		{
			icon = "radio_button_unchecked";
			size = 10;
		}
		else
		{
			icon = "circle";
			size = 10;
			color = isMultiple ? Theme.MultipleValues : Theme.Primary;
			color = color.WithAlpha( 0.3f );
		}

		Paint.Pen = color;
		Paint.DrawIcon( LocalRect, icon, size );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		property.SetNullState( !property.IsNull );

		controlWidget.Enabled = !property.IsNull;
		Update();
		SignalValuesChanged();
	}
}

