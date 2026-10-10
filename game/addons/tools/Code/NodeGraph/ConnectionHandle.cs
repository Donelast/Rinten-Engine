using Rinten.UI;

namespace Editor.NodeEditor;

/// <summary>One grip of a grid-drawn wire: push it along its axis and the wire bends there instead of where it would have.</summary>
public sealed class ConnectionHandle : GraphItem
{
	private const float HandleSize = 12.0f;

	public Connection Connection { get; }

	private ConnectionHandleConfig _config;

	public ConnectionHandleConfig Config
	{
		get => _config;
		set
		{
			_config = value;

			SetClass( "vertical", value.Direction == DragDirection.Vertical );
			SetClass( "horizontal", value.Direction == DragDirection.Horizontal );

			MoveToConfig();
		}
	}

	public ConnectionHandle( GraphView graph, Connection connection ) : base( graph )
	{
		Connection = connection;

		AddClass( "connection-handle" );

		Movable = true;
		Size = new Vector2( HandleSize, HandleSize );
		ZIndex = 2.0f;
	}

	/// <summary>Whether the grip is where the wire's style would have put it anyway.</summary>
	public bool IsAtDefault => Config.GetValue( Connection ).AlmostEqual( Config.Default );

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left ) return;

		Graph.Begin( new ConnectionHandleInteraction( Graph, this ), e );

		e.StopPropagation();
	}

	//Where the grip sits, which is wherever the wire's style says it does.
	private void MoveToConfig()
	{
		Position = Config.GetScenePosition( Connection ) - (Size * 0.5f);

		SetClass( "moved", !IsAtDefault );
	}

	//Nothing happens while the nodes either end are selected: they are being dragged, the wire is following them, and bending it as well would fight the drag.
	/// <summary>Pushes the grip to where the pointer is, along the one axis it moves in.</summary>
	public void DragTo( Vector2 scenePosition )
	{
		if ( AreNodesSelected )
		{
			MoveToConfig();
			return;
		}

		var wanted = scenePosition;
		var current = Config.GetScenePosition( Connection );
		var diff = (wanted - current).SnapToGrid( Graph.GridSize );

		var offset = Config.Direction == DragDirection.Horizontal ? diff.x : diff.y;

		if ( offset.AlmostEqual( 0.0f ) )
		{
			MoveToConfig();
			return;
		}

		var value = Config.GetValue( Connection ) + offset;

		Connection.Input?.Inner.SetHandleOffset( Config.Name,
			value.AlmostEqual( Config.Default ) ? null : value );

		Connection.Layout();
	}

	private bool AreNodesSelected => Config.RelativePlug == ConnectionPlug.Input
		? Connection.Input is { Node.Selected: true }
		: Connection.Output is { Node.Selected: true };
}
