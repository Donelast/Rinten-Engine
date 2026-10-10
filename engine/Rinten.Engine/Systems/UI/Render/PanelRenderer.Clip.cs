using Rinten.Engine;
using Rinten.Rendering;

namespace Rinten.UI;

internal partial class PanelRenderer
{
	//Software scissor, panels outside of this should not be rendered
	internal Rect Scissor;

	//Accumulated clip rect from OverflowMode.ClipWhole ancestors. Any panel whose bounds extend
	//outside this rect will be skipped entirely. Null when no clip-whole ancestor is active.
	internal Rect? ClipWholeRect;

	internal bool IsOutsideClipWholeRect( Rect r )
	{
		if ( !ClipWholeRect.HasValue ) return false;
		var clip = ClipWholeRect.Value;
		return r.Left < clip.Left || r.Top < clip.Top || r.Right > clip.Right || r.Bottom > clip.Bottom;
	}

	//Accumulated clip rect from OverflowMode.Scroll and OverflowMode.Hidden ancestors. Any panel whose bounds lie completely outside this rect will
	//be skipped entirely (early cull). Uses an overlap test so partially-visible panels still render. Null when no scroll/hidden ancestor is active.
	internal Rect? ScrollCullRect;

	internal bool IsOutsideScrollCullRect( Rect r )
	{
		if ( !ScrollCullRect.HasValue ) return false;
		var clip = ScrollCullRect.Value;
		return r.Right <= clip.Left || r.Bottom <= clip.Top || r.Left >= clip.Right || r.Top >= clip.Bottom;
	}

	//Scissor passed to gpu shader to be transformed
	internal GPUScissor ScissorGPU;
	internal struct GPUScissor
	{
		public Rect Rect;
		public Vector4 CornerRadius;
		public Matrix Matrix;
		public bool Invert;
	}

	//Scope that updates the renderer's scissor state for child panels to inherit. Does
	//NOT modify any command lists - those are set up separately in BuildCommandList.
	internal ref struct ClipScope
	{
		Rect Previous;
		GPUScissor PreviousGPU;
		bool _disposed = false; //Whether this scope actually set a new scissor or not. If the panel had overflow: visible then we won't set a new scissor, so we can skip restoring the old one.

		public ClipScope( Rect scissorRect, Vector4 cornerRadius, Matrix globalMatrix )
		{
			_disposed = true;

			var renderer = GlobalContext.Current.UISystem.Renderer;

			Previous = renderer.Scissor;
			PreviousGPU = renderer.ScissorGPU;

			renderer.ScissorGPU.Rect = new Rect()
			{
				Left = Math.Max( scissorRect.Left, PreviousGPU.Rect.Left ),
				Top = Math.Max( scissorRect.Top, PreviousGPU.Rect.Top ),
				Right = Math.Min( scissorRect.Right, PreviousGPU.Rect.Right ),
				Bottom = Math.Min( scissorRect.Bottom, PreviousGPU.Rect.Bottom ),
			};

			renderer.ScissorGPU.CornerRadius = cornerRadius;
			renderer.ScissorGPU.Matrix = globalMatrix;

			var tl = globalMatrix.Transform( scissorRect.TopLeft );
			var tr = globalMatrix.Transform( scissorRect.TopRight );
			var bl = globalMatrix.Transform( scissorRect.BottomLeft );
			var br = globalMatrix.Transform( scissorRect.BottomRight );

			var min = Vector2.Min( Vector2.Min( tl, tr ), Vector2.Min( bl, br ) );
			var max = Vector2.Max( Vector2.Max( tl, tr ), Vector2.Max( bl, br ) );

			scissorRect = new Rect( min, max - min );

			renderer.Scissor = new Rect()
			{
				Left = Math.Max( scissorRect.Left, Previous.Left ),
				Top = Math.Max( scissorRect.Top, Previous.Top ),
				Right = Math.Min( scissorRect.Right, Previous.Right ),
				Bottom = Math.Min( scissorRect.Bottom, Previous.Bottom ),
			};
		}

		public void Dispose()
		{
			if ( !_disposed ) return;
			_disposed = false;

			var renderer = GlobalContext.Current.UISystem.Renderer;
			renderer.Scissor = Previous;
			renderer.ScissorGPU = PreviousGPU;
		}
	}

	/// <summary>Create a clip scope for a panel's children. This updates the renderer's scissor state so child panels will inherit the correct scissor when their command lists are built.</summary>
	public ClipScope Clip( Panel panel )
	{
		var overflow = panel.ComputedStyle?.Overflow ?? OverflowMode.Visible;
		if ( overflow == OverflowMode.Visible || overflow == OverflowMode.ClipWhole ) return default;

		var size = (panel.Box.Rect.Width + panel.Box.Rect.Height) * 0.5f;
		var borderRadius = new Vector4( panel.ComputedStyle.BorderTopLeftRadius?.GetPixels( size ) ?? 0, panel.ComputedStyle.BorderTopRightRadius?.GetPixels( size ) ?? 0, panel.ComputedStyle.BorderBottomLeftRadius?.GetPixels( size ) ?? 0, panel.ComputedStyle.BorderBottomRightRadius?.GetPixels( size ) ?? 0 );

		return new ClipScope( panel.Box.ClipRect, borderRadius, panel.GlobalMatrix ?? Matrix.Identity );
	}

	internal static void SetScissorAttributes( CommandList commandList, GPUScissor scissor )
	{
		if ( scissor.Rect.Width == 0 && scissor.Rect.Height == 0 )
		{
			commandList.Attributes.Set( "HasScissor", 0 );
			return;
		}

		commandList.Attributes.Set( "ScissorRect", scissor.Rect.ToVector4() );
		commandList.Attributes.Set( "ScissorCornerRadius", scissor.CornerRadius );
		commandList.Attributes.Set( "ScissorTransformMat", scissor.Matrix );
		commandList.Attributes.Set( "HasScissor", 1 );
	}

	void InitScissor( Rect rect )
	{
		Scissor = rect;
		ScissorGPU = new() { Rect = rect, Matrix = Matrix.Identity };
	}

	void InitScissor( Rect rect, CommandList commandList )
	{
		InitScissor( rect );
		SetScissorAttributes( commandList, ScissorGPU );
	}

}
