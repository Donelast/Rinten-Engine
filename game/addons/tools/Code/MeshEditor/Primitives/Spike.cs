using System;

namespace Editor.MeshEditor;

/// <summary>Creates a pointed cone fitted to the drawn bounds.</summary>
[Title( "Spike" ), Icon( "details" )]
internal class SpikePrimitive : PrimitiveBuilder
{
	[Title( "Number of sides" ), WideMode, Description( "Controls how round the base of the spike is." )]
	public int NumberOfSides { get; set; } = 16;

	[Hide] public Vector3 Center { get; set; }
	[Hide] public Vector3 Size { get; set; }

	public override void SetFromBox( BBox box )
	{
		Center = box.Center;
		Size = box.Size;
	}

	public override void Build( PolygonMesh mesh )
	{
		var points = new Vector3[NumberOfSides];
		var halfSize = Size / 2;

		for ( int i = 0; i < NumberOfSides; i++ )
		{
			var angle = i * (MathF.PI * 2.0f / NumberOfSides);

			//The base lies on the ground plane - X and Z - and the spike grows along Y, the same way the cylinder beside it is built.
			points[i] = Center + new Vector3( -MathF.Cos( angle ), -1.0f, -MathF.Sin( angle ) ) * halfSize;
		}

		mesh.AddFace( points.Select( x => x.WithY( Center.y - halfSize.y ) ).ToArray() ); //bottom face

		var topCenter = new Vector3( Center.x, Center.y + halfSize.y, Center.z );

		//sides
		for ( int i = 0; i < NumberOfSides; i++ )
		{
			var nextIndex = (i + 1) % NumberOfSides;
			mesh.AddFace( topCenter, points[nextIndex], points[i] );
		}
	}
}
