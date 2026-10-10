using Editor.Inspectors;
using Facepunch.ActionGraphs;
using Rinten;
using Rinten.ActionGraphs;
using Rinten.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Editor.ActionGraphs;

/// <summary>One action graph, as a line that opens it.</summary>
[StyleSheet( "ActionControl.cs.scss" )]
[CustomEditor( typeof( ActionGraph ) )]
[CustomEditor( typeof( IActionGraphDelegate ) )]
public sealed class ActionControl : BaseControl
{
	private readonly Panel _field;
	private readonly IconPanel _icon;
	private readonly Rinten.UI.Label _title;
	private readonly Rinten.UI.Label _description;

	private int _displayHash;

	public ActionControl()
	{
		ShowBackground = false;

		_field = AddChild<Panel>( "field" );
		_field.AddEventListener( "onclick", OnFieldClick );

		_icon = _field.AddChild<IconPanel>( "icon" );

		var text = _field.AddChild<Panel>( "text" );

		_title = text.AddChild<Rinten.UI.Label>( "title" );
		_title.Tokenize = false;

		_description = text.AddChild<Rinten.UI.Label>( "description" );
		_description.Tokenize = false;
	}

	public override void Rebuild()
	{
		_displayHash = 0;

		SyncDisplay();
	}

	public override void Tick()
	{
		base.Tick();

		SyncDisplay();
	}

	private ActionGraph Graph
	{
		get
		{
			if ( Property is null ) return null;

			try
			{
				if ( Property.PropertyType == typeof( ActionGraph ) )
				{
					return Property.GetValue<ActionGraph>();
				}

				if ( Property.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) ) )
				{
					return Property.GetValue<IActionGraphDelegate>()?.Graph;
				}

				return Property.GetValue<Delegate>()?.GetActionGraphInstance()?.Graph;
			}
			catch ( InvalidCastException )
			{
				return null;
			}
		}
	}

	private Type DelegateType
	{
		get
		{
			var property = Property;

			if ( property.Parent is SerializedCollection { TargetObject: DelegateControl.DelegateWrapper wrapper } )
			{
				property = wrapper.ParentProperty;
			}

			return property.PropertyType.IsAssignableTo( typeof( Delegate ) )
				? property.PropertyType
				: null;
		}
	}

	//What the line says about the graph, and how it says it.
	private void SyncDisplay()
	{
		if ( Property is null ) return;

		var graph = Graph;
		var errorCount = graph?.GetMessages().Count( x => x.IsError ) ?? 0;
		var hash = HashCode.Combine( graph, graph?.Title, graph?.Description, errorCount );

		if ( hash == _displayHash ) return;

		_displayHash = hash;

		SetClass( "empty", graph is null );
		SetClass( "error", errorCount > 0 );

		_icon.Text = graph?.Icon ?? "account_tree";

		if ( graph is null )
		{
			_title.Text = "Empty Action";
			_description.Text = null;
			_description.SetClass( "hidden", true );

			return;
		}

		var title = graph.Title;
		var description = errorCount > 0
			? $"{errorCount} Error{(errorCount == 1 ? "" : "s")}"
			: graph.Description;

		_title.Text = string.IsNullOrWhiteSpace( title ) ? "Action" : title;
		_description.Text = description;
		_description.SetClass( "hidden", string.IsNullOrWhiteSpace( description ) );
	}

	//Opening the graph is a press on the line, wherever on it.
	private void OnFieldClick( PanelEvent e )
	{
		if ( e is not MousePanelEvent mouse || mouse.MouseButton != MouseButtons.Left ) return;
		if ( Property is null || IsReadOnly ) return;

		OpenEditor();
		e.StopPropagation();
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		if ( Property is null ) return;

		OpenContextMenu();
		e.StopPropagation();
	}

	//Only a plain delegate can be emptied here - a graph or a graph delegate is the value rather than something pointing at one.
	private void OpenContextMenu()
	{
		if ( Property.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) ) ) return;
		if ( Property.PropertyType == typeof( ActionGraph ) ) return;

		var menu = new Menu();

		menu.AddOption( "Clear", "clear", Clear ).Enabled = !IsReadOnly;
		menu.OpenAtCursor( this );
	}

	private void Clear()
	{
		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue<object>( null );
		Property.Parent?.NoteFinishEdit( Property );
	}

	private void OpenEditor()
	{
		//A delegate whose signature the node library can't describe has no graph to make, and asking for one throws from inside it.
		if ( DelegateType is { } delegateType && !DelegateSupport.CanEdit( delegateType ) )
		{
			Log.Warning( $"Action graphs can't describe {delegateType}, so there is nothing to open." );
			return;
		}

		ActionGraphView.Open( PrepareGraphForEditing() );
	}

	private void UpdateProperty( ActionGraph graph )
	{
		if ( !Property.IsValid ) return;

		if ( Property.PropertyType == typeof( ActionGraph ) )
		{
			Property.SetValue( graph );
		}
		else if ( Property.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) ) )
		{
			var inst = Property.GetValue<IActionGraphDelegate>();

			Property.SetValue( graph.CreateDelegate( DelegateType, inst?.Defaults ) );
		}
		else
		{
			var inst = Property.GetValue<Delegate>()?.GetActionGraphInstance();

			Property.SetValue( graph.CreateDelegate( DelegateType, inst?.Defaults ).Delegate );
		}

		Property.Parent?.NoteChanged( Property );
	}

	private ActionGraph PrepareGraphForEditing()
	{
		var oldGraph = Graph;
		var newGraph = PrepareGraphForEditing( oldGraph, Property, DelegateType );

		if ( oldGraph != newGraph )
		{
			UpdateProperty( newGraph );
		}

		return newGraph;
	}

	internal static ActionGraph PrepareGraphForEditing( ActionGraph graph, SerializedProperty property, Type delegateType )
	{
		graph ??= delegateType is not null
			? ActionGraph.CreateDelegate( EditorNodeLibrary, delegateType ).Graph
			: ActionGraph.CreateEmpty( EditorNodeLibrary );

		if ( string.IsNullOrEmpty( graph.Title ) )
		{
			graph.Title = property.DisplayName;
		}

		if ( FindHostObject( property ) is { } obj )
		{
			var eventArgs = new FindGraphTargetEvent( graph ) { TargetType = obj.GetType(), TargetValue = obj };

			EditorEvent.Run( FindGraphTargetEvent.EventName, eventArgs );

			if ( eventArgs.TargetType is { } type && eventArgs.TargetValue is null )
			{
				EditorActionGraph.SetTargetType( graph, type );
			}
			else if ( eventArgs.TargetValue is { } value )
			{
				EditorActionGraph.SetTarget( graph, value );
			}

			graph.SourceLocation ??= obj switch
			{
				GameResource resource => new GameResourceSourceLocation( resource ),
				GameObject go => go.Scene.GetSourceLocation(),
				_ => null
			};
		}

		graph.AddRequiredNodes();

		return graph;
	}

	private static object FindHostObject( SerializedProperty property )
	{
		var parent = property.Parent;

		while ( parent != null )
		{
			if ( parent.IsMultipleTargets )
			{
				return null;
			}

			switch ( parent.Targets.FirstOrDefault() )
			{
				case GameObject go:
					return go;

				case Component component:
					return component.GameObject;

				case GameResource resource:
					return resource;
			}

			parent = parent.ParentProperty?.Parent;
		}

		return null;
	}

	public override bool AcceptEditorDrops => !IsReadOnly;

	public override DropAction OnEditorDragHover( Widget.DragEvent ev )
	{
		var accept = TryGetDraggedAction( ev, out _ );

		SetClass( "drop-target", accept );

		return accept ? DropAction.Link : DropAction.Ignore;
	}

	public override void OnEditorDragLeave() => SetClass( "drop-target", false );

	public override void OnEditorDragDrop( Widget.DragEvent ev )
	{
		SetClass( "drop-target", false );

		if ( !TryGetDraggedAction( ev, out var resource ) )
		{
			return;
		}

		var graph = PrepareGraphForEditing();
		var graphNodeType = new GraphNodeType( resource );
		var node = graphNodeType.CreateNode( graph );

		graph.InputNode?.UpdateParameters();

		if ( graph.InputNode?.Outputs.Signal is { } signal && node.Inputs.TryGetValue( ParameterNames.Signal, out var inSignal ) )
		{
			inSignal.SetLink( signal );
		}

		if ( graph.TargetOutput is { } outTarget && node.Inputs.Values.FirstOrDefault( x => x.IsTarget ) is { } inTarget )
		{
			inTarget.SetLink( outTarget );
		}

		if ( graph.UserData["LastInsertedId"]?.Deserialize<int>() is { } lastInsertedId && graph.Nodes.TryGetValue( lastInsertedId, out var lastInserted ) )
		{
			var lastInsertedPos = lastInserted.UserData["Position"]?.Deserialize<Vector2>() ?? Vector2.Zero;
			node.UserData["Position"] = Json.ToNode( new Vector2( lastInsertedPos.x, lastInsertedPos.y + 64f ) );
		}
		else
		{
			var maxPosX = graph.Nodes.Values.Max( x => x.UserData["Position"]?.Deserialize<Vector2>().x ?? 0f );
			node.UserData["Position"] = Json.ToNode( new Vector2( maxPosX + 256f, 0f ) );
		}

		graph.UserData["LastInsertedId"] = node.Id;

		ActionGraphView.Rebuild( graph );
	}

	private static bool TryGetDraggedAction( Widget.DragEvent ev, out ActionGraphResource resource )
	{
		resource = null;

		if ( ev.Data.Assets.FirstOrDefault() is not { IsInstalled: true } asset )
		{
			return false;
		}

		if ( !string.Equals( Path.GetExtension( asset.AssetPath ), ".action", StringComparison.OrdinalIgnoreCase ) )
		{
			return false;
		}

		return ResourceLibrary.TryGet( asset.AssetPath, out resource );
	}
}
