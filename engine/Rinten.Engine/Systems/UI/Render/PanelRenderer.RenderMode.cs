namespace Rinten.UI
{
	internal partial class PanelRenderer
	{
		string RenderMode;
		Stack<string> RenderModeStack = new Stack<string>();
		internal BlendMode OverrideBlendMode = BlendMode.Normal;

		private void PopRenderMode()
		{
			RenderModeStack.Pop();
			SetRenderMode( RenderModeStack.Peek() );
		}

		private bool PushRenderMode( Panel panel )
		{
			var style = panel.ComputedStyle;
			if ( style.MixBlendMode == null ) return false;

			//IF THE MODE IS THE SAME, DON'T DO SHIT
			if ( RenderMode == style.MixBlendMode )
				return false;

			//PUSH CURRENT ONTO STACK, SWITCH TO NEXT
			RenderModeStack.Push( RenderMode );
			SetRenderMode( style.MixBlendMode );

			return true;
		}

		BlendMode ParseBlendMode( string blendModeStr )
		{
			var blendMode = blendModeStr switch
			{
				"lighten" => BlendMode.Lighten,
				"multiply" => BlendMode.Multiply,
				_ => BlendMode.Normal,
			};

			return blendMode;
		}

		void SetRenderMode( string renderMode )
		{
			RenderMode = renderMode;
			OverrideBlendMode = ParseBlendMode( renderMode );
		}
	}
}
