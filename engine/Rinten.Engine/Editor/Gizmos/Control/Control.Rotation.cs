namespace Rinten;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		/// <summary>A full 3d rotation gizmo. If rotated will return true and newValue will be the new rotation.</summary>
		public bool Rotate( string name, Rotation value, out Rotation newValue )
		{
			using var scaler = Gizmo.GizmoControls.PushFixedScale();
			newValue = value;

			bool hasValueChanged = false;
			Rotation delta = Rotation.Identity;

			using ( Rinten.Gizmo.Scope( name ) )
			{
				Rinten.Gizmo.Draw.IgnoreDepth = true;

				using ( Rinten.Gizmo.Scope( "pitch", 0, Rotation.LookAt( Vector3.Left ) ) )
				{
					if ( RotateSingle( "pitch", Rinten.Gizmo.Colors.Pitch, out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Left, angleDelta );
						hasValueChanged = true;
					}
				}

				using ( Rinten.Gizmo.Scope( "yaw", 0, Rotation.LookAt( Vector3.Up ) ) )
				{
					if ( RotateSingle( "yaw", Rinten.Gizmo.Colors.Yaw, out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Up, angleDelta );
						hasValueChanged = true;
					}
				}

				using ( Rinten.Gizmo.Scope( "roll", 0, Rotation.LookAt( Vector3.Forward ) ) )
				{
					if ( RotateSingle( "roll", Rinten.Gizmo.Colors.Roll, out var angleDelta ) )
					{
						delta *= Rotation.FromAxis( Vector3.Forward, angleDelta );
						hasValueChanged = true;
					}
				}

				float _angleDelta;
				bool rotateSingle;

				using ( Rinten.Gizmo.Scope( "view", 0, Gizmo.Transform.Rotation.Inverse * Gizmo.Camera.Rotation ) )
				{
					rotateSingle = RotateSingle( "view", Color.White, out _angleDelta, 22, false );
				}

				using ( Rinten.Gizmo.Scope( "view", 0, Rotation.Identity ) )
				{
					if ( rotateSingle )
					{
						var camForward = Gizmo.Transform.NormalToLocal( Gizmo.CameraTransform.Rotation.Forward );
						delta *= Rotation.FromAxis( camForward, _angleDelta );
						hasValueChanged = true;
					}
				}
			}

			if ( hasValueChanged )
				newValue = value * delta;

			return hasValueChanged;
		}

		[Obsolete( "Use Rotate( string name, out Rotation outValue ) and WorldRotation = outValue rather than *=" )]
		public bool Rotate( string name, out Angles outValue )
		{
			using var scaler = Gizmo.GizmoControls.PushFixedScale();
			outValue = default;

			bool hasValueChanged = false;

			using ( Rinten.Gizmo.Scope( name ) )
			{
				Rinten.Gizmo.Draw.IgnoreDepth = true;

				using ( Rinten.Gizmo.Scope( "pitch", 0, Rotation.LookAt( Vector3.Left ) ) )
				{
					if ( RotateSingle( "pitch", Rinten.Gizmo.Colors.Pitch, out var angleDelta ) )
					{
						outValue += new Angles( angleDelta, 0, 0 );
						hasValueChanged = true;
					}
				}

				using ( Rinten.Gizmo.Scope( "yaw", 0, Rotation.LookAt( Vector3.Up ) ) )
				{
					if ( RotateSingle( "yaw", Rinten.Gizmo.Colors.Yaw, out var angleDelta ) )
					{
						outValue += new Angles( 0, angleDelta, 0 );
						hasValueChanged = true;
					}
				}

				using ( Rinten.Gizmo.Scope( "roll", 0, Rotation.LookAt( Vector3.Forward ) ) )
				{
					if ( RotateSingle( "roll", Rinten.Gizmo.Colors.Roll, out var angleDelta ) )
					{
						outValue += new Angles( 0, 0, angleDelta );
						hasValueChanged = true;
					}
				}
			}

			return hasValueChanged;
		}

		/// <summary>A single rotation axis</summary>
		public bool RotateSingle( string name, Color color, out float angleDelta, float size = 19.0f, bool useHalfCircle = true )
		{
			angleDelta = 0;

			using var x = Rinten.Gizmo.Scope( name );

			Rinten.Gizmo.Draw.LineThickness = 4.5f;
			Rinten.Gizmo.Draw.Color = color;

			if ( !Rinten.Gizmo.IsHovered )
				Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Draw.Color.Darken( 0.33f );

			if ( Pressed.Any && !Pressed.This )
			{
				Rinten.Gizmo.Draw.LineThickness = 3.0f;
				Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Draw.Color.WithAlphaMultiplied( 0.5f );
			}

			using ( Gizmo.Hitbox.LineScope() )
			{
				if ( Pressed.This )
				{
					Rinten.Gizmo.Draw.LineCircle( 0, size, sections: 64 );
				}
				else if ( useHalfCircle )
				{
					Rinten.Gizmo.Draw.ScreenBiasedHalfCircle( 0, size );
				}
				else
				{
					Rinten.Gizmo.Draw.LineCircle( 0, size, sections: 64 );
				}
			}

			if ( !Rinten.Gizmo.IsHovered || !Pressed.This )
				return false;

			var plane = new Plane( 0, Vector3.Forward );


			Rinten.Gizmo.Draw.LineThickness = 3;

			Vector3 pressPoint = Vector3.Zero;
			if ( Camera.Ortho && Camera.Rotation.Forward.Abs() != Transform.Forward.Abs() )
			{
				pressPoint = Pressed.Ray.ToLocal( Gizmo.Transform ).Position;
			}
			else if ( !plane.TryTrace( Pressed.Ray.ToLocal( Gizmo.Transform ), out pressPoint, true ) ) return false;

			Transform localCameera = Gizmo.LocalCameraTransform;

			var pressNormal = pressPoint.Normal;

			//Get a direction adjacent to the circle part we grabbed
			var tangent = Vector3.Cross( pressNormal, Vector3.Forward );

			//Gizmo.Draw.Line( pressNormal * size - tangent * size * 10.0f, (pressNormal * size) + tangent * size * 10.0f );

			Vector3 dragNormal = localCameera.Forward;

			var facingCamera = localCameera.Forward.Dot( Vector3.Forward );

			if ( facingCamera > 0.5f )
				dragNormal = Vector3.Forward;

			var delta = Rinten.Gizmo.GetMouseDistanceVector( pressPoint, dragNormal );
			var angleDifference = delta.Dot( tangent ) * -4.0f;

			//Gizmo.Draw.Plane( pressPoint, dragNormal );

			if ( angleDifference == 0.0f ) return false;

			angleDelta = -angleDifference;
			return true;
		}
	}
}
