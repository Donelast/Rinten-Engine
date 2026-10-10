namespace Rinten;

partial class ModelDefinition
{
	public enum MirrorAxis
	{
		X = 0,
		Y = 1,
		Z = 2,
	}

	/// <summary>Compile-time TRS applied to the whole model.</summary>
	public class ModelTransform
	{
		[Property, Hide]
		public bool Enabled { get; set; } = true;

		[Property]
		public Vector3 Position { get; set; }

		[Property]
		public Angles Rotation { get; set; }

		[Property, Title( "Model scale (multiplier)" ), Description( "Model scale (multiplier)" )]
		public Vector3 Scale { get; set; } = Vector3.One;

		public bool IsIdentity => Position.IsNearlyZero( 0.0001f )
			&& Rotation.AsVector3().IsNearlyZero( 0.0001f )
			&& Scale.x.AlmostEqual( 1f ) && Scale.y.AlmostEqual( 1f ) && Scale.z.AlmostEqual( 1f );
	}

	/// <summary>Compile-time mirror of geometry, skeleton, hulls and clips.</summary>
	public class ModelMirror
	{
		[Property, Hide]
		public bool Enabled { get; set; }

		[Property]
		public MirrorAxis Axis { get; set; } = MirrorAxis.X;

		[Property, Description( "Reverse triangle winding after the flip." )]
		public bool FlipWinding { get; set; } = true;

		[Property, Description( "Swap left and right bone names after the flip." )]
		public bool RemapLeftRight { get; set; } = true;
	}
}
