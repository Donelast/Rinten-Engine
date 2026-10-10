using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.FxEditor;

/// <summary>Inspector page for a .fx: settings, tree, selected item's fields, parameter defaults Tuning only; structural changes need the editor window</summary>
[Inspector( typeof( ParticleDefinition ) )]
[StyleSheet( "FxInspectorPanel.cs.scss" )]
public sealed class FxInspectorPanel : InspectorPanel, Inspectors.IInspectorHead, IEffectEditor
{
	readonly ParticleDefinition _definition;

	//The way into the editor, for the asset's header to hold.
	readonly ToolbarButton _open;

	//The effect's own numbers under no heading, for the asset's header to hold.
	readonly Panel _loose;

	//The page's own first card, emptied into the asset's header where there is one.
	readonly Panel _head;

	//The effect as the editor's tree, a click picking what the card under it shows.
	EffectTree _tree;

	//The tree's panel, for the first card to hold.
	Panel _treeRows;

	//The fields of what is picked in the tree: the editor's own Properties sheet.
	Inspectors.InspectorCard _picked;
	PropertiesPanel _pickedSheet;

	object _selection;

	public FxInspectorPanel( SerializedObject so ) : base( so )
	{
		_definition = so.Targets.OfType<ParticleDefinition>().FirstOrDefault();

		AddClass( "fx-inspector column" );

		//Laid out the way the inspector lays out a GameObject: the way in and the effect's own numbers in the first card - the
		//asset's header, where there is one - then a card for what it is made of and one for the parameters it leaves open.
		var scroll = AddChild<Panel>( "scroll" );

		if ( _definition is null )
		{
			Note( scroll, "No effect" );
			return;
		}

		_head = scroll.AddChild<Panel>( "inspector-head" );

		_open = _head.AddChild<ToolbarButton>();
		_open.Icon = "auto_awesome";
		_open.ToolTip = "Open in FX Editor";
		_open.OnPressed = Open;

		_loose = _head.AddChild<Panel>( "loose column" );

		//The effect's own numbers: how long, whether it loops, the slots.
		so.OnPropertyChanged += _ => Changed();

		Inspectors.InspectorCard.Lay( _loose, scroll, so, "fx" );

		BuildTree( scroll );
		BuildParameters( scroll );
	}

	public void FillHead( Inspectors.AssetHeaderPanel head )
	{
		if ( _open is null ) return;

		head.Tools.AddChild( _open );
		head.Fields.AddChild( _loose );
		head.Fields.AddChild( _treeRows );

		_head.AddClass( "hidden" );
	}

	void Open()
	{
		if ( _definition?.ResourcePath is not { Length: > 0 } path ) return;
		if ( AssetSystem.FindByPath( path ) is not { } asset ) return;

		asset.OpenInEditor();
	}

	//The effect's tree, in the first card, and under it a card for the fields of what is picked in it.
	void BuildTree( Panel into )
	{
		_tree = new EffectTree( Select, x => ReferenceEquals( x, _selection ), Changed );

		//The dock marked its panel as its content, laid over the whole of the widget from its top left; standing in the card it is a column instead.
		_treeRows = _tree.Content;
		_treeRows.RemoveClass( "panel-widget-content" );
		_treeRows.RemoveClass( "panel-widget-content-auto" );
		_head.AddChild( _treeRows );
		_tree.SetDefinition( _definition );

		_picked = into.AddChild( new Inspectors.InspectorCard( "", "tune" ) );
		_picked.AddClass( "picked hidden" );

		_pickedSheet = _picked.Body.AddChild( new PropertiesPanel( this, compact: true ) );
		_picked.Refold();
	}

	//Show the fields of one thing in the effect. The effect itself is the first card's already.
	void Select( object target )
	{
		_selection = target is ParticleDefinition ? null : target;
		_tree?.NoteSelection();

		_picked.SetClass( "hidden", _selection is null );
		_pickedSheet.Target = _selection;

		if ( _selection is null ) return;

		var (title, subtitle) = PropertiesPanel.Describe( _selection );

		_picked.Title = title;
		_picked.Icon = EffectTree.IconOf( _selection );
		_picked.Tooltip = subtitle;
	}

	//The sheet's edits, as the window would take them: there is no undo here and no preview, so an edit is the file written and the tree told.
	void IEffectEditor.OnEdited( string name )
	{
		Changed();
		_tree?.Refresh();
	}

	void IEffectEditor.CommitUndo( string name ) { }

	void IEffectEditor.NoteAnchorRenamed( ParticleAnchor anchor, string was ) => ParticleEditorWindow.CarryAnchorRename( _definition, anchor, was );

	void IEffectEditor.NoteShapeRenamed( ParticleShape shape, string was ) => ParticleEditorWindow.CarryShapeRename( _definition, shape, was );

	//Parameters are not in the tree; their defaults have a card of their own.
	void IEffectEditor.NoteParameterRenamed( ParticleDefinition.Parameter parameter, string was ) { }

	public override void OnDeleted()
	{
		base.OnDeleted();

		//Nothing ever showed the dock itself; it only held the tree's logic.
		_tree?.Destroy();
	}

	//The parameters and their defaults, editable.
	void BuildParameters( Panel into )
	{
		var card = into.AddChild( new Inspectors.InspectorCard( "Parameters", "tune", null, "fx.parameters" ) );
		var section = card.Body;

		var any = _definition.FloatParameters.Count + _definition.VectorParameters.Count + _definition.ColorParameters.Count;

		if ( any == 0 )
		{
			section.AddClass( "rows" );
			Note( section, "No parameters - the effect plays as authored" );
			card.Refold();
			return;
		}

		var values = new ParticleDefaultsObject( _definition );

		values.OnPropertyChanged += _ => Changed();

		var sheet = section.AddChild<Inspectors.ControlSheet>();

		sheet.Target = values;
		sheet.Rebuild();

		card.Refold();
	}

	//An edit here is an edit of the file: the players follow, and the file is written.
	void Changed()
	{
		if ( _definition is null ) return;

		_definition.NoteChanged();

		//The window, where it has the file open, sees the revision move and follows; the file is written here so a closed window is not needed.
		if ( ParticleEditorWindow.Editors.Any( e => ReferenceEquals( e.Definition, _definition ) ) ) return;

		if ( _definition.ResourcePath is { Length: > 0 } path && AssetSystem.FindByPath( path ) is { } asset )
			asset.SaveToDisk( _definition );
	}

	static void Note( Panel parent, string text )
	{
		var note = parent.AddChild<UILabel>( "note" );
		note.Tokenize = false;
		note.Text = text;
	}
}

//The parameters' defaults as one object with a property per parameter, so the
//control sheet draws them as rows - a slider for a ranged float, a vector, a colour.
internal sealed class ParticleDefaultsObject : SerializedObject
{
	public override string TypeName => "Defaults";
	public override string TypeTitle => "Defaults";

	public ParticleDefaultsObject( ParticleDefinition definition )
	{
		PropertyList = new List<SerializedProperty>();

		foreach ( var parameter in definition.FloatParameters )
		{
			var p = parameter;
			var attributes = p.HasRange ? new Attribute[] { new PropertyAttribute(), new RangeAttribute( p.Min, p.Max ) } : null;

			PropertyList.Add( new ParticleParameterProperty( this, p.Name, typeof( float ), () => p.DefaultValue, v => p.DefaultValue = (float)v, attributes ) );
		}

		foreach ( var parameter in definition.VectorParameters )
		{
			var p = parameter;
			PropertyList.Add( new ParticleParameterProperty( this, p.Name, typeof( Vector3 ), () => p.DefaultValue, v => p.DefaultValue = (Vector3)v, null ) );
		}

		foreach ( var parameter in definition.ColorParameters )
		{
			var p = parameter;
			PropertyList.Add( new ParticleParameterProperty( this, p.Name, typeof( ParticleGradient ), () => p.DefaultValue, v => p.DefaultValue = (ParticleGradient)v, null ) );
		}
	}
}
