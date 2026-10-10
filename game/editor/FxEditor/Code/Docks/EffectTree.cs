using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.FxEditor;

/// <summary>The effect as a tree: its emitters, the four stages each of them has, and the modules in them.</summary>
public sealed class EffectTree : PanelDockHost
{
	readonly ParticleEditorWindow _window;
	readonly EffectTreePanel _panel;

	//What a click picks, and whether a row is the picked one - the window's selection, or a page's.
	readonly Action<object> _select;
	readonly Func<object, bool> _isSelected;

	//Told when a row's eye switched something, where there is no window to tell.
	readonly Action _edited;

	//Emitters and stages somebody has shut, by the object they are.
	readonly HashSet<object> _shut = new();

	ParticleDefinition _definition;

	//No parent: the dock manager is what puts a dock somewhere, and a parent handed over here is replaced the moment the dock is opened.
	public EffectTree( ParticleEditorWindow window ) : base( null )
	{
		_window = window;
		_select = window.Select;
		_isSelected = window.IsSelected;

		Name = "Effect";
		WindowTitle = "Effect";
		SetWindowIcon( "account_tree" );
		MinimumWidth = 180;

		_panel = new EffectTreePanel( this );

		SetContent( _panel );
	}

	/// <summary>The tree with no window behind it: rows to pick from and nothing that changes the effect's shape.</summary>
	public EffectTree( Action<object> select, Func<object, bool> isSelected, Action edited = null ) : base( null )
	{
		_select = select;
		_isSelected = isSelected;
		_edited = edited;

		_panel = new EffectTreePanel( this );

		SetContent( _panel );
	}

	//Whether there is a window to change the effect's shape through: the menus, adding, deleting, solo.
	internal bool CanEdit => _window is not null;

	internal ParticleEditorWindow Window => _window;
	internal ParticleDefinition Definition => _definition;

	/// <summary>What the tree is built from.</summary>
	public void SetDefinition( ParticleDefinition definition )
	{
		_definition = definition;
		_shut.Clear();

		Refresh();
	}

	/// <summary>Build the rows again, for when the effect has changed shape.</summary>
	public void Refresh() => _panel?.Rebuild();

	/// <summary>Read the selection again without rebuilding anything.</summary>
	public void NoteSelection() => _panel?.ReadSelection();

	internal bool IsShut( object key ) => _shut.Contains( key );

	//The tree opened or shut a row: remembered, so it stays so as the rows are read again.
	internal void NoteShut( object key, bool shut )
	{
		if ( shut ) _shut.Add( key );
		else _shut.Remove( key );
	}

	//A row's eye: an emitter or a module switched on or off - through the window, with its undo, where there is one.
	internal void ToggleEnabled( object target )
	{
		switch ( target )
		{
			case ParticleModule module when _window is not null:
				_window.SetModuleEnabled( module, !module.Enabled );
				return;

			case ParticleDefinition.Emitter emitter when _window is not null:
				_window.PushUndo( emitter.Enabled ? "Disable Emitter" : "Enable Emitter" );
				emitter.Enabled = !emitter.Enabled;
				_window.OnEdited();
				return;

			case ParticleModule module:
				module.Enabled = !module.Enabled;
				break;

			case ParticleDefinition.Emitter emitter:
				emitter.Enabled = !emitter.Enabled;
				break;

			default:
				return;
		}

		_edited?.Invoke();
		Refresh();
	}

	//What a row asks of the list

	internal void Select( object target ) => _select?.Invoke( target );

	internal bool IsSelected( object target ) => _isSelected?.Invoke( target ) == true;

	//One context menu for every row kind: actions on the row and things to add beside it
	internal void OpenRowMenu( ParticleDefinition.Emitter emitter, ParticleModuleStage? stage, ParticleModule module, ParticleShape shape = null, bool shapes = false,
		ParticleAnchor anchor = null, bool anchors = false )
	{
		if ( !CanEdit ) return;

		var menu = new Menu( this );

		if ( anchor is not null )
		{
			menu.AddOption( "Duplicate", "difference", () => _window.DuplicateAnchor( anchor ), "Ctrl+D" );
			menu.AddOption( "Delete", "delete", () => _window.DeleteAnchor( anchor ), "Del" );
			menu.AddSeparator();
		}

		if ( anchor is not null || anchors )
		{
			menu.AddOption( "Add Anchor", "anchor", () => _window.AddAnchor() );
			menu.OpenAtCursor();
			return;
		}

		if ( shape is not null )
		{
			menu.AddOption( "Duplicate", "difference", () => _window.DuplicateShape( shape ), "Ctrl+D" );
			menu.AddOption( "Delete", "delete", () => _window.DeleteShape( shape ), "Del" );
			menu.AddSeparator();
		}

		if ( shape is not null || shapes )
		{
			FillShapes( menu.AddMenu( "Add Shape", "add" ) );
			menu.OpenAtCursor();
			return;
		}

		if ( module is not null )
		{
			var on = module.Enabled;

			menu.AddOption( on ? "Disable" : "Enable", on ? "visibility_off" : "visibility",
				() => _window.SetModuleEnabled( module, !on ) );

			menu.AddOption( "Move Up", "arrow_upward", () => _window.MoveModule( emitter, module, -1 ) );
			menu.AddOption( "Move Down", "arrow_downward", () => _window.MoveModule( emitter, module, 1 ) );

			menu.AddSeparator();
			menu.AddOption( "Copy", "content_copy", () => _window.CopyModule( module ), "Ctrl+C" );
			if ( _window.CanPaste )
				menu.AddOption( "Paste", "content_paste", () => _window.Paste( emitter ), "Ctrl+V" );
			menu.AddOption( "Duplicate", "difference", () => _window.DuplicateModule( emitter, module ), "Ctrl+D" );
			menu.AddOption( "Delete", "delete", () => _window.DeleteModule( emitter, module ), "Del" );
		}
		else if ( emitter is not null && stage is null )
		{
			var on = emitter.Enabled;
			var solo = _window.IsSolo( emitter );

			menu.AddOption( on ? "Disable" : "Enable", on ? "visibility_off" : "visibility", () =>
			{
				_window.PushUndo( on ? "Disable Emitter" : "Enable Emitter" );
				emitter.Enabled = !on;
				_window.OnEdited();
			} );

			menu.AddOption( solo ? "Unsolo" : "Solo", "headphones", () => _window.ToggleSolo( emitter ) );

			menu.AddSeparator();
			menu.AddOption( "Copy", "content_copy", () => _window.CopyEmitter( emitter ), "Ctrl+C" );
			if ( _window.CanPaste )
				menu.AddOption( "Paste", "content_paste", () => _window.Paste( emitter ), "Ctrl+V" );
			menu.AddOption( "Duplicate", "difference", () => _window.DuplicateEmitter( emitter ), "Ctrl+D" );
			menu.AddOption( "Delete", "delete", () => _window.DeleteEmitter( emitter ), "Del" );
		}
		else if ( stage is not null && _window.CanPaste )
		{
			menu.AddOption( "Paste", "content_paste", () => _window.Paste( emitter ), "Ctrl+V" );
		}

		if ( menu.HasOptions )
			menu.AddSeparator();

		FillAdds( menu, emitter, stage ?? module?.Stage );

		if ( !menu.HasOptions && !menu.HasMenus )
			return;

		menu.OpenAtCursor();
	}

	//The Add above the tree, which offers whatever the selection can take.
	internal void OpenAddMenu()
	{
		if ( !CanEdit ) return;

		var menu = new Menu( this );

		var selection = _window.Selection;
		var emitter = Owner( selection );

		FillAdds( menu, emitter, StageOf( selection ) );

		if ( !menu.HasOptions && !menu.HasMenus )
			return;

		menu.OpenAtCursor();
	}

	//Everything that could be added here, whatever "here" turned out to be.
	void FillAdds( Menu menu, ParticleDefinition.Emitter emitter, ParticleModuleStage? stage )
	{
		if ( _definition is null ) return;

		menu.AddOption( "Add Emitter", "add_circle", () => _window.AddEmitter() );

		//A head start: an emitter already shaped like the thing most effects are made of, to be tuned rather than built up from nothing.
		var templates = menu.AddMenu( "Add Emitter From Template", "auto_fix_high" );

		foreach ( var (name, icon) in EmitterTemplates.All )
			templates.AddOption( name, icon, () => _window.AddEmitterFromTemplate( name ) );

		if ( _window.CanPaste )
			menu.AddOption( "Paste", "content_paste", () => _window.Paste(), "Ctrl+V" );

		FillShapes( menu.AddMenu( "Add Shape", "category" ) );
		menu.AddOption( "Add Anchor", "anchor", () => _window.AddAnchor() );

		if ( emitter is null ) return;

		//A stage in hand means one list of modules; an emitter on its own means the four of them, each behind its own name.
		if ( stage is { } only )
		{
			FillModules( menu.AddMenu( $"Add {only} Module", "add" ), emitter, only );
			return;
		}

		var add = menu.AddMenu( "Add Module", "add" );

		foreach ( var each in EffectTreePanel.Stages )
			FillModules( add.AddMenu( each.ToString(), "add" ), emitter, each );
	}

	void FillShapes( Menu menu )
	{
		foreach ( var kind in Enum.GetValues<ParticleShape.ShapeKind>() )
		{
			var chosen = kind;

			menu.AddOption( kind.ToString(), ShapeIcon( kind ), () => _window.AddShape( chosen ) );
		}
	}

	//The glyph a thing in the effect is drawn with: in the tree, and over its properties in the inspector.
	internal static string IconOf( object target )
	{
		switch ( target )
		{
			case ParticleAnchor:
				return "anchor";

			case ParticleShape shape:
				return ShapeIcon( shape.Kind );

			case ParticleModule module:
				var info = DisplayInfo.ForType( module.GetType() );
				return string.IsNullOrEmpty( info.Icon ) ? "extension" : info.Icon;

			case ParticleDefinition.Emitter:
				return "blur_on";

			case ParticleDefinition.Parameter:
				return "tune";

			default:
				return "auto_awesome";
		}
	}

	internal static string ShapeIcon( ParticleShape.ShapeKind kind ) => kind switch
	{
		ParticleShape.ShapeKind.Point => "fiber_manual_record",
		ParticleShape.ShapeKind.Sphere => "circle",
		ParticleShape.ShapeKind.Box => "check_box_outline_blank",
		ParticleShape.ShapeKind.Circle => "radio_button_unchecked",
		ParticleShape.ShapeKind.Cone => "change_history",
		ParticleShape.ShapeKind.Line => "horizontal_rule",
		ParticleShape.ShapeKind.Spline => "gesture",
		ParticleShape.ShapeKind.Grid => "grid_on",
		ParticleShape.ShapeKind.Helix => "cyclone",
		ParticleShape.ShapeKind.Model => "view_in_ar",
		_ => "category"
	};

	void FillModules( Menu menu, ParticleDefinition.Emitter emitter, ParticleModuleStage stage )
	{
		foreach ( var type in ParticleEditorWindow.ModuleTypes( stage ).OrderBy( type => type.Title ) )
		{
			var chosen = type;

			menu.AddOption( type.Title, string.IsNullOrEmpty( type.Icon ) ? "extension" : type.Icon,
				() => _window.AddModule( emitter, chosen ) ).StatusTip = type.Description;
		}
	}

	//Which emitter something in the tree belongs to.
	ParticleDefinition.Emitter Owner( object target )
	{
		if ( target is ParticleDefinition.Emitter emitter )
			return emitter;

		if ( target is not ParticleModule module || _definition is null )
			return null;

		foreach ( var candidate in _definition.Emitters )
		{
			if ( candidate.Modules( module.Stage )?.Contains( module ) == true )
				return candidate;
		}

		return null;
	}

	static ParticleModuleStage? StageOf( object target )
		=> target is ParticleModule module ? module.Stage : null;

	//Delete whatever this row stands for, where it is something that can go.
	internal void Delete( ParticleDefinition.Emitter emitter, ParticleModule module, ParticleShape shape = null, ParticleAnchor anchor = null )
	{
		if ( !CanEdit ) return;

		if ( anchor is not null ) _window.DeleteAnchor( anchor );
		else if ( shape is not null ) _window.DeleteShape( shape );
		else if ( module is not null ) _window.DeleteModule( emitter, module );
		else if ( emitter is not null ) _window.DeleteEmitter( emitter );
	}
}

//The strip of buttons over the tree, and the tree. An EditorPanel rather than a Panel, so it
//hears the editor's own events itself - the dock above it does not have to forward a frame down.
[StyleSheet( "EffectTree.cs.scss" )]
internal sealed class EffectTreePanel : EditorPanel
{
	readonly EffectTree _tree;

	//The rows: the editor's one tree.
	internal EffectRows Rows { get; }

	public EffectTreePanel( EffectTree tree )
	{
		_tree = tree;

		AddClass( "effect-tree tree-dock" );
		SetClass( "compact", !_tree.CanEdit );

		//The bar the inspector's tab opens into, and the tree in a frame under it - design.scss's .tree-dock. Adding is the window's; folding is anybody's.
		var bar = AddChild<Panel>( "dock-bar" );

		if ( _tree.CanEdit )
		{
			var add = bar.AddChild<ToolbarButton>();
			add.Icon = "add";
			add.ToolTip = "Add";
			add.OnPressed = _tree.OpenAddMenu;
		}

		var unfold = bar.AddChild<ToolbarButton>();
		unfold.Icon = "unfold_more";
		unfold.ToolTip = "Expand all";
		unfold.OnPressed = () => Rows?.OpenAll();

		var fold = bar.AddChild<ToolbarButton>();
		fold.Icon = "unfold_less";
		fold.ToolTip = "Collapse all";
		fold.OnPressed = () => Rows?.CloseAll();

		bar.AddChild<Panel>( "spacer" );

		Rows = AddChild( new EffectRows( tree ) );
		Rows.SetClass( "tree-frame", _tree.CanEdit );
	}

	/// <summary>Read the effect again.</summary>
	public void Rebuild() => Rows.Refresh();

	/// <summary>Say again which row is the selected one.</summary>
	public void ReadSelection() => Rows.ShowSelection();

	internal static readonly ParticleModuleStage[] Stages =
	[
		ParticleModuleStage.Spawn,
		ParticleModuleStage.Initialize,
		ParticleModuleStage.Update,
		ParticleModuleStage.Render
	];

	/// <summary>Copy, paste, duplicate and delete on whatever row is selected.</summary>
	public override void OnButtonTyped( ButtonEvent e )
	{
		//Without a window there is nothing to copy into or delete through.
		if ( !e.Pressed || !_tree.CanEdit )
		{
			base.OnButtonTyped( e );
			return;
		}

		var window = _tree.Window;
		var selected = window.Selection;
		var emitter = window.EmitterOf( selected );
		var module = selected as ParticleModule;
		var shape = selected as ParticleShape;
		var anchor = selected as ParticleAnchor;
		var ctrl = Application.KeyboardModifiers.HasFlag( KeyboardModifiers.Ctrl );

		switch ( e.Button )
		{
			case "d" when ctrl && anchor is not null:
				window.DuplicateAnchor( anchor );
				break;

			case "delete" when anchor is not null:
				window.DeleteAnchor( anchor );
				break;

			case "d" when ctrl && shape is not null:
				window.DuplicateShape( shape );
				break;

			case "delete" when shape is not null:
				window.DeleteShape( shape );
				break;

			case "c" when ctrl:
				if ( module is not null ) window.CopyModule( module );
				else if ( emitter is not null ) window.CopyEmitter( emitter );
				break;

			case "v" when ctrl:
				window.Paste( emitter );
				break;

			case "d" when ctrl:
				if ( module is not null ) window.DuplicateModule( emitter, module );
				else if ( emitter is not null ) window.DuplicateEmitter( emitter );
				break;

			case "delete":
				if ( module is not null ) window.DeleteModule( emitter, module );
				else if ( emitter is not null ) window.DeleteEmitter( emitter );
				break;

			default:
				base.OnButtonTyped( e );
				return;
		}

		e.StopPropagation = true;
	}
}

//A heading over things in the tree that is not a thing itself: the shapes, the anchors.
internal sealed class EffectHeading
{
	public string Name { get; }

	EffectHeading( string name ) => Name = name;

	public static readonly EffectHeading Shapes = new( "Shapes" );
	public static readonly EffectHeading Anchors = new( "Anchors" );
}

//One of an emitter's four stages, as a node of its own. A stage is not an object - it is an enum value belonging to an emitter - so
//the tree keeps one of these for each pair, the same one for as long as the emitter lasts, for its rows and its folding to be keyed by.
internal sealed class EffectStage
{
	public ParticleDefinition.Emitter Emitter { get; }
	public ParticleModuleStage Stage { get; }

	public EffectStage( ParticleDefinition.Emitter emitter, ParticleModuleStage stage )
	{
		Emitter = emitter;
		Stage = stage;
	}
}

//Effect rows in the shared tree; EffectTree handles selection and menus
internal sealed class EffectRows : TreePanel<object>
{
	readonly EffectTree _tree;

	readonly Dictionary<(ParticleDefinition.Emitter, ParticleModuleStage), EffectStage> _stages = new();

	int _revision;

	//Set while the tree's folding is put back on the rows, so it is not noted back.
	bool _seeding;

	public EffectRows( EffectTree tree )
	{
		_tree = tree;

		AddClass( "effect-rows" );
		SelectionMode = TreeSelectionMode.Single;
	}

	ParticleDefinition Definition => _tree.Definition;

	protected override int Revision => _revision;

	protected override bool HasRoots() => Definition is not null;

	protected override IEnumerable<object> Roots
	{
		get
		{
			if ( Definition is { } definition )
				yield return definition;
		}
	}

	protected override IEnumerable<object> GetChildren( object node )
	{
		switch ( node )
		{
			case ParticleDefinition definition:
				yield return EffectHeading.Shapes;
				yield return EffectHeading.Anchors;

				foreach ( var emitter in definition.Emitters )
					yield return emitter;

				break;

			case EffectHeading heading when heading == EffectHeading.Shapes:
				foreach ( var shape in Definition.Shapes )
					yield return shape;

				break;

			case EffectHeading heading when heading == EffectHeading.Anchors:
				foreach ( var anchor in Definition.Anchors )
					yield return anchor;

				break;

			case ParticleDefinition.Emitter emitter:
				foreach ( var stage in EffectTreePanel.Stages )
					yield return StageOf( emitter, stage );

				break;

			case EffectStage stage:
				foreach ( var module in stage.Emitter.Modules( stage.Stage ) )
					yield return module;

				break;
		}
	}

	EffectStage StageOf( ParticleDefinition.Emitter emitter, ParticleModuleStage stage )
	{
		if ( !_stages.TryGetValue( (emitter, stage), out var node ) )
			_stages[(emitter, stage)] = node = new EffectStage( emitter, stage );

		return node;
	}

	/// <summary>Read the effect again, and open what nobody shut.</summary>
	public void Refresh()
	{
		_revision++;

		if ( Definition is { } definition )
		{
			_seeding = true;
			OpenUnshut( definition );
			_seeding = false;
		}

		Bump();
		ShowSelection();
	}

	void OpenUnshut( object node )
	{
		if ( _tree.IsShut( node ) )
		{
			Close( node );
			return;
		}

		Open( node );

		foreach ( var child in GetChildren( node ) )
		{
			if ( child is EffectHeading or ParticleDefinition.Emitter or EffectStage )
				OpenUnshut( child );
		}
	}

	protected override void SeedOpenRoots( IEnumerable<object> roots )
	{
		//Opened from what nobody shut - see Refresh.
	}

	protected override void OnExpandedChanged( object node, bool expanded )
	{
		if ( !_seeding ) _tree.NoteShut( node, !expanded );
	}

	/// <summary>Lights the row of what the window has picked, without telling it back.</summary>
	public void ShowSelection()
	{
		var picked = Definition is null ? null : Picked();

		Select( picked is null ? Array.Empty<object>() : [picked], skipEvents: true );
	}

	object Picked()
	{
		var definition = Definition;

		if ( _tree.IsSelected( definition ) ) return definition;

		foreach ( var shape in definition.Shapes )
			if ( _tree.IsSelected( shape ) ) return shape;

		foreach ( var anchor in definition.Anchors )
			if ( _tree.IsSelected( anchor ) ) return anchor;

		foreach ( var emitter in definition.Emitters )
		{
			if ( _tree.IsSelected( emitter ) ) return emitter;

			foreach ( var module in emitter.AllModules() )
				if ( _tree.IsSelected( module ) ) return module;
		}

		return null;
	}

	protected override void OnSelectionChanged( IReadOnlyList<object> selected )
	{
		if ( selected.Count > 0 )
			_tree.Select( selected[^1] );
	}

	//A heading is a fold rather than a thing: pressed, it folds and is not picked.
	protected override bool OnRowPressed( object node, MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left || node is not (EffectHeading or EffectStage) ) return false;

		ToggleOpen( node );
		return true;
	}

	protected override void OnContextMenu( object node )
	{
		var emitter = OwnerOf( node );

		switch ( node )
		{
			case EffectStage stage:
				_tree.OpenRowMenu( stage.Emitter, stage.Stage, null );
				break;

			case EffectHeading heading:
				_tree.OpenRowMenu( null, null, null, shapes: heading == EffectHeading.Shapes, anchors: heading == EffectHeading.Anchors );
				break;

			default:
				_tree.Select( node );
				_tree.OpenRowMenu( emitter, (node as ParticleModule)?.Stage, node as ParticleModule,
					node as ParticleShape, anchor: node as ParticleAnchor );
				break;
		}
	}

	//Which emitter a thing in the tree belongs to.
	ParticleDefinition.Emitter OwnerOf( object node )
	{
		if ( node is ParticleDefinition.Emitter emitter ) return emitter;
		if ( node is EffectStage stage ) return stage.Emitter;
		if ( node is not ParticleModule module || Definition is null ) return null;

		foreach ( var candidate in Definition.Emitters )
		{
			if ( candidate.Modules( module.Stage )?.Contains( module ) == true )
				return candidate;
		}

		return null;
	}

	protected override TreeItemInfo GetInfo( object node )
	{
		var window = _tree.Window;

		switch ( node )
		{
			case ParticleDefinition definition:
				return new TreeItemInfo( "Effect", EffectTree.IconOf( definition ), true );

			case EffectHeading heading:
			{
				var shapes = heading == EffectHeading.Shapes;
				var count = shapes ? Definition.Shapes.Count : Definition.Anchors.Count;
				var hue = shapes ? ShapeHue : AnchorHue;

				return new TreeItemInfo( heading.Name, shapes ? "category" : "anchor", count > 0 )
				{
					IsHeading = true,
					Count = count,
					IconColor = hue,
					TextColor = hue,
				};
			}

			case ParticleShape shape:
				return new TreeItemInfo( shape.Name, EffectTree.IconOf( shape ), false )
				{
					IconColor = Color.Lerp( Theme.TextControl, ShapeHue, 0.35f ),
					Tooltip = "Shape: geometry modules sample by name",
				};

			case ParticleAnchor anchor:
				return new TreeItemInfo( anchor.Name, EffectTree.IconOf( anchor ), false )
				{
					IconColor = Color.Lerp( Theme.TextControl, AnchorHue, 0.35f ),
					Tooltip = "Anchor: a pose the scene binds to an object",
				};

			case ParticleDefinition.Emitter emitter:
			{
				var solo = window?.IsSolo( emitter ) == true;
				var muted = window?.SoloEmitter is not null && !solo;

				//The eye, and in the window the headphones: play this one alone in the preview, so a smoke can be tuned without the fire in front of it.
				var eye = TreeRowAction.Visibility( emitter.Enabled, () => _tree.ToggleEnabled( emitter ) );

				return new TreeItemInfo( emitter.Name, EffectTree.IconOf( emitter ), true )
				{
					Dim = !emitter.Enabled,
					Classes = muted ? "muted" : null,
					Actions = window is null
						? [eye]
						: [new TreeRowAction( "headphones", "headphones", "Solo", solo, () => window.ToggleSolo( emitter ) ), eye],
				};
			}

			case EffectStage stage:
			{
				var count = stage.Emitter.Modules( stage.Stage ).Count;
				var hue = StageHue( stage.Stage );

				return new TreeItemInfo( stage.Stage.ToString(), StageIcon( stage.Stage ), count > 0 )
				{
					IsHeading = true,
					Count = count,
					IconColor = hue,
					TextColor = hue,
				};
			}

			case ParticleModule module:
			{
				var owner = OwnerOf( module );
				var off = !module.Enabled || owner is { Enabled: false };

				//A module is washed with its stage's colour, not wearing it, or four modules would be four different colours of text.
				return new TreeItemInfo( module.Name ?? DisplayInfo.ForType( module.GetType() ).Name, EffectTree.IconOf( module ), false )
				{
					IconColor = Color.Lerp( Theme.TextControl, StageHue( module.Stage ), 0.6f ),
					Dim = off,
					Tooltip = PropertiesPanel.FirstSentence( DisplayInfo.ForType( module.GetType() ).Description ),
					Actions = [TreeRowAction.Visibility( module.Enabled, () => _tree.ToggleEnabled( module ) )],
				};
			}

			default:
				return new TreeItemInfo( node?.ToString() ?? "", "circle", false );
		}
	}

	static readonly Color ShapeHue = new( 0.95f, 0.75f, 0.35f );
	static readonly Color AnchorHue = new( 0.4f, 0.9f, 0.9f );

	//The stage's own hue, so a module is read as belonging to a moment rather than to a flat list.
	internal static Color StageHue( ParticleModuleStage stage ) => stage switch
	{
		ParticleModuleStage.Spawn => new Color( 1.0f, 0.6f, 0.2f ),
		ParticleModuleStage.Initialize => new Color( 0.35f, 0.8f, 0.4f ),
		ParticleModuleStage.Update => new Color( 0.35f, 0.62f, 1.0f ),
		_ => new Color( 0.85f, 0.4f, 0.9f )
	};

	static string StageIcon( ParticleModuleStage stage ) => stage switch
	{
		ParticleModuleStage.Spawn => "star",
		ParticleModuleStage.Initialize => "start",
		ParticleModuleStage.Update => "update",
		_ => "brush"
	};
}
