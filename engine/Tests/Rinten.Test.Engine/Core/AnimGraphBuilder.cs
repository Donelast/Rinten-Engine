using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace EngineTests;

//Building a graph and reading it back. The builder is the contract between the graph editor and the runtime: the editor calls it, the
//runtime reads what it wrote. These check the join - that a graph goes in as method calls and comes out as something with the same shape.
[TestClass]
public class AnimGraphBuilderTest
{
	//A graph with a parameter, a clip, a blend and a state machine in it - enough kinds that a definition which loses one shows up here.
	[TestMethod]
	public void BuildsADefinitionThatReadsBack()
	{
		var builder = AnimGraph.Builder;
		builder.Skeleton = "models/citizen/citizen.mdl";

		var speed = builder.FloatParameter( "move_speed", 0.0f, 320.0f );
		var moving = builder.Compare( speed, AnimGraphBuilder.FloatComparison.GreaterThan, 1.0f );

		var idle = builder.Clip( "animations/idle.anim", new AnimGraphBuilder.ClipSettings { Looping = true } );
		var walk = builder.Clip( "animations/walk.anim", new AnimGraphBuilder.ClipSettings { Looping = true, Speed = 1.5f } );

		var machine = builder.StateMachine();

		var idleState = machine.AddState( idle );
		var walkState = machine.AddState( walk, new AnimGraphBuilder.StateMachineBuilder.StateSettings
		{
			EntryEvents = new[] { "started_walking" },
		} );

		machine.AddTransition( idleState, walkState,
			machine.CreateTransition( walkState, new AnimGraphBuilder.TransitionSettings { Duration = 0.2f } ),
			new AnimGraphBuilder.StateMachineBuilder.TransitionRouteSettings { Condition = moving } );

		var definition = builder.Build( machine );
		var graph = AnimGraph.FromDefinition( definition, "test" );

		Assert.IsNotNull( graph, "the definition should read back" );
		Assert.IsTrue( graph.IsValid );
		Assert.IsFalse( graph.IsError );

		Assert.AreEqual( "models/citizen/citizen.mdl", graph.ModelName );

		//The root is the state machine, which was the last pose node built.
		Assert.AreEqual( ((AnimGraphBuilder.Node)machine).GetRuntimeIndex(), RootOf( definition ) );

		//One parameter, and it is the one that was declared rather than any the graph made for itself.
		Assert.AreEqual( 1, graph.Parameters.Count );
		Assert.AreEqual( "move_speed", graph.Parameters[0].Name );
		Assert.AreEqual( AnimGraphBuilder.NodeKind.Float, graph.Parameters[0].Kind );
		Assert.AreEqual( 320.0f, graph.Parameters[0].Max, 0.001f );

		Assert.IsNotNull( graph.Parameter( "MOVE_SPEED" ), "parameters are found whatever the case" );
		Assert.IsNull( graph.Parameter( "nothing_called_this" ) );

		//And the clip that was asked for is in there, with what was said about it.
		var clip = NodeOf( definition, walk.GetRuntimeIndex() );

		Assert.AreEqual( "Clip", clip["Kind"].GetValue<string>() );
		Assert.AreEqual( "animations/walk.anim", clip["Clip"].GetValue<string>() );
		Assert.AreEqual( 1.5f, clip["Speed"].GetValue<float>(), 0.001f );
		Assert.IsTrue( clip["Looping"].GetValue<bool>() );
	}

	//A node handle is where the node is, and nothing connected is no handle at all - not the
	//first node, which is what an unconnected input would be if nothing were spelled as a zero.
	[TestMethod]
	public void NothingConnectedIsNotTheFirstNode()
	{
		var builder = AnimGraph.Builder;

		var first = builder.ReferencePose();

		Assert.IsTrue( first.IsValid );
		Assert.AreEqual( 0, first.GetRuntimeIndex(), "the first node built is node zero" );

		AnimGraphBuilder.Node nothing = default;

		Assert.IsNull( nothing, "an input nobody connected is nothing, not a node" );
		Assert.AreNotEqual( nothing, first );

		var second = builder.ZeroPose();
		Assert.AreEqual( 1, second.GetRuntimeIndex() );

		//An input nobody connected reads as -1 in the definition rather than as node zero.
		var blend = builder.Blend( first, second, default( AnimGraphBuilder.Node ) );
		var node = NodeOf( builder.Build( blend ), blend.GetRuntimeIndex() );

		Assert.AreEqual( 0, node["A"].GetValue<int>() );
		Assert.AreEqual( 1, node["B"].GetValue<int>() );
		Assert.AreEqual( -1, node["Weight"].GetValue<int>(), "nothing connected is nothing" );
	}

	//Transitions out of a state are tried in the order they were added, which is the order the editor drew them in. A state
	//where two conditions are true at once has to pick one, and the first written is the only answer an author can predict.
	[TestMethod]
	public void TransitionsKeepTheOrderTheyWereAddedIn()
	{
		var builder = AnimGraph.Builder;
		var machine = builder.StateMachine();

		var from = machine.AddState( builder.ReferencePose() );
		var first = machine.AddState( builder.ZeroPose() );
		var second = machine.AddState( builder.ReferencePose() );

		machine.AddTransition( from, first, machine.CreateTransition( first ) );
		machine.AddTransition( from, second, machine.CreateTransition( second ) );

		var node = NodeOf( builder.Build( machine ), ((AnimGraphBuilder.Node)machine).GetRuntimeIndex() );

		var routes = node["States"][from.GetRuntimeIndex()]["Routes"].AsArray();

		Assert.AreEqual( 2, routes.Count );
		Assert.AreEqual( first.GetRuntimeIndex(), routes[0]["To"].GetValue<int>() );
		Assert.AreEqual( second.GetRuntimeIndex(), routes[1]["To"].GetValue<int>() );
	}

	//A two-dimensional blend space needs to know which poses are neighbours.
	[TestMethod]
	public void TriangulatesABlendSpace()
	{
		//A square, which is two triangles however they are drawn, with all four corners on the outline.
		var square = new List<Vector2>
		{
			new( 0, 0 ),
			new( 1, 0 ),
			new( 1, 1 ),
			new( 0, 1 ),
		};

		AnimGraphBuilder.TriangulateBlendSpace( square, out var triangles, out var hull );

		Assert.AreEqual( 6, triangles.Count, "two triangles, three indices each" );
		Assert.AreEqual( 4, hull.Count, "every corner is on the outline" );

		foreach ( var index in triangles )
			Assert.IsTrue( index >= 0 && index < square.Count, $"index {index} is not one of the points" );

		//The property that makes it Delaunay: no point sits inside another triangle's circumcircle. Long
		//thin triangles are what make a blend jump as it crosses one, and this is what rules them out.
		for ( var t = 0; t < triangles.Count; t += 3 )
		{
			var a = square[triangles[t]];
			var b = square[triangles[t + 1]];
			var c = square[triangles[t + 2]];

			for ( var p = 0; p < square.Count; p++ )
			{
				if ( p == triangles[t] || p == triangles[t + 1] || p == triangles[t + 2] ) continue;

				Assert.IsFalse( StrictlyInside( a, b, c, square[p] ),
					$"point {p} is inside the circle through {triangles[t]}, {triangles[t + 1]}, {triangles[t + 2]}" );
			}
		}

		//A point in the middle should be used by the triangles around it.
		var withCentre = new List<Vector2>( square ) { new( 0.5f, 0.5f ) };

		AnimGraphBuilder.TriangulateBlendSpace( withCentre, out var moreTriangles, out var sameHull );

		Assert.AreEqual( 12, moreTriangles.Count, "four triangles around the middle" );
		Assert.AreEqual( 4, sameHull.Count, "the middle is not on the outline" );
		CollectionAssert.Contains( moreTriangles, 4, "and it is used" );
	}

	//Points in a line have no triangles between them. That is not a failure - it is a
	//one-dimensional blend space drawn in two dimensions, and the outline alone describes it.
	[TestMethod]
	public void PointsInALineHaveNoTriangles()
	{
		var line = new List<Vector2> { new( 0, 0 ), new( 1, 0 ), new( 2, 0 ), new( 3, 0 ) };

		AnimGraphBuilder.TriangulateBlendSpace( line, out var triangles, out var hull );

		Assert.AreEqual( 0, triangles.Count );
		Assert.IsTrue( hull.Count >= 2, "the ends of the line are still an outline" );

		//And nothing at all is not a crash.
		AnimGraphBuilder.TriangulateBlendSpace( new List<Vector2>(), out var none, out var noHull );

		Assert.AreEqual( 0, none.Count );
		Assert.AreEqual( 0, noHull.Count );
	}

	//-- shared ---------------------------------------------------------------

	private static int RootOf( string definition ) => JsonNode.Parse( definition )["Root"].GetValue<int>();

	private static JsonObject NodeOf( string definition, int index )
		=> JsonNode.Parse( definition )["Nodes"][index].AsObject();

	//Whether a point is inside the circle through three others - the test that says whether a triangulation is Delaunay.
	private static bool StrictlyInside( Vector2 a, Vector2 b, Vector2 c, Vector2 point )
	{
		var ax = a.x - point.x;
		var ay = a.y - point.y;
		var bx = b.x - point.x;
		var by = b.y - point.y;
		var cx = c.x - point.x;
		var cy = c.y - point.y;

		var determinant =
			( ( ( ax * ax ) + ( ay * ay ) ) * ( ( bx * cy ) - ( cx * by ) ) ) -
			( ( ( bx * bx ) + ( by * by ) ) * ( ( ax * cy ) - ( cx * ay ) ) ) +
			( ( ( cx * cx ) + ( cy * cy ) ) * ( ( ax * by ) - ( bx * ay ) ) );

		var winding = ( ( b.x - a.x ) * ( c.y - a.y ) ) - ( ( c.x - a.x ) * ( b.y - a.y ) );

		//A hair of slack: a point exactly on the circle is allowed, and four corners of a square are all exactly on one.
		const float epsilon = 1e-5f;

		return winding < 0.0f ? determinant < -epsilon : determinant > epsilon;
	}
}
