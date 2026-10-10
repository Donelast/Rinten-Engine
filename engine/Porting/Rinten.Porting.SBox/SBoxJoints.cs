using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Rinten.Porting.SBox;

//The joints a model document names, as this engine writes them.
internal static class SBoxJoints
{
	public static List<ModelDefinition.Joint> Read( string file, ForeignBasis bones )
	{
		var joints = new List<ModelDefinition.Joint>();
		var document = Load( file );
		if ( document is null ) return joints;

		//Same unit as SBoxHulls uses for this document, so joints line up with the bodies
		var unit = SBoxModelModifiers.Unit( SBoxModelModifiers.Read( document ) );

		//Walk the prefabs too: characters keep their ragdoll in a separate prefab
		foreach ( var node in SBoxModelDoc.Nodes( document, file ) )
		{
			var kind = node.String( "_class" ) ?? "";
			var type = kind switch
			{
				"PhysicsJointConical" => ModelDefinition.JointKind.Ball,
				"PhysicsJointHinge" or "PhysicsJointRevolute" => ModelDefinition.JointKind.Hinge,
				"PhysicsJointSlider" or "PhysicsJointPrismatic" => ModelDefinition.JointKind.Slider,
				"PhysicsJointWeld" or "PhysicsJointFixed" => ModelDefinition.JointKind.Fixed,
				_ => (ModelDefinition.JointKind?)null
			};

			if ( type is null ) continue;

			var (px, py, pz) = Triple( node["anchor_origin"] );
			var (rx, ry, rz) = Triple( node["anchor_angles"] );
			var (sx, sy, sz) = Triple( node["swing_offset_angle"] );

			joints.Add( new ModelDefinition.Joint
			{
				Name = node.String( "name" ) ?? "",
				Type = type.Value,
				BoneA = node.String( "parent_body" ) ?? "",
				BoneB = node.String( "child_body" ) ?? "",
				//Anchors are in the parent body's bone space, not model space
				Position = bones.Vector( new Vector3( px, py, pz ) ) * unit,
				Rotation = new Angles( rx, ry, rz ),
				ModelSpace = false,
				EnableCollision = node.Bool( "collision_enabled" ),
				EnableSwingLimit = node.Bool( "enable_swing_limit", true ),
				SwingLimit = node.Float( "swing_limit", 30.0f ),
				EnableTwistLimit = node.Bool( "enable_twist_limit", true ),
				MinTwist = node.Float( "min_twist_angle", -30.0f ),
				MaxTwist = node.Float( "max_twist_angle", 30.0f ),
				MinAngle = node.Float( "min_angle", node.Float( "min_hinge_angle", -90.0f ) ),
				MaxAngle = node.Float( "max_angle", node.Float( "max_hinge_angle", 90.0f ) ),
				MinLength = node.Float( "min_length" ) * unit,
				MaxLength = node.Float( "max_length", 8.0f ) * unit,

				//What the joint does between its limits, which their documents carry and this used to
				//drop: a ragdoll with none of it swings freely until every limb is against a stop.
				SwingOffset = new Angles( sx, sy, sz ),
				Friction = node.Float( "friction" ),
				LinearStrength = node.Float( "linear_strength" ),
				AngularStrength = node.Float( "angular_strength" ),
			} );
		}

		return joints;
	}

	private static (float x, float y, float z) Triple( CoreImportedKeyValues3 node )
	{
		if ( node is null ) return default;

		if ( node.IsArray )
		{
			var items = node.Items;
			return (
				items.Count > 0 ? items[0].AsFloat() : 0.0f,
				items.Count > 1 ? items[1].AsFloat() : 0.0f,
				items.Count > 2 ? items[2].AsFloat() : 0.0f );
		}

		return (node.Float( "x" ), node.Float( "y" ), node.Float( "z" ));
	}

	private static IEnumerable<CoreImportedKeyValues3> Nodes( CoreImportedKeyValues3 node )
	{
		if ( node is null ) yield break;

		if ( node.IsArray )
		{
			foreach ( var item in node.Items )
			{
				foreach ( var found in Nodes( item ) )
					yield return found;
			}

			yield break;
		}

		if ( !node.IsObject ) yield break;

		if ( node["_class"] is not null ) yield return node;

		foreach ( var key in node.Keys )
		{
			var child = node[key];
			if ( child is null || key == "_class" ) continue;
			if ( !child.IsObject && !child.IsArray ) continue;

			foreach ( var found in Nodes( child ) )
				yield return found;
		}
	}

	private static CoreImportedKeyValues3 Load( string file )
	{
		try
		{
			//The text their editor writes, and only that. A document read out of the DATA block of the file
			//beside it is the same document after their compiler has been over it - see PortPaths.IsCompiled.
			return CoreImportedKeyValues3.ReadText( File.ReadAllText( file ) );
		}
		catch ( Exception e ) when ( e is IOException or ArgumentException )
		{
			return null;
		}
	}
}
