using Rinten.Rendering;

namespace Rinten;

public sealed partial class CameraComponent : Component, Component.ExecuteInEditor
{
	internal PostProcessLayers PostProcess { get; private set; } = new PostProcessLayers();

	//Off means no effects and no tonemapping curve either
	/// <summary>Enable or disable post processing for this camera.</summary>
	[Header( "Post Processing" )]
	[Property]
	public bool EnablePostProcessing { get; set; } = true;

	/// <summary>If set then we'll trigger post process volumes from this position, instead of the camera's position.</summary>
	[Property]
	public GameObject PostProcessAnchor { get; set; }

	//Whether the picture is finished by the default curve with post processing off - for a probe's capture, which takes that curve back off the
	//colours it reads to recover the light: see CoreEnvmap.Untonemapped. Without a curve everything brighter than white would come back as white.
	internal bool KeepsCurveWithoutPost { get; set; }


	internal void PrintPostProcessDebugOverlay( ref Vector2 pos, HudPainter hud )
	{
		var text = $"";

		foreach ( var group in PostProcess.Layers.OrderBy( x => x.Key ) )
		{
			//The lists stay with their stages between frames, emptied.
			if ( group.Value.Count == 0 ) continue;

			text += $"{group.Key}\n";

			foreach ( var layer in group.Value.OrderBy( x => x.Order ) )
			{
				text += $"\t{layer.Order,6:D}: {layer.Name}\n";
			}
		}

		var textScope = TextRendering.Scope.Default;
		textScope.Text = text;
		textScope.FontSize = 11;
		textScope.FontName = "Roboto Mono";
		textScope.FontWeight = 600;
		textScope.Outline = new TextRendering.Outline { Enabled = true, Color = Color.Black.WithAlpha( 0.9f ), Size = 3 };
		textScope.Shadow = new TextRendering.Shadow { Enabled = true, Color = Color.Black.WithAlpha( 0.5f ), Offset = 0, Size = 2 };

		var drawnRect = hud.DrawText( textScope, pos, flags: TextFlag.LeftTop );
		pos.y = drawnRect.Bottom;
	}
}
