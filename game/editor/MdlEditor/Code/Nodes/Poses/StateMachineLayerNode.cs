using System.Text.Json.Serialization;

namespace Editor.MdlEditor.Nodes;

/// <summary>A state-machine layer for Layer Blend; the active state supplies weights and mask</summary>
[Title( "State Machine Layer" ), Group( "Layers" ), Icon( "layers" )]
public class StateMachineLayerNode : Node
{
	private const float DebugHeight = 24;

	[Input( typeof( PoseLink ) ), Title( "State Machine" )]
	public NodeInput StateMachine { get; set; }

	[Property]
	public AnimGraphBuilder.LayerBlendMode BlendMode { get; set; } = AnimGraphBuilder.LayerBlendMode.Overlay;

	[Property]
	public bool IsSynchronized { get; set; }

	[Property]
	public bool IgnoreEvents { get; set; }

	[Output( typeof( LayerLink ) )]
	[JsonIgnore]
	public object Layer => default;

	public override Color PrimaryColor => Color.Parse( "#ff6347" ).Value;

	[JsonIgnore]
	internal bool HasLiveLayerWeight { get; private set; }

	[JsonIgnore]
	internal float LiveLayerWeight { get; private set; }

	public StateMachineLayerNode()
	{
		ExpandSize = new Vector2( 0, DebugHeight + 8 );
	}

	//The weight this layer is running at, read off the live model.
	private Rinten.UI.Label _weightReadout;

	public override void BuildContent( Rinten.UI.Panel body )
	{
		_weightReadout = body.AddChild<Rinten.UI.Label>( "layer-weight" );
		_weightReadout.Tokenize = false;
	}

	public override void OnFrame()
	{
		base.OnFrame();

		if ( _weightReadout is null ) return;

		_weightReadout.Text = HasLiveLayerWeight ? LiveLayerWeight.ToString( "0.00" ) : "-";
	}

	public bool SetLiveLayerWeight( bool hasWeight, float weight = 0.0f )
	{
		if ( HasLiveLayerWeight == hasWeight && LiveLayerWeight == weight )
			return false;

		HasLiveLayerWeight = hasWeight;
		LiveLayerWeight = weight;
		return true;
	}

	internal override AnimGraphBuilder.Node Compile( Compiler compiler )
	{
		compiler.Warn( "State Machine Layer must be connected to a Layer Blend node", this );
		return compiler.ReferencePose();
	}
}
