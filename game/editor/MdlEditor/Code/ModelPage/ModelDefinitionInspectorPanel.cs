using Rinten.UI;
using System.IO;
using System.Linq;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Inspector page for a model: its outliner and the picked node's fields, as panels
[Inspector( typeof( ModelDefinition ) )]
[StyleSheet( "ModelDefinitionInspectorPanel.cs.scss" )]
internal sealed class ModelDefinitionInspectorPanel : InspectorPanel, IModelDocHost, Inspectors.IInspectorHead
{
	readonly Asset _asset;
	readonly ModelDefinition _definition;
	readonly ModelOutliner _list;
	readonly ModelNodeInspector _node;
	readonly Inspectors.InspectorCard _nodeCard;
	readonly Panel _head;
	readonly ToolbarButton _open;

	//Whether the file may be written at all - a cloud package cannot.
	readonly bool _readOnly;

	//An edit is waiting to be written, at the end of a tick rather than mid-edit.
	bool _saveWanted;

	Model _model;

	public ModelDefinitionInspectorPanel( SerializedObject so ) : base( so )
	{
		AddClass( "model-definition-inspector" );

		_definition = so.Targets.OfType<ModelDefinition>().FirstOrDefault();
		_asset = _definition is null ? null : AssetSystem.FindByPath( _definition.ResourcePath );

		if ( _definition is null ) return;

		_readOnly = _asset is null || AssetSystem.IsCloudInstalled( _asset.Package ) || !_asset.HasSourceFile;

		_definition.EnsureEditorDefaults();

		//Like a GameObject: a head card with the parts tree, then one card for the picked node
		var scroll = AddChild<Panel>( "scroll" );

		_head = scroll.AddChild<Panel>( "inspector-head" );
		var top = _head.AddChild<Panel>( "top-row" );
		var words = top.AddChild<Panel>( "words" );

		var title = words.AddChild<UILabel>( "title" );
		title.Text = _asset?.Name ?? "Model";
		title.Tokenize = false;

		var path = words.AddChild<UILabel>( "path" );
		path.Text = _asset?.Path ?? "";
		path.Tokenize = false;

		_open = top.AddChild<ToolbarButton>();
		_open.Icon = "open_in_new";
		_open.ToolTip = "Open in MdlEditor";
		_open.OnPressed = () => _asset?.OpenInEditor();

		_list = new ModelOutliner( this, compact: true );
		_list.ItemSelected = ShowNode;
		_head.AddChild( Standing( _list.Content ) );

		_nodeCard = scroll.AddChild( new Inspectors.InspectorCard( "", "tune" ) );
		_nodeCard.AddClass( "node hidden" );

		_node = new ModelNodeInspector( this, compact: true );
		_nodeCard.Body.AddChild( Standing( _node.Content ) );
		_nodeCard.Refold();

		_list.SetModel( _asset, _definition );
	}

	/// <summary>Under an asset's header, which names the file already: the button goes beside the name there, and the tree under it.</summary>
	public void FillHead( Inspectors.AssetHeaderPanel head )
	{
		if ( _open is null ) return;

		head.Tools.AddChild( _open );
		head.Fields.AddChild( _list.Content );
		_head.AddClass( "hidden" );
	}

	//A widget's panel, made to stand in this column instead. The widget marked it as its content, which is a panel laid over the whole
	//of the widget from its top left - kept, that put the outliner and the node's fields on top of one another at the head of the page.
	static Panel Standing( Panel panel )
	{
		panel.RemoveClass( "panel-widget-content" );
		panel.RemoveClass( "panel-widget-content-auto" );
		return panel;
	}

	void ShowNode( ModelDocItem item )
	{
		var shown = item is not null && !item.IsFolder;

		_nodeCard.SetClass( "hidden", !shown );

		if ( !shown ) return;

		_nodeCard.Title = item.Title;
		_nodeCard.Icon = string.IsNullOrEmpty( item.Icon ) ? "tune" : item.Icon;
		_node.Show( _definition, item );
	}

	public override void Tick()
	{
		base.Tick();

		if ( _readOnly || _definition is null ) return;

		//Edits land on the definition object straight - a mesh added in the outliner, a field set on a node
		//- so its own flag is asked as well as ours. Writing it clears the flag, so this is once per edit.
		if ( !_saveWanted && _definition.HasUnsavedChanges ) _saveWanted = true;

		if ( !_saveWanted ) return;

		_saveWanted = false;

		Save();
	}

	//Writes the model out - which compiles it - and keeps the file's before and after.
	void Save()
	{
		var file = _asset?.GetSourceFile( true );
		var before = Read( file );

		_asset?.SaveToDisk( _definition );
		_model = null;

		ResourceHistory.Record( file, "Edit", before, Read( file ) );
	}

	static string Read( string file )
	{
		if ( string.IsNullOrEmpty( file ) ) return null;

		try
		{
			return File.Exists( file ) ? File.ReadAllText( file ) : null;
		}
		catch
		{
			return null;
		}
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		//Nothing ever showed them; they only held the outliner's logic.
		_list?.Destroy();
		_node?.Destroy();
	}

	Asset IModelDocHost.Asset => _asset;
	ModelDefinition IModelDocHost.Definition => _definition;
	ModelOutliner IModelDocHost.Outliner => _list;

	Model IModelDocHost.CompiledModel => _model ??= _asset?.LoadResource<Model>();

	bool IModelDocHost.CanSave => false;

	void IModelDocHost.SaveModel() { }

	void IModelDocHost.NotifyDefinitionChanged( bool rebuildInspector )
	{
		//Written at the end of this tick - see Tick - and the list and the node's fields drawn again from what is now in the definition.
		_saveWanted = true;
		_definition?.StateHasChanged();

		_list?.RefreshTree();
		if ( rebuildInspector )
			_node?.Refresh();
	}
}
