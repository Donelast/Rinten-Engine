using System;

namespace Rinten;

public static partial class Gizmo
{
	public sealed partial class GizmoControls
	{
		/// <summary>Uniform scale handles. Drag delta is in the gizmo's scaled space.</summary>
		public bool Scale( string name, float value, out float outValue )
		{
			using var scaler = PushFixedScale();
			var handleScale = Transform.UniformScale;
			outValue = value;

			using ( Rinten.Gizmo.Scope( name ) )
			{
				float movement = 0.0f;
				Rinten.Gizmo.Draw.IgnoreDepth = true;

				//By the letters - see Control.Position, which says why. Here it was not only a picture: these distances are read back as sizes, so with the x and z
				//arrows pointing down the negative axes, pulling the red or the blue handle away from a thing shrank it and pushing the handle into it made it grow.
				Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Red;
				if ( Arrow( "x", Vector3.Right, out var xdist, head: "box" ) )
				{
					movement += xdist;
				}

				Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Green;
				if ( Arrow( "y", Vector3.Up, out var ydist, head: "box" ) )
				{
					movement += ydist;
				}

				Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Blue;
				if ( Arrow( "z", Vector3.Backward, out var zdist, head: "box" ) )
				{
					movement += zdist;
				}

				if ( movement == 0.0f )
				{
					return false;
				}

				if ( handleScale != 0.0f )
					movement *= 1.0f / handleScale;

				outValue += movement * 0.01f;
				return true;

			}
		}

		/// <summary>Per-axis scale handles. Drag delta is in the gizmo's scaled space.</summary>
		public bool Scale( string name, Vector3 value, out Vector3 outValue, Rotation? axisRotation = null, float squareSize = 3.0f )
		{
			using var scaler = PushFixedScale();
			var handleScale = Transform.UniformScale;
			var axis = axisRotation ?? Rotation.Identity;
			outValue = value;

			using var x = Rinten.Gizmo.Scope( name, Vector3.Zero, axis );

			if ( Settings.GlobalSpace )
				Transform = Transform.WithRotation( Rotation.Identity );

			using ( Rinten.Gizmo.Scope( name ) )
			{
				Vector3 movement = 0.0f;
				Rinten.Gizmo.Draw.IgnoreDepth = true;

				//By the letters - see Control.Position, which says why. Here it was not only a picture: these distances are read back as sizes, so with the x and z
				//arrows pointing down the negative axes, pulling the red or the blue handle away from a thing shrank it and pushing the handle into it made it grow.
				Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Red;
				if ( Arrow( "x", Vector3.Right, out var xdist, head: "box" ) )
				{
					movement += xdist * Vector3.Right;
				}

				Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Green;
				if ( Arrow( "y", Vector3.Up, out var ydist, head: "box" ) )
				{
					movement += ydist * Vector3.Up;
				}

				Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Blue;
				if ( Arrow( "z", Vector3.Backward, out var zdist, head: "box" ) )
				{
					movement += zdist * Vector3.Backward;
				}

				if ( squareSize > 0 )
				{
					float squareOffset = squareSize * 3.0f;
					using var scope = Rinten.Gizmo.Scope( "squares" );
					Hitbox.DepthBias *= 0.8f;

					using ( Rinten.Gizmo.Scope() )
					{
						Rinten.Gizmo.Transform = Rinten.Gizmo.Transform.ToWorld( new Transform( Vector3.Zero ) );
						Rinten.Gizmo.Draw.Color = Color.White;
						if ( DragBox( "center", squareSize, Rotation.Identity, out var moved ) )
						{
							movement += moved;
						}
					}

					using ( Rinten.Gizmo.Scope() )
					{
						Rinten.Gizmo.Transform = Rinten.Gizmo.Transform.ToWorld( new Transform( Vector3.Up * squareOffset + Vector3.Right * squareOffset ) );
						Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Blue;
						if ( DragSquare( "xy", squareSize, Rotation.LookAt( Vector3.Backward, Vector3.Up ), out var moved ) )
						{
							movement += moved;
						}
					}

					using ( Rinten.Gizmo.Scope() )
					{
						Rinten.Gizmo.Transform = Rinten.Gizmo.Transform.ToWorld( new Transform( Vector3.Backward * squareOffset + Vector3.Right * squareOffset ) );
						Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Green;
						if ( DragSquare( "xz", squareSize, Rotation.LookAt( Vector3.Up, Vector3.Backward ), out var moved ) )
						{
							movement += moved;
						}
					}

					using ( Rinten.Gizmo.Scope() )
					{
						Rinten.Gizmo.Transform = Rinten.Gizmo.Transform.ToWorld( new Transform( Vector3.Backward * squareOffset + Vector3.Up * squareOffset ) );
						Rinten.Gizmo.Draw.Color = Rinten.Gizmo.Colors.Red;
						if ( DragSquare( "yz", squareSize, Rotation.LookAt( Vector3.Right, Vector3.Down ), out var moved ) )
						{
							movement += moved;
						}
					}
				}

				if ( movement == 0.0f )
				{
					return false;
				}

				if ( handleScale != 0.0f )
					movement *= 1.0f / handleScale;

				outValue += movement * 0.01f;
				return true;

			}
		}

	}
}
