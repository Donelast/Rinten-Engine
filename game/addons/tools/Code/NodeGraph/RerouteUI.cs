using Rinten.UI;
using UILabel = Rinten.UI.Label;

namespace Editor.NodeEditor;

/// <summary>A corner for a wire to go round: a dot with one socket either side of it and room above for a word about what is passing through.</summary>
public class RerouteUI : NodeUI
{
	private const float DotSize = 16.0f;

	private UILabel _comment;

	//Sized by hand: it is a dot, and a dot is the size a dot is.
	protected override bool WritesSize => true;

	public RerouteUI( GraphView graph, INode node ) : base( graph, node )
	{
		AddClass( "reroute" );

		ZIndex = 0.0f;
		Size = DotSize;
	}

	protected override void BuildContent()
	{
		_comment = AddChild<UILabel>( "reroute-comment" );
		_comment.Tokenize = false;

		//The sockets sit on top of the dot rather than either side of it: the whole node is
		//the handle, and which end a wire takes is decided by which of them is in front.
		InputColumn = AddChild<Panel>( "plugs in" );
		OutputColumn = AddChild<Panel>( "plugs out" );
	}

	protected override void Layout()
	{
		Size = DotSize;

		//A reroute that is fed by something prefers to give its output when the pointer lands on it, because
		//that is the end there is anything to do with. One with nothing in it wants its input instead.
		var preferOutput = Inputs.Any( x => x.IsConnected );

		foreach ( var plug in AllPlugs )
		{
			plug.Visible = false;
		}

		SetClass( "prefer-output", preferOutput );
	}

	protected override void OnUpdate()
	{
		var colour = Outputs.FirstOrDefault()?.HandleConfig.Color ?? Graph.DefaultHandleConfig.Color;

		if ( !HasHovered ) colour = colour.Desaturate( 0.2f ).Darken( 0.3f );

		Style.BackgroundColor = Selected ? SelectionOutline : colour;

		var text = (Node as IRerouteNode)?.Comment;

		_comment.SetClass( "hidden", string.IsNullOrWhiteSpace( text ) );
		_comment.Text = text ?? string.Empty;

		foreach ( var plug in AllPlugs )
		{
			plug.Update();
		}
	}
}
