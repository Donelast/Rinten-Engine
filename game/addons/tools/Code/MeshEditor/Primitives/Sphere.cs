using System;

namespace Editor.MeshEditor;

/// <summary>Creates a sphere fitted to the drawn bounds.</summary>
[Title( "Sphere" ), Icon( "sports_baseball" )]
internal class SpherePrimitive : PrimitiveBuilder
{
	[Title( "Number of sides" ), WideMode, Description( "Controls how smooth the sphere is." )]
	public int NumberOfSides { get; set; } = 8;

	[Hide] public Vector3 Center { get; set; }
	[Hide] public Vector3 Size { get; set; }

	public override void SetFromBox( BBox box )
	{
		Center = box.Center;
		Size = box.Size;
	}

	public override void Build( PolygonMesh mesh )
	{
		//Builds the sphere from constant-angle slices: quad rings, with flattened cones at the poles

		var halfSize = Size / 2;

		float angle = 0.0f;
		float angleStep = MathF.PI / NumberOfSides;

		for ( int slice = 0; slice < NumberOfSides; slice++ )
		{
			float angle1 = angle + angleStep;

			//Make the upper polygon.
			var upperPoints = new Vector3[NumberOfSides + 1];
			{
				for ( int i = 0; i < NumberOfSides; i++ )
				{
					var angle2 = i * (MathF.PI * 2.0f / NumberOfSides);
					//The rings lie on the ground plane - X and Z - stacked along Y.
					upperPoints[i] = Center + new Vector3( -MathF.Cos( angle2 ), -1.0f, -MathF.Sin( angle2 ) ) * halfSize * MathF.Sin( angle );
				}

				upperPoints[NumberOfSides] = upperPoints[0];
			}

			//Make the lower polygon.
			var lowerPoints = new Vector3[NumberOfSides + 1];
			{
				for ( int i = 0; i < NumberOfSides; i++ )
				{
					var angle2 = i * (MathF.PI * 2.0f / NumberOfSides);
					lowerPoints[i] = Center + new Vector3( -MathF.Cos( angle2 ), -1.0f, -MathF.Sin( angle2 ) ) * halfSize * MathF.Sin( angle1 );
				}

				lowerPoints[NumberOfSides] = lowerPoints[0];
			}

			float upperHeight = Center.y + halfSize.y * MathF.Cos( angle );
			float lowerHeight = Center.y + halfSize.y * MathF.Cos( angle1 );

			for ( int i = 0; i < NumberOfSides; i++ )
			{
				//Top and bottom are cones, not rings
				if ( slice == 0 )
				{
					mesh.AddFace(
						upperPoints[i + 1].WithY( upperHeight ),
						lowerPoints[i + 1].WithY( lowerHeight ),
						lowerPoints[i].WithY( lowerHeight )
					);
				}
				else if ( slice == NumberOfSides - 1 )
				{
					mesh.AddFace(
						upperPoints[i].WithY( upperHeight ),
						upperPoints[i + 1].WithY( upperHeight ),
						lowerPoints[i + 1].WithY( lowerHeight )
					);
				}
				else
				{
					mesh.AddFace(
						upperPoints[i].WithY( upperHeight ),
						upperPoints[i + 1].WithY( upperHeight ),
						lowerPoints[i + 1].WithY( lowerHeight ),
						lowerPoints[i].WithY( lowerHeight )
					);
				}
			}

			angle += angleStep;
		}
	}
}
