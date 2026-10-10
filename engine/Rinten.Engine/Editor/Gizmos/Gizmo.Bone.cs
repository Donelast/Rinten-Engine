namespace Rinten;

public static partial class Gizmo
{
	/// <summary>How wide a bone's joint is drawn, in metres.</summary>
	public const float BoneJointSize = 0.01f;

	/// <summary>Draws a bone at the scope origin: line to its parent and a joint square; picking is the caller's</summary>
	public static void DrawBone( Vector3 toParent, bool selected, bool hovered, float size = BoneJointSize )
	{
		Draw.IgnoreDepth = true;
		Draw.LineThickness = 1;

		if ( !toParent.IsNearZeroLength )
		{
			Draw.Color = Color.White.WithAlpha( 0.2f );
			Draw.Line( 0, toParent );
		}

		Draw.Color = selected ? Colors.Active : hovered ? Color.White : Color.White.WithAlpha( 0.2f );
		Draw.Sprite( 0, size, Texture.White );
	}
}
