namespace Rinten;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		/// <summary>A scalable sphere gizmo. Returns true if the gizmo was interacted with and outValue will return the new radius.</summary>
		public bool Sphere( string name, float radius, out float outRadius, Color color )
		{
			outRadius = radius;
			var localCameraRot = Transform.RotationToLocal( Camera.Rotation );

			Rinten.Gizmo.Draw.IgnoreDepth = true;
			using ( var scope = Rinten.Gizmo.Scope( name, new Transform( 0, localCameraRot ) ) )
			{
				float movement = 0.0f;

				Rinten.Gizmo.Draw.LineThickness = 4.0f;
				Rinten.Gizmo.Draw.Color = color;

				if ( !Rinten.Gizmo.IsHovered )
					Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Draw.Color.Darken( 0.33f );

				if ( Pressed.Any && !Pressed.This )
					return false;

				using ( Hitbox.LineScope() )
				{
					var delta = Rinten.Gizmo.GetMouseDistanceDelta( 0, Rotation.FromAxis( localCameraRot.Forward, 0 ).Forward );

					if ( Pressed.This )
					{
						movement += delta;
					}

					//Fixed segment count; deriving it from the radius in metres gave zero segments for small spheres
					Gizmo.Draw.LineCircle( 0, radius, sections: 32 );
				}

				if ( movement != 0.0f )
				{
					outRadius = radius + movement;
					return true;
				}

				return false;
			}
		}
	}
}
