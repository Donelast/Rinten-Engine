using Rinten.Rendering;

namespace Rinten.UI;

public partial class Panel
{
	string PanelLayerRTName => field ??= $"PanelLayer.{GetHashCode()}";

	Vector2? _panelLayerSize;

	//Where the offscreen layer sits: the margin box, grown to hold the panel's outset
	//box shadows - drawn into the layer, they were clipped to the margin box before.
	Rect _panelLayerBounds;

	//Cached layer state computed during Build for use during Gather
	internal Matrix? CachedLayerMatrix;

	bool NeedsLayer( Styles styles )
	{
		if ( HasFilter ) return true;
		if ( styles.FilterDropShadow.Count > 0 ) return true;
		if ( styles.MaskImage != null ) return true;
		if ( styles.Isolation == UI.Isolation.Isolate ) return true;

		return false;
	}

	internal void UpdateLayer( Styles styles )
	{
		if ( NeedsLayer( styles ) )
		{
			_panelLayerBounds = CalculateLayerBounds( styles );
			var size = _panelLayerBounds.Size;

			if ( size.x <= 1 ) return;
			if ( size.y <= 1 ) return;

			_panelLayerSize = size;
		}
		else
		{
			_panelLayerSize = null;
		}
	}

	//Fits the margin box and outset shadows inside an integer-sized offscreen target.
	Rect CalculateLayerBounds( Styles styles )
	{
		var bounds = Box.RectOuter;

		if ( styles.BoxShadow is { Count: > 0 } shadows )
		{
			for ( int i = 0; i < shadows.Count; i++ )
			{
				var shadow = shadows[i];
				if ( shadow.Inset || shadow.Color.a <= 0 ) continue;

				//Match the renderer's shadow quad (GPUBoxInstance.FromShadow): border box + offset + spread + blur.
				var shape = (Box.Rect + new Vector2( shadow.OffsetX, shadow.OffsetY )).Grow( shadow.Spread );
				bounds.Add( shape.Grow( MathF.Ceiling( shadow.Blur ) ) );
			}
		}

		//Round outward to integer target pixels without clipping fractional shadows.
		bounds.Left = MathF.Floor( bounds.Left );
		bounds.Top = MathF.Floor( bounds.Top );
		bounds.Right = MathF.Ceiling( bounds.Right );
		bounds.Bottom = MathF.Ceiling( bounds.Bottom );
		return bounds;
	}

	//Called before rendering this panel
	internal void PushLayer( PanelRenderer render )
	{
		if ( _panelLayerSize is null ) return;
		if ( ComputedStyle is null ) return;
		if ( !IsVisible ) return;

		var mat = render.Matrix.Inverted;
		mat *= Matrix.CreateTranslation( _panelLayerBounds.Position * -1.0f );

		//Store state for gather phase
		CachedLayerMatrix = mat;
		_lastLayerMatrix = GlobalMatrix;
		GlobalMatrix = null;
	}

	//Called during Gather phase — push RT on the global command list.
	internal void PushLayerGather( PanelRenderer render, CommandList globalCL )
	{
		if ( _panelLayerSize is null ) return;
		if ( CachedLayerMatrix is null ) return;

		//Set identity transform for layer content
		globalCL.Attributes.Set( "TransformMat", Matrix.Identity );

		var handle = globalCL.GetRenderTarget( PanelLayerRTName, (int)_panelLayerSize.Value.x, (int)_panelLayerSize.Value.y, depthFormat: ImageFormat.None );

		render.PushLayer( this, globalCL, handle, CachedLayerMatrix.Value );
	}

	//Build commands for post-children layer drawing (filters, masks, etc.) Called during Build phase after
	//children are processed. LayerStack is maintained during Build so PopLayer can set correct state.
	internal void BuildLayerPopCommands( PanelRenderer render, RenderTarget defaultRenderTarget )
	{
		//Restore layer transform
		if ( _lastLayerMatrix.HasValue )
		{
			GlobalMatrix = _lastLayerMatrix;
			_lastLayerMatrix = null;
		}

		if ( _panelLayerSize is null ) return;
		if ( ComputedStyle is null ) return;
		if ( !IsVisible ) return;

		LayerCommandList.Reset();

		//Find parent layer's matrix by walking up the tree. If no parent layer, use Identity. RT is set during Draw via
		//PopLayer on the global CL — don't set it here, or it would override the parent layer's RT for nested layers.
		var parentLayerMat = Matrix.Identity;
		var isWorld = false;
		var ancestor = Parent;
		while ( ancestor != null )
		{
			if ( ancestor.HasPanelLayer && ancestor.CachedLayerMatrix.HasValue )
			{
				parentLayerMat = ancestor.CachedLayerMatrix.Value;
				break;
			}
			ancestor = ancestor.Parent;
		}
		isWorld = render.IsWorldPanel( this );

		LayerCommandList.Attributes.Set( "LayerMat", parentLayerMat );
		LayerCommandList.Attributes.SetCombo( "D_WORLDPANEL", isWorld ? 1 : 0 );

		//Apply transform/scissor from cached descriptor state
		LayerCommandList.Attributes.Set( "TransformMat", CachedDescriptors.TransformMat );
		PanelRenderer.SetScissorAttributes( LayerCommandList, CachedDescriptors.Scissor );

		BuildLayerPopCommandsInto( render, LayerCommandList );
	}

	private void BuildLayerPopCommandsInto( PanelRenderer render, CommandList commandList )
	{
		var attributes = commandList.Attributes;

		//Shared attributes
		attributes.Set( "Texture", new RenderTargetHandle { Name = PanelLayerRTName }.ColorTexture );
		attributes.Set( "BoxPosition", _panelLayerBounds.Position );
		attributes.Set( "BoxSize", _panelLayerBounds.Size );

		//Pre-filter: draw shadows and border before everything else as separate layers
		DrawPreFilterShadows( commandList );
		DrawPreFilterBorder( commandList );
		ResetPrefilterAttributes( commandList );

		float blurSize = ComputedStyle.FilterBlur.Value.GetPixels( 1.0f );

		attributes.Set( "FilterBlur", blurSize );
		attributes.Set( "FilterSaturate", ComputedStyle.FilterSaturate.Value.GetFraction( 1.0f ) );
		attributes.Set( "FilterSepia", ComputedStyle.FilterSepia.Value.GetFraction( 1.0f ) );
		attributes.Set( "FilterBrightness", ComputedStyle.FilterBrightness.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterContrast", ComputedStyle.FilterContrast.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterInvert", ComputedStyle.FilterInvert.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterHueRotate", ComputedStyle.FilterHueRotate.Value.GetPixels( 1.0f ) );
		attributes.Set( "FilterTint", ComputedStyle.FilterTint ?? Vector4.One );

		float growSize = blurSize;

		//Handle masks
		bool hasMask = ComputedStyle.MaskImage != null;
		attributes.SetCombo( "D_MASK_IMAGE", hasMask ? 1 : 0 );

		if ( hasMask )
		{
			var imageRectInput = new ImageRect.Input
			{
				ScaleToScreen = ScaleToScreen,
				Image = ComputedStyle?.MaskImage,
				PanelRect = Box.RectOuter,
				DefaultSize = Length.Auto,
				ImagePositionX = ComputedStyle.MaskPositionX,
				ImagePositionY = ComputedStyle.MaskPositionY,
				ImageSizeX = ComputedStyle.MaskSizeX,
				ImageSizeY = ComputedStyle.MaskSizeY,
			};

			var maskCalc = ImageRect.Calculate( imageRectInput );

			attributes.Set( "MaskPos", maskCalc.Rect );
			attributes.Set( "MaskTexture", ComputedStyle?.MaskImage );
			attributes.Set( "MaskMode", (int)(ComputedStyle?.MaskMode ?? MaskMode.MatchSource) );
			attributes.Set( "MaskAngle", (ComputedStyle?.MaskAngle?.GetPixels( 360.0f ) ?? 0.0f).DegreeToRadian() );
			attributes.Set( "MaskScope", (int)(ComputedStyle?.MaskScope ?? MaskScope.Default) );

			var filter = (ComputedStyle?.ImageRendering ?? ImageRendering.Anisotropic) switch
			{
				ImageRendering.Point => FilterMode.Point,
				ImageRendering.Bilinear => FilterMode.Bilinear,
				ImageRendering.Trilinear => FilterMode.Trilinear,
				_ => FilterMode.Anisotropic
			};

			var sampler = (ComputedStyle?.MaskRepeat ?? BackgroundRepeat.Repeat) switch
			{
				BackgroundRepeat.RepeatX => new SamplerState { AddressModeV = TextureAddressMode.Clamp, Filter = filter },
				BackgroundRepeat.RepeatY => new SamplerState { AddressModeU = TextureAddressMode.Clamp, Filter = filter },
				BackgroundRepeat.NoRepeat => new SamplerState
				{
					AddressModeU = TextureAddressMode.Border,
					AddressModeV = TextureAddressMode.Border,
					Filter = filter
				},
				BackgroundRepeat.Clamp => new SamplerState
				{
					AddressModeU = TextureAddressMode.Clamp,
					AddressModeV = TextureAddressMode.Clamp,
					Filter = filter
				},
				_ => new SamplerState { Filter = filter }
			};

			attributes.Set( "SamplerIndex", SamplerState.GetBindlessIndex( sampler ) );
			attributes.Set( "BorderSamplerIndex", SamplerState.GetBindlessIndex( new SamplerState
			{
				AddressModeU = TextureAddressMode.Border,
				AddressModeV = TextureAddressMode.Border,
				Filter = filter
			} ) );
		}

		attributes.SetCombo( "D_BLENDMODE", render.OverrideBlendMode );
		commandList.DrawQuad( _panelLayerBounds.Grow( growSize ).Ceiling(), Material.UI.Filter, Color.White );
	}

	//Draws shadows for the current layer into the specified command list.
	private void DrawPreFilterShadows( CommandList commandList )
	{
		foreach ( var shadow in ComputedStyle.FilterDropShadow )
		{
			var outerRect = _panelLayerBounds;

			var shadowSize = new Vector2( shadow.OffsetX, shadow.OffsetY );

			//Grow outerRect so that it can fit the shadow
			float growSize = MathF.Max( shadowSize.x, shadowSize.y );
			growSize = MathF.Max( 1.0f, growSize );
			growSize *= MathF.Max( 1.0f, shadow.Blur * 2.0f );
			outerRect = outerRect.Grow( growSize );

			ResetPrefilterAttributes( commandList );

			commandList.Attributes.Set( "FilterDropShadowScale", _panelLayerBounds.Size / outerRect.Size );
			commandList.Attributes.Set( "FilterDropShadowOffset", shadowSize );
			commandList.Attributes.Set( "FilterDropShadowBlur", shadow.Blur );
			commandList.Attributes.Set( "FilterDropShadowColor", shadow.Color );

			commandList.DrawQuad( outerRect, Material.UI.DropShadow, Color.White );
		}
	}

	//Draws borders for the current layer into the specified command list.
	private void DrawPreFilterBorder( CommandList commandList )
	{
		float filterBorderWidth = ComputedStyle.FilterBorderWidth.Value.GetPixels( 1.0f );
		filterBorderWidth *= ScaleToScreen;

		if ( filterBorderWidth > 0.0f )
		{
			var outerRect = _panelLayerBounds;

			//Grow outerRect so that it can fit the border
			outerRect = outerRect.Grow( filterBorderWidth );

			ResetPrefilterAttributes( commandList );

			commandList.Attributes.Set( "FilterBorderWrapColorScale", _panelLayerBounds.Size / outerRect.Size );
			commandList.Attributes.Set( "FilterBorderWrapColor", ComputedStyle.FilterBorderColor.Value );
			commandList.Attributes.Set( "FilterBorderWrapWidth", filterBorderWidth );

			commandList.DrawQuad( outerRect, Material.UI.BorderWrap, Color.White );
		}
	}

	private void ResetPrefilterAttributes( CommandList commandList )
	{
		commandList.Attributes.Set( "FilterDropShadowScale", 0 );
		commandList.Attributes.Set( "FilterDropShadowOffset", 0 );
		commandList.Attributes.Set( "FilterDropShadowBlur", 0 );
		commandList.Attributes.Set( "FilterDropShadowColor", 0 );

		commandList.Attributes.Set( "FilterBorderWrapColor", 0 );
		commandList.Attributes.Set( "FilterBorderWrapWidth", 0 );
	}

	//Returns true if this panel has a layer that needs post-children rendering.
	internal bool HasPanelLayer => _panelLayerSize != null;
}
