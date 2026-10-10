using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnitTests.Core;

//Joints deserialize from .mdl JSON: every field reaches the solver and enums parse in any form
[TestClass]
public class ModelDefinitionJointTests
{
	//The same options AddJoints reads a definition with.
	static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		IncludeFields = true,
		Converters = { new JsonStringEnumConverter() },
	};

	static ModelDefinition.Joint Read( string json )
	{
		var definition = JsonSerializer.Deserialize<ModelDefinition>( json, Options );
		Assert.IsNotNull( definition );
		Assert.AreEqual( 1, definition.Joints.Count );
		return definition.Joints[0];
	}

	[TestMethod]
	public void EveryJointFieldSurvivesTheFile()
	{
		var joint = Read( """
		{
			"Joints": [{
				"Name": "shoulder_L",
				"Type": "Ball",
				"BoneA": "spine",
				"BoneB": "upperarm_L",
				"Position": "1,2,3",
				"Rotation": "10,20,30",
				"EnableCollision": true,
				"EnableSwingLimit": true,
				"SwingLimit": 45,
				"SwingOffset": "0,-90,-90",
				"EnableTwistLimit": true,
				"MinTwist": -12,
				"MaxTwist": 34,
				"Friction": 4,
				"LinearStrength": 250,
				"AngularStrength": 125,
				"Stiffness": 8,
				"Damping": 0.75
			}]
		}
		""" );

		Assert.AreEqual( "shoulder_L", joint.Name );
		Assert.AreEqual( ModelDefinition.JointKind.Ball, joint.Type );
		Assert.AreEqual( "spine", joint.BoneA );
		Assert.AreEqual( "upperarm_L", joint.BoneB );
		Assert.IsTrue( joint.EnableCollision );

		Assert.AreEqual( 45f, joint.SwingLimit, 0.001f );

		//The cone is aimed apart from the anchor frame - this is the field that lets a
		//shoulder's cone point down and out while twist stays measured about something sensible.
		Assert.AreEqual( -90f, joint.SwingOffset.yaw, 0.001f );
		Assert.AreEqual( -90f, joint.SwingOffset.roll, 0.001f );

		Assert.AreEqual( -12f, joint.MinTwist, 0.001f );
		Assert.AreEqual( 34f, joint.MaxTwist, 0.001f );

		Assert.AreEqual( 4f, joint.Friction, 0.001f );
		Assert.AreEqual( 250f, joint.LinearStrength, 0.001f );
		Assert.AreEqual( 125f, joint.AngularStrength, 0.001f );
		Assert.AreEqual( 8f, joint.Stiffness, 0.001f );
		Assert.AreEqual( 0.75f, joint.Damping, 0.001f );
	}

	//An enum written by hand is a name; one written by the serialiser is the number behind it. Taking only the name is what once turned every
	//joint of a ragdoll into the first kind there is - fifteen hinges, shoulders and hips among them, fighting each other instead of hanging.
	[TestMethod]
	public void AJointKindReadsAsANameOrAsANumber()
	{
		Assert.AreEqual( ModelDefinition.JointKind.Ball,
			Read( """{ "Joints": [{ "Type": "Ball" }] }""" ).Type );

		Assert.AreEqual( ModelDefinition.JointKind.Ball,
			Read( $$"""{ "Joints": [{ "Type": {{(int)ModelDefinition.JointKind.Ball}} }] }""" ).Type );

		Assert.AreEqual( ModelDefinition.JointKind.Slider,
			Read( $$"""{ "Joints": [{ "Type": {{(int)ModelDefinition.JointKind.Slider}} }] }""" ).Type );
	}

	//A file that says nothing about aiming or friction still reads, and describes the joint it always did: cone on the frame, nothing resisting.
	[TestMethod]
	public void AnOlderFileWithoutTheNewFieldsStillReads()
	{
		var joint = Read( """{ "Joints": [{ "Type": "Ball", "SwingLimit": 20 }] }""" );

		Assert.AreEqual( 20f, joint.SwingLimit, 0.001f );
		Assert.AreEqual( Angles.Zero, joint.SwingOffset );
		Assert.AreEqual( 0f, joint.Friction, 0.001f );
		Assert.AreEqual( 0f, joint.LinearStrength, 0.001f );
		Assert.AreEqual( 0f, joint.Stiffness, 0.001f );
	}
}
