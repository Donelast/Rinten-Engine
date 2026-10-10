using Rinten.UI;

namespace Editor.NodeEditor;

/// <summary>An output: as many wires out of it as anything wants.</summary>
public class PlugOut : Plug
{
	private readonly HashSet<Connection> _connections = new();

	public IEnumerable<Connection> Connections => _connections;

	public new IPlugOut Inner => (IPlugOut)base.Inner;

	public override bool IsConnected => _connections.Any( x => x.IsValid() );

	public override Vector2 ConnectionPosition => HandleSceneRect.Center;

	public PlugOut( NodeUI node, IPlug plug ) : base( node, plug )
	{
		AddClass( "plug-out" );
	}

	protected override void Build( PlugHandle handle, Panel label )
	{
		AddChild( label );
		AddChild( handle );
	}

	//Should only be called from Connection.
	internal void AddConnectionInternal( Connection value )
	{
		_connections.Add( value );

		Node.MarkNodeChanged();
	}

	//Should only be called from Connection.
	internal void RemoveConnectionInternal( Connection value )
	{
		_connections.Remove( value );

		Node.MarkNodeChanged();
	}

	protected override Color OnGetHandleColor( ref HandleConfig config, bool isTarget )
	{
		if ( isTarget && Graph.Preview is { IsInvalidPreview: true } ) return Theme.Red;

		return config.Color;
	}
}
