using System;
using System.Runtime.InteropServices;

namespace SceneTests;

//A renderer's graph overrides the model's; its parameters show in the inspector and read back
[TestClass]
[DoNotParallelize]
public class SkinnedRendererGraphTests
{
	[StructLayout( LayoutKind.Sequential )]
	private struct Corner
	{
		[VertexLayout.Position] public Vector3 Position;
		[VertexLayout.Normal] public Vector3 Normal;
	}

	//Something to render that names no graph of its own, so what the renderer plays is only ever what was set on it.
	private static Model Triangle()
	{
		var corners = new[]
		{
			new Corner { Position = new Vector3( 0, 0, 0 ), Normal = Vector3.Up },
			new Corner { Position = new Vector3( 1, 0, 0 ), Normal = Vector3.Up },
			new Corner { Position = new Vector3( 1, 1, 0 ), Normal = Vector3.Up },
		};

		var mesh = new Mesh();
		mesh.CreateVertexBuffer( corners.Length, corners.AsSpan() );
		mesh.CreateIndexBuffer( 3, new[] { 0, 1, 2 } );

		return new ModelBuilder().AddMesh( mesh ).Create();
	}

	//One of each kind a game can set, and one it cannot.
	private static AnimGraph GraphWithParameters()
	{
		var builder = AnimGraph.Builder;

		builder.BoolParameter( "moving" );
		builder.FloatParameter( "move_speed", 0.0f, 320.0f );
		builder.IdParameter( "holdtype", "none", "pistol", "rifle" );
		builder.VectorParameter( "aim_direction" );
		builder.TargetParameter( "look_at" );

		return AnimGraph.FromDefinition( builder.Build( builder.ReferencePose() ), "test" );
	}

	private static SkinnedModelRenderer Renderer( out Scene scene )
	{
		ThreadSafe.MarkMainThread();

		scene = new Scene();

		using var scope = scene.Push();

		var thing = scene.CreateObject();
		var renderer = thing.Components.Create<SkinnedModelRenderer>( false );

		renderer.Model = Triangle();
		renderer.Enabled = true;

		return renderer;
	}

	//A graph is only ever a description until something asks it what it holds, and that is what both the inspector and the movie binder ask.
	[TestMethod]
	public void AGraphSaysWhatItsParametersAre()
	{
		var graph = GraphWithParameters();

		Assert.IsNotNull( graph );
		Assert.AreEqual( 5, graph.ParamCount, "every declared parameter, and only the declared ones" );

		Assert.AreEqual( typeof( bool ), graph.GetParameterType( "moving" ) );
		Assert.AreEqual( typeof( float ), graph.GetParameterType( "move_speed" ) );
		Assert.AreEqual( typeof( Vector3 ), graph.GetParameterType( "aim_direction" ) );

		//An id is a name to the graph and a position in its option list to whoever sets it - which is a byte, the same as an enum.
		Assert.AreEqual( typeof( byte ), graph.GetParameterType( "holdtype" ) );

		//A place in the world is handed over while the graph runs, not assigned a value, so there is no control to put in front of anyone.
		Assert.IsNull( graph.GetParameterType( "look_at" ) );

		Assert.IsNull( graph.GetParameterType( "nothing_declares_this" ) );

		var speed = graph.Parameter( "move_speed" );

		Assert.IsNotNull( speed );
		Assert.AreEqual( 320.0f, speed.Max, 0.001f, "the bounds the graph declared are what the slider is drawn from" );

		var holdtype = graph.Parameter( "holdtype" );

		Assert.IsNotNull( holdtype );
		Assert.AreEqual( 3, holdtype.Options.Count );
		Assert.AreEqual( "pistol", holdtype.Options[1] );
	}

	//The field is an override: setting it drives the model with that graph, and clearing it goes
	//back to whatever the model itself names - which for a model naming nothing is nothing.
	[TestMethod]
	public void TheGraphFieldOverridesTheModelsOwn()
	{
		var renderer = Renderer( out var scene );

		using var scope = scene.Push();

		Assert.IsNull( renderer.AnimGraph, "nothing has overridden anything yet" );
		Assert.IsNull( renderer.EffectiveAnimationGraph, "and this model names no graph of its own" );

		var graph = GraphWithParameters();

		renderer.AnimGraph = graph;

		Assert.AreSame( graph, renderer.EffectiveAnimationGraph );
		Assert.AreSame( graph, renderer.SceneModel.AnimGraph,
			"setting the field has to reach the thing being animated, or the override is a value nobody reads" );

		renderer.AnimGraph = null;

		Assert.IsNull( renderer.EffectiveAnimationGraph, "cleared, it falls back to the model's - which is none" );
		Assert.IsNull( renderer.SceneModel.AnimGraph );
	}

	//The Parameters group shows itself when there is a graph with parameters in it, whichever of the two graphs that is.
	[TestMethod]
	public void TheParametersGroupFollowsTheGraphThatIsPlaying()
	{
		var renderer = Renderer( out var scene );

		using var scope = scene.Push();

		Assert.IsFalse( renderer.ShouldShowParametersEditor, "no graph, nothing to show" );

		var graph = GraphWithParameters();

		renderer.AnimGraph = graph;

		Assert.IsTrue( renderer.ShouldShowParametersEditor,
			"a graph declaring five parameters is exactly when the group is worth having" );

		Assert.AreSame( graph, renderer.Parameters.Graph,
			"and the rows are built from the graph that is playing" );

		//Turning the graph off is saying the model is posed some other way - by a sequence, or by a ragdoll - and its parameters are then nobody's business.
		renderer.UseAnimGraph = false;

		Assert.IsFalse( renderer.ShouldShowParametersEditor );

		renderer.UseAnimGraph = true;
		renderer.AnimGraph = null;

		Assert.IsFalse( renderer.ShouldShowParametersEditor, "and back to nothing when the graph goes" );
	}

	//Graph parameters read back what was set, since the backend itself always answers zero
	[TestMethod]
	public void AParameterReadsBackWhatWasSet()
	{
		var renderer = Renderer( out var scene );

		using var scope = scene.Push();

		renderer.AnimGraph = GraphWithParameters();

		renderer.Set( "moving", true );
		renderer.Set( "move_speed", 137.5f );
		renderer.Set( "holdtype", 2 );
		renderer.Set( "aim_direction", new Vector3( 0, 0, 1 ) );

		Assert.IsTrue( renderer.GetBool( "moving" ) );
		Assert.AreEqual( 137.5f, renderer.GetFloat( "move_speed" ), 0.001f );
		Assert.AreEqual( 2, renderer.GetInt( "holdtype" ) );
		Assert.AreEqual( new Vector3( 0, 0, 1 ), renderer.GetVector( "aim_direction" ) );

		//Set by another name and the answer moves with it - the store keeps one value per parameter however it was reached.
		renderer.Set( "move_speed", 0.0f );

		Assert.AreEqual( 0.0f, renderer.GetFloat( "move_speed" ), 0.001f );

		//And forgotten outright when it is cleared, which is what the row's reset does.
		renderer.ClearParameter( "moving" );

		Assert.IsFalse( renderer.ContainsParameter( "moving" ) );
	}

	//A parameter set before the renderer is enabled is still set afterwards. Which is the case the store exists for: a component's properties
	//are deserialized before it is enabled, so every parameter a saved scene carries is set while there is no scene model to set it on.
	[TestMethod]
	public void AParameterSetBeforeThereIsAnythingToSetItOnSurvives()
	{
		ThreadSafe.MarkMainThread();

		var scene = new Scene();

		using var scope = scene.Push();

		var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );

		renderer.Set( "move_speed", 42.0f );

		Assert.AreEqual( 42.0f, renderer.GetFloat( "move_speed" ), 0.001f,
			"there is no scene model yet, so the store is the only place this can have gone" );

		renderer.Model = Triangle();
		renderer.AnimGraph = GraphWithParameters();
		renderer.Enabled = true;

		Assert.AreEqual( 42.0f, renderer.GetFloat( "move_speed" ), 0.001f );
	}
}
