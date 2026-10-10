using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnitTests.Core;

//How a constraint gets out of a .mdl and back into one. The axes were a number before they were a name, and every model ported so
//far has numbers written in it - so a definition being editable now must not mean the ones already on disk read as something else.
[TestClass]
public class ModelDefinitionConstraintTests
{
	static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		ReadCommentHandling = JsonCommentHandling.Skip,
		AllowTrailingCommas = true,
		IncludeFields = true,
		Converters = { new JsonStringEnumConverter() },
	};

	static ModelDefinition.Constraint Read( string json )
	{
		var definition = JsonSerializer.Deserialize<ModelDefinition>( json, Options );
		Assert.IsNotNull( definition );
		Assert.AreEqual( 1, definition.Constraints.Count );
		return definition.Constraints[0];
	}

	[TestMethod]
	public void APortedConstraintReadsItsAxesAsNumbers()
	{
		var constraint = Read( """
		{
			"Constraints": [{
				"Kind": "TiltTwist",
				"Bone": "arm_lower_twist_R",
				"Weight": 0.5,
				"DriverAxis": 1,
				"BoneAxis": 2,
				"Drivers": [{ "Bone": "hand_R", "Weight": 1 }]
			}]
		}
		""" );

		Assert.AreEqual( ModelDefinition.ConstraintKind.TiltTwist, constraint.Kind );
		Assert.AreEqual( ModelDefinition.ConstraintAxis.Y, constraint.DriverAxis );
		Assert.AreEqual( ModelDefinition.ConstraintAxis.Z, constraint.BoneAxis );
		Assert.AreEqual( 1, constraint.Drivers.Count );
		Assert.AreEqual( "hand_R", constraint.Drivers[0].Bone );
	}

	//Written before there was a switch, so it is on.
	[TestMethod]
	public void AConstraintWithNoSwitchInTheFileIsOn()
	{
		Assert.IsTrue( Read( """{ "Constraints": [{ "Bone": "arm_lower_twist_R" }] }""" ).Enabled );
	}

	[TestMethod]
	public void ASwitchedOffConstraintStaysOff()
	{
		Assert.IsFalse( Read( """{ "Constraints": [{ "Bone": "b", "Enabled": false }] }""" ).Enabled );
	}

	//The drivers are off the property sheet, which must not mean off the file.
	[TestMethod]
	public void EveryConstraintFieldSurvivesTheFile()
	{
		var definition = new ModelDefinition();

		definition.Constraints.Add( new ModelDefinition.Constraint
		{
			Kind = ModelDefinition.ConstraintKind.Aim,
			Enabled = false,
			Bone = "eye_R",
			Weight = 0.25f,
			BoneAxis = ModelDefinition.ConstraintAxis.Z,
			DriverAxis = ModelDefinition.ConstraintAxis.Y,
			Folder = "face",
			Drivers =
			{
				new ModelDefinition.Constraint.Driver
				{
					Bone = "head",
					Position = new Vector3( 1, 2, 3 ),
					Rotation = new Angles( 10, 20, 30 ),
					Weight = 0.75f,
				},
			},
		} );

		var read = Read( JsonSerializer.Serialize( definition, Options ) );

		Assert.AreEqual( ModelDefinition.ConstraintKind.Aim, read.Kind );
		Assert.IsFalse( read.Enabled );
		Assert.AreEqual( "eye_R", read.Bone );
		Assert.AreEqual( 0.25f, read.Weight, 0.0001f );
		Assert.AreEqual( ModelDefinition.ConstraintAxis.Z, read.BoneAxis );
		Assert.AreEqual( ModelDefinition.ConstraintAxis.Y, read.DriverAxis );
		Assert.AreEqual( "face", read.Folder );

		Assert.AreEqual( 1, read.Drivers.Count );
		Assert.AreEqual( "head", read.Drivers[0].Bone );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), read.Drivers[0].Position );
		Assert.AreEqual( 0.75f, read.Drivers[0].Weight, 0.0001f );
	}
}
