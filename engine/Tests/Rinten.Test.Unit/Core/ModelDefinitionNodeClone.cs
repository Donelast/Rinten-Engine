using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnitTests.Core;

//Nodes clone as their list's base type so hull discriminators survive the JSON round trip
[TestClass]
public class ModelDefinitionNodeCloneTests
{
	//The options ModelDocClipboard uses.
	static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true,
		IncludeFields = true,
		WriteIndented = false,
		Converters = { new JsonStringEnumConverter() },
	};

	static T Clone<T>( T value ) where T : class
		=> JsonSerializer.Deserialize( JsonSerializer.Serialize( value, typeof( T ), Options ), typeof( T ), Options ) as T;

	[TestMethod]
	public void ADuplicatedHullKeepsWhichKindOfHullItWas()
	{
		ModelDefinition.Hull box = new ModelDefinition.BoxHull
		{
			Name = "pelvis",
			Bone = "spine_0",
			Center = new Vector3( 1, 2, 3 ),
			Extents = new Vector3( 4, 5, 6 ),
		};

		var copy = Clone( box );

		var copied = copy as ModelDefinition.BoxHull;
		Assert.IsNotNull( copied, $"a duplicated box hull came back as {copy?.GetType().Name ?? "nothing"}" );

		Assert.AreEqual( "pelvis", copied.Name );
		Assert.AreEqual( "spine_0", copied.Bone );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), copied.Center );
		Assert.AreEqual( new Vector3( 4, 5, 6 ), copied.Extents );
	}

	[TestMethod]
	public void EveryKindOfHullSurvivesBeingDuplicated()
	{
		ModelDefinition.Hull[] hulls =
		[
			new ModelDefinition.SphereHull { Radius = 3f },
			new ModelDefinition.CapsuleHull { Radius = 2f },
			new ModelDefinition.BoxHull(),
			new ModelDefinition.RenderHull(),
			new ModelDefinition.RenderHull { File = "a.fbx" },
			new ModelDefinition.RenderMesh(),
		];

		foreach ( var hull in hulls )
		{
			var copy = Clone( hull );
			Assert.IsNotNull( copy, $"{hull.GetType().Name} did not survive at all" );
			Assert.AreEqual( hull.GetType(), copy.GetType(),
				$"{hull.GetType().Name} came back as {copy.GetType().Name}" );
		}
	}

	//The point of cloning rather than adding the same object twice: two nodes that share a list are two nodes that quietly edit each other.
	[TestMethod]
	public void ADuplicateDoesNotShareItsListsWithTheOriginal()
	{
		var group = new ModelDefinition.MaterialSet { Name = "red" };
		group.Materials.Add( "materials/red.mat" );

		var copy = Clone( group );
		Assert.IsNotNull( copy );

		copy.Materials.Add( "materials/blue.mat" );

		Assert.AreEqual( 1, group.Materials.Count,
			"editing the duplicate's materials also edited the original's" );
	}

	[TestMethod]
	public void ADuplicatedJointKeepsEveryFieldOfItsCone()
	{
		var joint = new ModelDefinition.Joint
		{
			Name = "shoulder_L",
			Type = ModelDefinition.JointKind.Ball,
			BoneA = "spine",
			BoneB = "upperarm_L",
			SwingLimit = 45f,
			SwingOffset = new Angles( 0, -90, -90 ),
			MinTwist = -12f,
			MaxTwist = 34f,
			Friction = 4f,
		};

		var copy = Clone( joint );
		Assert.IsNotNull( copy );

		Assert.AreEqual( "shoulder_L", copy.Name );
		Assert.AreEqual( ModelDefinition.JointKind.Ball, copy.Type );
		Assert.AreEqual( 45f, copy.SwingLimit, 0.001f );
		Assert.AreEqual( new Angles( 0, -90, -90 ), copy.SwingOffset );
		Assert.AreEqual( -12f, copy.MinTwist, 0.001f );
		Assert.AreEqual( 34f, copy.MaxTwist, 0.001f );
		Assert.AreEqual( 4f, copy.Friction, 0.001f );
	}
}
