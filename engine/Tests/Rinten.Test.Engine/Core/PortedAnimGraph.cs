using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;
using Rinten.Porting;
using Rinten.Porting.SBox;

namespace EngineTests.Core;

//Imported anim graph, checked against the citizen's real graph
[TestClass]
[DoNotParallelize]
public class PortedAnimGraphTests
{
	private static string Citizen
	{
		get
		{
			var install = SBoxBaseContent.Find();
			if ( install is null ) return null;

			var path = Path.Combine( install, "addons", "citizen", "Assets",
				"models", "citizen", "citizen.vanmgrph_c" );

			return File.Exists( path ) ? path : null;
		}
	}

	private static string Translate( string into, PortReport report )
		=> Written( into, SBoxAnimGraph.Translate( Citizen, "models/citizen/citizen.vanmgrph_c", null, report, out var definition ), definition );

	//A translation written out under into, so the checks below read it as a file: the document
	//a model would carry, and the definition beside it. Null where nothing came of it.
	private static string Written( string into, string document, string definition )
	{
		if ( document is null ) return null;

		Directory.CreateDirectory( into );
		File.WriteAllText( Path.Combine( into, "graph.json" ), document );

		if ( definition is not null )
			File.WriteAllText( Path.Combine( into, "graph.definition.json" ), definition );

		return "graph.json";
	}

	[TestMethod]
	public void ACharactersGraphComesAcross()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );

		try
		{
			var report = new PortReport();
			var resource = Translate( into, report );

			Assert.IsNotNull( resource, "nothing came out of a graph with a thousand nodes in it" );

			var text = File.ReadAllText( Path.Combine( into, resource.Replace( '/', Path.DirectorySeparatorChar ) ) );
			var root = System.Text.Json.Nodes.JsonNode.Parse( text ).AsObject();

			//The skeleton it poses, which the graph itself remembers.
			Assert.AreEqual( "models/citizen/citizen.mdl", root["Model"]?.GetValue<string>() );

			//Every value the game sets has to exist, or setting it does nothing and the character never hears about it.
			var parameters = root["parameters"].AsArray()
				.Select( x => x.AsObject()["Name"]?.GetValue<string>() ?? "" )
				.ToHashSet();

			foreach ( var wanted in new[]
			{
				"move_x", "move_y", "move_speed", "move_direction", "duck",
				"b_grounded", "b_jump", "holdtype", "holdtype_handedness", "move_style",
				"aim_body", "aim_head", "aim_eyes", "sit", "special_movement_states",
			} )
			{
				Assert.IsTrue( parameters.Contains( wanted ),
					$"the graph has no {wanted}, and the citizen helper sets it every frame" );
			}

			Assert.IsTrue( parameters.Count > 60, $"only {parameters.Count} parameters came across" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	//How much of it arrived, and what did not. A translation that silently drops most of a graph still writes a file that loads. What
	//says whether it worked is the count: their graph is a thousand nodes and a couple of hundred states, and so should ours be.
	[TestMethod]
	public void MostOfItArrives()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );

		try
		{
			var report = new PortReport();
			var resource = Translate( into, report );

			var text = File.ReadAllText( Path.Combine( into, resource.Replace( '/', Path.DirectorySeparatorChar ) ) );

			//Counted off what the port said, which is what the person reading the report is told.
			var said = string.Join( " ", report.Notes );

			Assert.IsTrue( said.Contains( "state(s)" ), "the port said nothing about what it translated" );

			//The shapes a character's graph is made of. None of these being there means a whole kind of node was dropped.
			foreach ( var wanted in new[] { "StateMachineNode", "AnimationClipNode" } )
			{
				Assert.IsTrue( text.Contains( $"\"{wanted}\"" ),
					$"nothing in the translated graph is a {wanted}" );
			}

			//Speed-driven blend positions convert from inches/s to metres/s; angle-driven ones stay
			var speeds = new List<float>();
			var counted = 0;

			foreach ( var (parameter, position) in Blends( text ) )
			{
				++counted;

				if ( !parameter.StartsWith( "move_" ) && !parameter.StartsWith( "wish_" ) ) continue;
				if ( !parameter.EndsWith( "_x" ) && !parameter.EndsWith( "_y" ) && !parameter.EndsWith( "speed" ) ) continue;

				//How fast it turns, which is degrees a second in either engine.
				if ( parameter.Contains( "rotation" ) ) continue;

				speeds.Add( position );
			}

			Assert.IsTrue( counted > 20, $"only {counted} blend positions in the whole graph" );
			Assert.IsTrue( speeds.Count > 5, $"only {speeds.Count} of them are driven by how fast the character is going" );

			//A citizen runs at five metres a second. In their units that is two hundred, and the outermost anchor of a blend is two and a half thousand.
			var furthest = speeds.OrderByDescending( System.MathF.Abs ).First();

			Assert.IsTrue( System.MathF.Abs( furthest ) < 200.0f,
				$"a speed blend sits at {furthest}, which is inches" );

			Assert.IsTrue( speeds.Any( x => System.MathF.Abs( x ) is > 0.5f and < 10.0f ),
				"nothing blends at a speed a person walks or runs at" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	//Every blend point in the graph, with the name of what drives it. Read out of the definition rather than off the builder,
	//because what is checked is the file a game will load - a conversion applied everywhere except on the way out is not applied.
	private static IEnumerable<(string Parameter, float Position)> Blends( string definition )
	{
		var root = System.Text.Json.Nodes.JsonNode.Parse( definition ).AsObject();
		foreach ( var found in BlendNodes( root ) )
		{
			var (graph, node) = found;
			var parameterNodes = graph["nodes"].AsArray()
				.Select( item => item.AsObject() )
				.Where( item => (item["_class"]?.GetValue<string>() ?? "").EndsWith( "ParameterNode" ) )
				.ToDictionary( item => item["Identifier"]?.GetValue<string>() ?? "", item => item["ParameterName"]?.GetValue<string>() ?? "" );

			if ( node["_class"]?.GetValue<string>() == "Blend1DNode" )
			{
				var parameter = LinkedParameterName( node["Parameter"], parameterNodes );
				foreach ( var point in node["Points"].AsArray().Select( item => item.AsObject() ) )
				{
					yield return (parameter, point["Value"]?.GetValue<float>() ?? 0f);
				}
			}

			if ( node["_class"]?.GetValue<string>() == "Blend2DNode" )
			{
				var x = LinkedParameterName( node["X"], parameterNodes );
				var y = LinkedParameterName( node["Y"], parameterNodes );

				foreach ( var point in node["Points"].AsArray().Select( item => item.AsObject() ) )
				{
					var position = (point["Position"]?.GetValue<string>() ?? "0,0").Split( ',' );
					if ( position.Length > 0 && float.TryParse( position[0], System.Globalization.CultureInfo.InvariantCulture, out var px ) )
						yield return (x, px);
					if ( position.Length > 1 && float.TryParse( position[1], System.Globalization.CultureInfo.InvariantCulture, out var py ) )
						yield return (y, py);
				}
			}
		}
	}

	private static IEnumerable<(System.Text.Json.Nodes.JsonObject Graph, System.Text.Json.Nodes.JsonObject Node)> BlendNodes( System.Text.Json.Nodes.JsonObject graph )
	{
		if ( graph["nodes"] is not System.Text.Json.Nodes.JsonArray nodes )
			yield break;

		foreach ( var item in nodes.Select( node => node.AsObject() ) )
		{
			var type = item["_class"]?.GetValue<string>() ?? "";
			if ( type is "Blend1DNode" or "Blend2DNode" )
				yield return (graph, item);

			foreach ( var sub in item.Select( property => property.Value ).OfType<System.Text.Json.Nodes.JsonObject>() )
			{
				if ( sub["nodes"] is null ) continue;

				foreach ( var found in BlendNodes( sub ) )
					yield return found;
			}

			foreach ( var array in item.Select( property => property.Value ).OfType<System.Text.Json.Nodes.JsonArray>() )
			{
				foreach ( var sub in array.OfType<System.Text.Json.Nodes.JsonObject>() )
				{
					if ( sub["Graph"] is not System.Text.Json.Nodes.JsonObject nested ) continue;

					foreach ( var found in BlendNodes( nested ) )
						yield return found;
				}
			}
		}
	}

	private static string LinkedParameterName( System.Text.Json.Nodes.JsonNode link, Dictionary<string, string> parameterNodes )
	{
		if ( link is not System.Text.Json.Nodes.JsonObject obj ) return "";
		return parameterNodes.TryGetValue( obj["Identifier"]?.GetValue<string>() ?? "", out var name ) ? name : "";
	}

	//What the port owns up to. Every node kind with no counterpart here is named and counted,
	//because a graph that quietly drops a piece of itself is worse than one that says which piece.
	[TestMethod]
	public void ItSaysWhatItCouldNotBring()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );

		try
		{
			var report = new PortReport();
			Translate( into, report );

			var said = string.Join( " ", report.Notes );

			Assert.IsTrue( said.Contains( "passed through" ),
				"the port claims it translated everything, and their graph has nodes ours has no answer for" );

			//Named and counted, not merely mentioned: what is left has to be readable as a list of things to go and do.
			Assert.IsTrue( System.Text.RegularExpressions.Regex.IsMatch( said, @"[A-Z][a-z ]+ \(\d+\)" ),
				$"what was left behind was not named: {said}" );

			//And the two that aim a character are not in that list. An aim grid is one clip of poses picked out by two angles, which is a blend space; a look
			//is a chain of bones the model carries. Both come across whole, and a character that cannot aim is what it looks like when they stop doing so.
			foreach ( var brought in new[] { "Aim matrix", "Look at" } )
			{
				Assert.IsFalse( said.Contains( brought ),
					$"'{brought}' was passed through, and it is how the character aims: {said}" );
			}
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	[TestMethod]
	public void AnEditorWeaponGraphKeepsItsStatesAndClips()
	{
		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );
		var source = Path.Combine( into, "weapon.vanmgrph" );

		try
		{
			Directory.CreateDirectory( into );
			File.WriteAllText( source, """
				<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} -->
				{
					_class = "CAnimGraphDoc_Graph"
					m_modelName = "models/weapons/mp5.vmdl"
					m_nodeManager =
					{
						_class = "CAnimGraphDoc_NodeManager"
						m_nodes =
						[
							{
								_class = "CAnimGraphDoc_RootNode"
								m_nNodeID = { m_id = 1 }
								m_inputConnection = { m_nodeID = { m_id = 2 } }
							}
							{
								_class = "CAnimGraphDoc_StateMachineNode"
								m_nNodeID = { m_id = 2 }
								m_states =
								[
									{
										m_stateID = { m_id = 20 }
										m_bIsStartState = true
										m_inputConnection = { m_nodeID = { m_id = 3 } }
										m_transitions = []
									}
									{
										m_stateID = { m_id = 21 }
										m_inputConnection = { m_nodeID = { m_id = 4 } }
										m_transitions = []
									}
								]
							}
							{
								_class = "CAnimGraphDoc_SequenceNode"
								m_nNodeID = { m_id = 3 }
								m_sequenceName = "idle"
								m_bLoop = true
								m_playbackSpeed = 1.0
							}
							{
								_class = "CAnimGraphDoc_SequenceNode"
								m_nNodeID = { m_id = 4 }
								m_sequenceName = "fire"
								m_bLoop = false
								m_playbackSpeed = 1.0
							}
						]
					}
				}
				""" );

			var report = new PortReport();
			var resource = Written( into, SBoxAnimGraph.Translate( source, "models/weapons/mp5.vanmgrph", null, report, out var definition ), definition );

			Assert.IsNotNull( resource, "an editor graph with a state machine should translate" );

			var text = File.ReadAllText( Path.Combine( into, resource.Replace( '/', Path.DirectorySeparatorChar ) ) );

			Assert.IsTrue( text.Contains( "\"StateMachineNode\"" ), "the state machine did not come across" );
			Assert.IsTrue( text.Contains( "\"AnimationClipNode\"" ), "the clips did not come across" );
			Assert.IsTrue( text.Contains( "idle" ), "the idle clip was dropped" );
			Assert.IsTrue( text.Contains( "fire" ), "the fire clip was dropped" );
			Assert.IsFalse( IsOnlyAPose( text ), "the graph collapsed to a single pose node" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	[TestMethod]
	public void DirectPlaybackIsNotCollapsedToAPose()
	{
		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );
		var source = Path.Combine( into, "view.vanmgrph" );

		try
		{
			Directory.CreateDirectory( into );
			File.WriteAllText( source, """
				<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} -->
				{
					_class = "CAnimationGraph"
					m_nodeManager =
					{
						m_nodes =
						[
							{
								key = { m_id = 1 }
								value =
								{
									_class = "CRootAnimNode"
									m_inputConnection = { m_nodeID = { m_id = 2 } }
								}
							}
							{
								key = { m_id = 2 }
								value = { _class = "CDirectPlaybackAnimNode" }
							}
						]
					}
				}
				""" );

			var report = new PortReport();
			var resource = Written( into, SBoxAnimGraph.Translate( source, "models/weapons/view.vanmgrph", null, report, out var definition ), definition );

			Assert.IsNotNull( resource, "a direct-playback graph should still write" );

			var text = File.ReadAllText( Path.Combine( into, resource.Replace( '/', Path.DirectorySeparatorChar ) ) );

			Assert.IsTrue( text.Contains( "\"ExternalPoseNode\"" ), "direct playback should remain a node the game can drive" );
			Assert.IsFalse( IsOnlyAPose( text ), "direct playback collapsed to a pose" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	//Every object in a translated graph, however deeply it is nested.
	private static IEnumerable<System.Text.Json.Nodes.JsonObject> Everything( System.Text.Json.Nodes.JsonNode node )
	{
		switch ( node )
		{
			case System.Text.Json.Nodes.JsonObject o:
				yield return o;
				foreach ( var pair in o )
					foreach ( var found in Everything( pair.Value ) ) yield return found;
				break;

			case System.Text.Json.Nodes.JsonArray a:
				foreach ( var item in a )
					foreach ( var found in Everything( item ) ) yield return found;
				break;
		}
	}

	private static string Kind( System.Text.Json.Nodes.JsonObject node )
		=> node["_class"]?.GetValue<string>() ?? "";

	private static List<System.Text.Json.Nodes.JsonObject> Translated( string into )
	{
		var report = new PortReport();
		var resource = Translate( into, report );

		Assert.IsNotNull( resource, "nothing came out of a graph with a thousand nodes in it" );

		var text = File.ReadAllText( Path.Combine( into, resource.Replace( '/', Path.DirectorySeparatorChar ) ) );

		return Everything( System.Text.Json.Nodes.JsonNode.Parse( text ) ).ToList();
	}

	//Aim grid frames run bottom to top, left to right: frame 0 aims down-left, frame 4 straight ahead
	[TestMethod]
	public void AnAimGridsBottomRowAimsDown()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );

		try
		{
			var all = Translated( into );

			//Which frame each grid point holds, so a position can be read against the pose it belongs to.
			var frames = all
				.Where( node => Kind( node ) == "AnimationPoseNode" )
				.Where( node => node["UseFramesAsInput"]?.GetValue<bool>() == true )
				.ToDictionary(
					node => node["Identifier"]?.GetValue<string>() ?? "",
					node => (int)( node["FixedTime"]?.GetValue<float>() ?? -1.0f ) );

			var grids = 0;

			foreach ( var blend in all.Where( node => Kind( node ) == "Blend2DNode" ) )
			{
				if ( blend["Points"] is not System.Text.Json.Nodes.JsonArray points ) continue;

				var placed = new Dictionary<int, Vector2>();

				foreach ( var point in points.OfType<System.Text.Json.Nodes.JsonObject>() )
				{
					var identifier = point["Pose"]?["Identifier"]?.GetValue<string>() ?? "";

					//A blend space of whole clips rather than of one clip's frames is a movement blend, and not this test's business.
					if ( !frames.TryGetValue( identifier, out var frame ) ) { placed.Clear(); break; }

					var text = ( point["Position"]?.GetValue<string>() ?? "" ).Split( ',' );
					if ( text.Length < 2 ) { placed.Clear(); break; }

					placed[frame] = new Vector2(
						float.Parse( text[0], System.Globalization.CultureInfo.InvariantCulture ),
						float.Parse( text[1], System.Globalization.CultureInfo.InvariantCulture ) );
				}

				if ( placed.Count < 9 ) continue;

				grids++;

				var step = placed[0].x;

				Assert.IsTrue( step > 0.0f, "frame 0 is the left column, and left is a positive yaw" );

				Assert.AreEqual( -step, placed[0].y, 0.01f, "frame 0 is the bottom row, so it aims down" );

				Assert.AreEqual( 0.0f, placed[4].x, 0.01f, "frame 4 is the middle of the grid" );
				Assert.AreEqual( 0.0f, placed[4].y, 0.01f, "frame 4 aims straight ahead" );

				Assert.AreEqual( -step, placed[8].x, 0.01f, "frame 8 is the right column" );
				Assert.AreEqual( step, placed[8].y, 0.01f, "frame 8 is the top row, so it aims up" );
			}

			Assert.IsTrue( grids > 15, $"only {grids} aim grids came across, and the citizen has two dozen" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	//Ported looks keep separate yaw and pitch limits and aim their attachment, not the chain's bone
	[TestMethod]
	public void ALookKeepsItsTwoLimitsApart()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );

		try
		{
			var looks = Translated( into )
				.Where( node => Kind( node ) == "LookAtNode" )
				.Select( node => (
					Chain: node["Chain"]?.GetValue<string>() ?? "",
					Attachment: node["Attachment"]?.GetValue<string>() ?? "",
					Yaw: node["YawLimit"]?.GetValue<float>() ?? -1.0f,
					Pitch: node["PitchLimit"]?.GetValue<float>() ?? -1.0f ) )
				.ToList();

			Assert.IsTrue( looks.Count > 40, $"only {looks.Count} looks came across" );

			Assert.IsTrue( looks.All( look => look.Attachment.Length > 0 ),
				"a look with nothing to aim points the first bone of its chain, which on a spine is up" );

			Assert.IsTrue( looks.All( look => look.Yaw >= 0.0f && look.Pitch >= 0.0f ),
				"a look with no limits written turns as far as it likes" );

			//The spine is the one that proves they were not collapsed.
			Assert.IsTrue( looks.Any( look => look.Yaw > look.Pitch * 2.0f ),
				"no look turns further round than it leans, so the two were taken as one" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	//Hold type composites blend in model space, so mask ramps don't compound down the arm
	[TestMethod]
	public void AHoldTypeIsBlendedWhereTheBonesStand()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );

		try
		{
			var layers = Translated( into )
				.Where( node => Kind( node ) == "LocalLayerNode" )
				.ToList();

			Assert.IsTrue( layers.Count > 100, $"only {layers.Count} layers came across" );

			var holds = layers
				.Where( node => node["BlendInModelSpace"]?.GetValue<bool>() == true )
				.Where( node => ( node["BlendMode"]?.GetValue<string>() ?? "" ) == "Overlay" )
				.ToList();

			Assert.IsTrue( holds.Count > 60,
				$"only {holds.Count} masked blends were drawn as model space, and the citizen composites 74" );

			Assert.IsTrue( holds.All( node => node["RotationOnly"]?.GetValue<bool>() == true ),
				"a hold type was drawn blending positions as well as turns" );

			Assert.IsTrue( holds.All( node => node["BoneMask"] is not null ),
				"a model-space blend was drawn with no mask, which is the whole body" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	//A model-space aim grid still adds to the pose instead of replacing it
	[TestMethod]
	public void AModelSpaceAimGridStillAdds()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );

		try
		{
			var modes = Translated( into )
				.Where( node => Kind( node ) == "LocalLayerNode" )
				.Select( node => node["BlendMode"]?.GetValue<string>() ?? "" )
				.ToList();

			Assert.IsTrue( modes.Any( mode => mode == "ModelSpace" ),
				"their model-space aim grids all came out as something else" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	//The compiled graph aims the same way as the page; the two come from different writers
	[TestMethod]
	public void TheCompiledGraphAimsTheSameWayThePageDoes()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var into = Path.Combine( Path.GetTempPath(), $"rinten-anim-{System.Guid.NewGuid():N}" );

		try
		{
			var report = new PortReport();
			var resource = Translate( into, report );

			Assert.IsNotNull( resource );

			var compiled = Path.Combine( into, "graph.definition.json" );

			Assert.IsTrue( File.Exists( compiled ), "a ported graph animates before anything has compiled it" );

			var root = System.Text.Json.Nodes.JsonNode.Parse( File.ReadAllText( compiled ) ).AsObject();

			var nodes = ( root["Nodes"] as System.Text.Json.Nodes.JsonArray )
				?.OfType<System.Text.Json.Nodes.JsonObject>().ToList();

			Assert.IsTrue( nodes is { Count: > 1000 }, "the compiled graph is most of a thousand nodes" );

			static string Of( System.Text.Json.Nodes.JsonObject node )
				=> node["Kind"]?.GetValue<string>() ?? "";

			var grids = 0;

			foreach ( var blend in nodes.Where( node => Of( node ) == "BlendSpace2D" ) )
			{
				if ( blend["Sources"] is not System.Text.Json.Nodes.JsonArray sources ) continue;
				if ( blend["Positions"] is not System.Text.Json.Nodes.JsonArray places ) continue;

				var placed = new Dictionary<int, Vector2>();
				var readable = true;

				for ( var i = 0; i < sources.Count && readable; i++ )
				{
					var index = sources[i]?.GetValue<int>() ?? -1;
					if ( index < 0 || index >= nodes.Count ) { readable = false; break; }

					var source = nodes[index];

					//Anything but a grid of one clip's frames is a movement blend, and not this test's business.
					if ( Of( source ) != "Pose" || source["Frames"]?.GetValue<bool>() != true )
					{
						readable = false;
						break;
					}

					if ( places[i] is not System.Text.Json.Nodes.JsonArray point || point.Count < 2 )
					{
						readable = false;
						break;
					}

					placed[(int)( source["FixedTime"]?.GetValue<float>() ?? -1.0f )] =
						new Vector2( point[0].GetValue<float>(), point[1].GetValue<float>() );
				}

				if ( !readable || placed.Count < 9 ) continue;

				grids++;

				var step = placed[0].x;

				Assert.IsTrue( step > 0.0f, "frame 0 is the left column" );
				Assert.AreEqual( -step, placed[0].y, 0.01f, "frame 0 is the bottom row, so it aims down" );

				Assert.AreEqual( 0.0f, placed[4].x, 0.01f, "frame 4 aims straight ahead" );
				Assert.AreEqual( 0.0f, placed[4].y, 0.01f, "frame 4 aims straight ahead" );

				Assert.AreEqual( -step, placed[8].x, 0.01f, "frame 8 is the right column" );
				Assert.AreEqual( step, placed[8].y, 0.01f, "frame 8 is the top row, so it aims up" );
			}

			Assert.IsTrue( grids > 15, $"only {grids} aim grids reached the compiled graph" );

			//The two limits, still apart by the time the engine reads them.
			var looks = nodes.Where( node => Of( node ) == "LookAt" ).ToList();

			Assert.IsTrue( looks.Count > 40, $"only {looks.Count} looks reached the compiled graph" );

			//What each look actually aims. Their citizen names one on every look there is, and the graph the game loads is the half that has to carry it.
			Assert.IsTrue( looks.All( look => ( look["Attachment"]?.GetValue<string>() ?? "" ).Length > 0 ),
				"a look reached the compiled graph with nothing to aim but its chain's first bone" );

			Assert.IsTrue( looks.Any( look =>
				( look["YawLimit"]?.GetValue<float>() ?? 0.0f )
				> ( look["PitchLimit"]?.GetValue<float>() ?? 0.0f ) * 2.0f ),
				"no look turns further round than it leans, so the two were taken as one" );

			//Model space means two things by the mode it is on: a difference authored against the
			//model where the layer adds, and a blend of where the bones stand where it does not.
			var layers = nodes
				.Where( node => Of( node ) == "Layers" )
				.SelectMany( node => ( node["Layers"] as System.Text.Json.Nodes.JsonArray
					?? new System.Text.Json.Nodes.JsonArray() ).OfType<System.Text.Json.Nodes.JsonObject>() )
				.ToList();

			var modelSpace = layers
				.Where( layer => layer["ModelSpace"]?.GetValue<bool>() == true )
				.ToList();

			Assert.IsTrue( modelSpace.Count > 0, "nothing came across as authored against the model" );

			Assert.IsTrue( modelSpace.Any( layer => ( layer["BlendMode"]?.GetValue<string>() ?? "" ) == "Additive" ),
				"their model-space adds all came out as something else" );

			//Their hold types: a mask of part-weights up the body, blended where the bones stand. Read
			//under each parent instead, the shares compound down the arm and the hand misses the weapon.
			var holds = modelSpace
				.Where( layer => ( layer["BlendMode"]?.GetValue<string>() ?? "" ) == "Overlay" )
				.ToList();

			Assert.IsTrue( holds.Count > 60,
				$"only {holds.Count} masked blends came across as model space, and the citizen composites 74" );

			Assert.IsTrue( holds.All( layer => layer["RotationOnly"]?.GetValue<bool>() == true ),
				"a hold type blended positions as well as turns, which stretches whatever it covers" );

			Assert.IsTrue( holds.All( layer => ( layer["Mask"]?.GetValue<int>() ?? -1 ) >= 0 ),
				"a model-space blend with no mask replaces the whole body" );
		}
		finally
		{
			if ( Directory.Exists( into ) ) Directory.Delete( into, true );
		}
	}

	static bool IsOnlyAPose( string definition )
	{
		var root = System.Text.Json.Nodes.JsonNode.Parse( definition ).AsObject();
		if ( root["nodes"] is not System.Text.Json.Nodes.JsonArray nodes ) return true;

		return definition.Contains( "\"ReferencePose\"" )
			&& !nodes.OfType<System.Text.Json.Nodes.JsonObject>().Any( x =>
				(x["_class"]?.GetValue<string>() ?? "") is "AnimationClipNode" or "StateMachineNode" or "ExternalPoseNode" or "SelectorNode" );
	}
}
