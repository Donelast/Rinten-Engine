using System.Text.Json.Nodes;
using Rinten.Porting;

namespace EngineTests;

//Scene unit conversion: convert known measurements, skip non-measurements, report unknowns
[TestClass]
public class SceneUnitsTest
{
	//A GameObject's position is a length by the format's own definition, so it converts whatever component it is attached to.
	[TestMethod]
	//Units and only units: these hand the converter an identity basis, so what the turn onto our axes does to
	//a scene is not exercised here. That is the porter's ForeignBasis, and it has no coverage in this suite.
	public void PositionConvertsAndRotationDoesNot()
	{
		var scene = JsonNode.Parse( """
			{
			  "GameObjects": [
			    {
			      "Name": "Sun",
			      "Position": "100,-200,50",
			      "Rotation": "0.749052,0.5386431,-0.3131638,0.2251959",
			      "Scale": "2,2,2"
			    }
			  ]
			}
			""" );

		var report = new PortReport();
		var changed = SceneUnits.Convert( scene, PortUnits.SBox, ForeignBasis.Same, "test.scene", report );

		Assert.AreEqual( 1, changed );

		var go = scene["GameObjects"][0];

		//A hundred inches is two and a half metres.
		Assert.AreEqual( "2.54,-5.08,1.27", go["Position"].GetValue<string>(),
			"a position is a length" );

		Assert.AreEqual( "0.749052,0.5386431,-0.3131638,0.2251959", go["Rotation"].GetValue<string>(),
			"a rotation is the same rotation at any unit" );

		Assert.AreEqual( "2,2,2", go["Scale"].GetValue<string>(),
			"a scale is a ratio between a model and itself" );
	}

	//A component's numbers are told apart by name, because nothing else can tell them apart: these
	//three are a length, a count and a fraction, and all three are written as bare json numbers.
	[TestMethod]
	public void KnownComponentPropertiesConvertByTheirDimension()
	{
		var scene = JsonNode.Parse( """
			{
			  "GameObjects": [
			    {
			      "Name": "Probe",
			      "Components": [
			        {
			          "__type": "Rinten.EnvmapProbe",
			          "MaxDistance": 512,
			          "FrameInterval": 5,
			          "Feathering": 0.02,
			          "Bounds": { "Mins": "-1000,-1000,-1000", "Maxs": "1000,1000,1000" },
			          "TintColor": "1,1,1,1"
			        }
			      ]
			    }
			  ]
			}
			""" );

		var report = new PortReport();
		SceneUnits.Convert( scene, PortUnits.SBox, ForeignBasis.Same, "test.scene", report );

		var probe = scene["GameObjects"][0]["Components"][0];

		Assert.AreEqual( 13.0048f, probe["MaxDistance"].GetValue<double>(), 0.0001,
			"a distance is a length" );

		Assert.AreEqual( 5, probe["FrameInterval"].GetValue<int>(),
			"a frame count is not a length" );

		Assert.AreEqual( 0.02, probe["Feathering"].GetValue<double>(), 0.0001,
			"a fraction is not a length" );

		Assert.AreEqual( "-25.4,-25.4,-25.4", probe["Bounds"]["Mins"].GetValue<string>(),
			"a bounding box is lengths, one level down" );

		Assert.AreEqual( "1,1,1,1", probe["TintColor"].GetValue<string>(),
			"a colour is written like a vector and is not one" );
	}

	//The part that makes this honest: a number the table has never heard of is left exactly as it was and reported. This is the behaviour to defend.
	//Guessing that an unknown number is a length turns somebody's tuning value into a fortieth of itself, and nothing in the engine ever says so.
	[TestMethod]
	public void UnknownPropertiesAreLeftAloneAndReported()
	{
		var scene = JsonNode.Parse( """
			{
			  "GameObjects": [
			    {
			      "Name": "Player",
			      "Components": [
			        {
			          "__type": "MyGame.CubeController",
			          "MoveSpeed": 100.0
			        }
			      ]
			    }
			  ]
			}
			""" );

		var report = new PortReport();
		SceneUnits.Convert( scene, PortUnits.SBox, ForeignBasis.Same, "test.scene", report );

		Assert.AreEqual( 100.0, scene["GameObjects"][0]["Components"][0]["MoveSpeed"].GetValue<double>(), 0.0001,
			"a property nobody has written a dimension for is not converted" );

		Assert.AreEqual( 1, report.Unconverted.Count, "and it is reported" );
		Assert.AreEqual( "MyGame.CubeController.MoveSpeed", report.Unconverted[0].What,
			"named in full, so the report line is the table key" );
	}

	//Prefabs keep their object under a wrapper; conversion must walk into it
	[TestMethod]
	public void APrefabConvertsThroughItsRootObject()
	{
		var prefab = JsonNode.Parse( """
			{
			  "RootObject": {
			    "Name": "dev_player",
			    "Position": "0,0,64",
			    "Components": [
			      {
			        "__type": "Rinten.CharacterController",
			        "Height": 64,
			        "Radius": 16,
			        "GroundAngle": 45
			      }
			    ],
			    "Children": [
			      {
			        "Name": "Camera",
			        "Position": "0,0,60"
			      }
			    ]
			  },
			  "ShowInMenu": false
			}
			""" );

		var report = new PortReport();
		var changed = SceneUnits.Convert( prefab, PortUnits.SBox, ForeignBasis.Same, "dev_player.prefab", report );

		Assert.AreEqual( 4, changed, "the root, its controller's two lengths, and the child" );

		var root = prefab["RootObject"];

		Assert.AreEqual( "0,0,1.6256", root["Position"].GetValue<string>() );
		Assert.AreEqual( "0,0,1.524", root["Children"][0]["Position"].GetValue<string>() );

		var controller = root["Components"][0];

		Assert.AreEqual( 1.6256f, controller["Height"].GetValue<double>(), 0.0001,
			"a character is about one and a half metres, not sixty-four of them" );

		Assert.AreEqual( 45, controller["GroundAngle"].GetValue<int>(),
			"an angle is the same angle at any unit" );
	}

	//A prefab instance's overridden position lives in its patch and must be converted too
	[TestMethod]
	public void APrefabInstancesOverriddenPositionConverts()
	{
		var scene = JsonNode.Parse( """
			{
			  "GameObjects": [
			    {
			      "__Prefab": "prefabs/dev_player.prefab",
			      "__PrefabInstancePatch": {
			        "AddedObjects": [],
			        "RemovedObjects": [],
			        "MovedObjects": [],
			        "PropertyOverrides": [
			          { "Target": { "Type": "GameObject" }, "Property": "Name", "Value": "Player" },
			          { "Target": { "Type": "GameObject" }, "Property": "Position", "Value": "3688,-360,2712" },
			          { "Target": { "Type": "GameObject" }, "Property": "Rotation", "Value": "0,0,0,-1" },
			          { "Target": { "Type": "Component" }, "Property": "ZFar", "Value": 100000 },
			          { "Target": { "Type": "Component" }, "Property": "MoveSpeed", "Value": 250 }
			        ]
			      }
			    }
			  ]
			}
			""" );

		var report = new PortReport();
		SceneUnits.Convert( scene, PortUnits.SBox, ForeignBasis.Same, "dev.scene", report );

		var overrides = scene["GameObjects"][0]["__PrefabInstancePatch"]["PropertyOverrides"];

		Assert.AreEqual( "93.675199,-9.144,68.884799", overrides[1]["Value"].GetValue<string>(),
			"ninety-four metres out, not three and a half kilometres" );

		Assert.AreEqual( "Player", overrides[0]["Value"].GetValue<string>(),
			"a name is not a measurement even where it parses as one" );

		Assert.AreEqual( "0,0,0,-1", overrides[2]["Value"].GetValue<string>(),
			"a rotation is the same rotation at any unit" );

		//The patch names the component by id and the type is in the prefab rather than here, so all there
		//is to go on is the property's name. That is enough for the ones the table knows by name alone.
		Assert.AreEqual( 2540f, overrides[3]["Value"].GetValue<double>(), 0.0001,
			"a far plane is a far plane whichever component it is on" );

		//And not enough for anything else, which is said out loud rather than guessed at.
		Assert.AreEqual( 250, overrides[4]["Value"].GetValue<int>(),
			"a name the table has never heard of is left alone" );

		Assert.IsTrue( report.Unconverted.Any( u => u.What.Contains( "MoveSpeed" ) ),
			"and reported, so somebody can look at it" );
	}

	//The controller a ported player actually stands on. Its capsule is a size and the number beside it that reads like one is not. The legacy format writes all of
	//these out whether or not they were changed, so every ported character arrives with them - sixty-four metres tall until the table said otherwise.
	[TestMethod]
	public void ACharacterControllerComesAcrossAsACapsuleAndNotAsSixtyFourMetres()
	{
		var scene = JsonNode.Parse( """
			{
			  "GameObjects": [
			    {
			      "Name": "Player",
			      "Components": [
			        {
			          "__type": "Rinten.CharacterController",
			          "Height": 64,
			          "Radius": 16,
			          "StepHeight": 18,
			          "GroundAngle": 45,
			          "Acceleration": 10,
			          "Bounciness": 0.3
			        }
			      ]
			    }
			  ]
			}
			""" );

		var report = new PortReport();
		SceneUnits.Convert( scene, PortUnits.SBox, ForeignBasis.Same, "player.prefab", report );

		var controller = scene["GameObjects"][0]["Components"][0];

		Assert.AreEqual( 1.6256, controller["Height"].GetValue<double>(), 0.0001 );
		Assert.AreEqual( 0.4064, controller["Radius"].GetValue<double>(), 0.0001 );
		Assert.AreEqual( 0.4572, controller["StepHeight"].GetValue<double>(), 0.0001 );

		Assert.AreEqual( 45, controller["GroundAngle"].GetValue<int>(), "an angle" );
		Assert.AreEqual( 0.3, controller["Bounciness"].GetValue<double>(), 0.0001, "a fraction" );

		//Not a distance: it is how fast the current velocity is pulled towards the wanted one, per second.
		Assert.AreEqual( 10, controller["Acceleration"].GetValue<int>() );

		Assert.AreEqual( 0, report.Unconverted.Count, "the table covers this component" );
	}

	//A compound value is converted as a whole, except where the whole is not all one thing. A joint frame is a transform: a position, a rotation
	//and a scale. Taken whole it arrives pointing somewhere else, which does not look like a units problem because nothing is the wrong size.
	[TestMethod]
	public void ATransformConvertsItsPositionAndLeavesItsRotationWhereItIs()
	{
		var scene = JsonNode.Parse( """
			{
			  "GameObjects": [
			    {
			      "Name": "Hinge",
			      "Components": [
			        {
			          "__type": "Rinten.HingeJoint",
			          "LocalFrame1": {
			            "Position": "0,0,32",
			            "Rotation": "0,0,0.7071,0.7071",
			            "Scale": "1,1,1"
			          },
			          "MaxTorque": 100,
			          "BreakForce": 500,
			          "TargetAngle": 90
			        }
			      ]
			    }
			  ]
			}
			""" );

		var report = new PortReport();
		SceneUnits.Convert( scene, PortUnits.SBox, ForeignBasis.Same, "test.scene", report );

		var joint = scene["GameObjects"][0]["Components"][0];
		var frame = joint["LocalFrame1"];

		Assert.AreEqual( "0,0,0.8128", frame["Position"].GetValue<string>(), "the frame sits somewhere" );
		Assert.AreEqual( "0,0,0.7071,0.7071", frame["Rotation"].GetValue<string>(), "and points somewhere" );
		Assert.AreEqual( "1,1,1", frame["Scale"].GetValue<string>() );

		//A torque is a force times the arm it acts on, so it moves twice where the force moves once.
		Assert.AreEqual( 12.7f, joint["BreakForce"].GetValue<double>(), 0.0001 );
		Assert.AreEqual( 0.064516f, joint["MaxTorque"].GetValue<double>(), 0.000001 );

		Assert.AreEqual( 90, joint["TargetAngle"].GetValue<int>(), "an angle is an angle" );
	}

	//A wildcard is right about a name until it meets the component that means something else by it, which is what an exact entry is for.
	[TestMethod]
	public void AnExactEntryBeatsTheWildcardThatWouldHaveBeenWrong()
	{
		var scene = JsonNode.Parse( """
			{
			  "GameObjects": [
			    {
			      "Name": "Post",
			      "Components": [
			        { "__type": "Rinten.AmbientOcclusion", "Radius": 16 },
			        { "__type": "Rinten.Vignette", "Center": "0.5,0.5" },
			        { "__type": "Rinten.SphereCollider", "Radius": 16 }
			      ]
			    }
			  ]
			}
			""" );

		var report = new PortReport();
		SceneUnits.Convert( scene, PortUnits.SBox, ForeignBasis.Same, "test.scene", report );

		var components = scene["GameObjects"][0]["Components"];

		Assert.AreEqual( 16, components[0]["Radius"].GetValue<int>(),
			"occlusion is sampled across the screen, not through the world" );

		Assert.AreEqual( "0.5,0.5", components[1]["Center"].GetValue<string>(),
			"a vignette is centred in texture space" );

		Assert.AreEqual( 0.4064f, components[2]["Radius"].GetValue<double>(), 0.0001,
			"and a collider is still a collider" );
	}
}
