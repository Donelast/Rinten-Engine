using Rinten.UI;
using System;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Property sheet for the selected outliner node. The dock is the only widget here; everything in it is panels. A dock slot
//has to be a widget because that is what the dock manager arranges - see PanelDockHost - and nothing below it needs to be.
sealed class ModelNodeInspector : PanelDockHost, IModelDocHost
{
	private readonly IModelDocHost _host;
	private readonly ModelNodeInspectorPanel _panel;

	/// <summary>Dock inspector, or a compact column without a heading.</summary>
	public ModelNodeInspector( IModelDocHost host, bool compact = false ) : base( null )
	{
		_host = host;

		Name = "Inspector";
		WindowTitle = "Inspector";
		SetWindowIcon( "tune" );

		_panel = new ModelNodeInspectorPanel( this, compact );

		SetContent( _panel );
	}

	internal void Show( ModelDefinition definition, ModelDocItem item ) => _panel.Show( definition, item );

	internal void Dismiss() => _panel.Dismiss();

	internal void Refresh() => _panel.Refresh();

	Asset IModelDocHost.Asset => _host.Asset;
	ModelDefinition IModelDocHost.Definition => _host.Definition;
	Model IModelDocHost.CompiledModel => _host.CompiledModel;
	ModelOutliner IModelDocHost.Outliner => _host.Outliner;
	bool IModelDocHost.CanSave => _host.CanSave;
	void IModelDocHost.SaveModel() => _host.SaveModel();
	void IModelDocHost.NotifyDefinitionChanged( bool rebuildInspector ) => _host.NotifyDefinitionChanged( rebuildInspector );
	void IModelDocHost.BeginDefinitionEdit( string name ) => _host.BeginDefinitionEdit( name );
	void IModelDocHost.EndDefinitionEdit( string name ) => _host.EndDefinitionEdit( name );
	Transform? IModelDocHost.BoneNow( string bone ) => _host.BoneNow( bone );
}

//The heading, and the properties of whatever the outliner has selected. Shaped like the main editor's inspector - one scroll,
//a band saying what is selected, then the sheet - because it is the same job and should not look like a different program.
[StyleSheet( "ModelNodeInspector.cs.scss" )]
internal sealed class ModelNodeInspectorPanel : EditorPanel
{
	private readonly IModelDocHost _host;
	private readonly bool _compact;

	private readonly UILabel _heading;
	private readonly Panel _scroll;

	//What a handler puts beside the properties - notes, buttons, painters.
	private readonly Panel _body;

	//One sheet, told what to show. A fresh one per selection left the last one in place and started
	//the next node's properties under it, so they walked down the dock as you clicked around.
	private readonly Inspectors.ControlSheet _sheet;

	private ModelDefinition _definition;
	private ModelDocItem _item;

	public ModelNodeInspectorPanel( IModelDocHost host, bool compact )
	{
		_host = host;
		_compact = compact;

		AddClass( "model-inspector" );
		SetClass( "compact", compact );

		//The main editor's inspector ground - design.scss's .inspector - so the dock is the same grey as the one in the scene editor.
		AddClass( "inspector" );

		_scroll = AddChild<Panel>( "scroll" );

		if ( !compact )
		{
			var header = _scroll.AddChild<Panel>( "heading" );
			_heading = header.AddChild<UILabel>( "title" );
			_heading.Text = "Inspector";
		}

		_sheet = _scroll.AddChild( new Inspectors.ControlSheet { ShowPropertyNameInTooltip = true } );
		_body = _scroll.AddChild<Panel>( "body" );
	}

	internal void Show( ModelDefinition definition, ModelDocItem item )
	{
		_definition = definition;
		_item = item;

		if ( _heading.IsValid() )
			_heading.Text = item is null ? "Inspector" : item.Title;

		RebuildBody();
	}

	internal void Dismiss() => Show( _definition, null );

	internal void Refresh()
	{
		if ( _definition is not null )
			RebuildBody();
	}

	private void RebuildBody()
	{
		_body.DeleteChildren();

		//Clear for every node, or the previous node's rows linger above the new body
		_sheet.ClearBody();

		if ( Hint() is { } hint )
		{
			_body.AddChild( Note( hint ) );
			return;
		}

		var handler = ModelDoc.Of( _item.Kind );

		handler.Bind( new ModelDocContext { Host = _host, IsCollapsed = _ => false }, _item, _sheet, _body );
	}

	//What to say instead of properties, or null where there are some.
	private string Hint()
	{
		if ( _definition is null ) return "Nothing is open.";
		if ( _item is null ) return "Select a node to edit it.";
		if ( ModelDoc.Of( _item.Kind ) is null ) return "Select a mesh, hull, clip or group to edit it.";

		return null;
	}

	private static UILabel Note( string text )
	{
		var label = new UILabel();

		label.AddClass( "note" );
		label.Text = text;

		return label;
	}
}
