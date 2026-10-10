using Rinten.UI;

namespace Editor.NodeEditor;

/// <summary>An input: one wire in, or a value edited in place.</summary>
public class PlugIn : Plug
{
	public Connection Connection { get; private set; }

	public override bool IsConnected => Connection.IsValid();

	public new IPlugIn Inner => (IPlugIn)base.Inner;

	public override Vector2 ConnectionPosition => HandleSceneRect.Center;

	public PlugIn( NodeUI node, IPlug plug ) : base( node, plug )
	{
		AddClass( "plug-in" );
	}

	protected override void Build( PlugHandle handle, Panel label )
	{
		AddChild( handle );
		AddChild( label );
	}

	//Should only be called from NodeEditor.Connection.
	internal void SetConnectionInternal( Connection value )
	{
		Connection = value;

		Node.MarkNodeChanged();

		if ( !Editor.IsValid() ) return;

		Editor.Enabled = !value.IsValid();
		Editor.SetClass( "hidden", !Editor.Enabled );
	}

	//A wire being dragged over it, or a node being dragged onto it - both mean the same thing to whoever is looking: let go and this connects.
	protected override Color OnGetHandleColor( ref HandleConfig config, bool isTarget )
	{
		if ( isTarget && Graph.Preview.IsValid() )
		{
			config = Graph.Preview.Output.HandleConfig;

			if ( Graph.Preview.IsInvalidPreview ) return Theme.Red;
		}
		else if ( Connection.IsValid() )
		{
			config = Connection.Output.HandleConfig;
		}

		return config.Color;
	}
}
