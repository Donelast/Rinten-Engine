using Rinten.Engine;
using Rinten.Rendering;

namespace Rinten.UI;

public partial class Panel
{
	internal BlendMode BackgroundBlendMode;

	//Set wherever what this panel draws may have changed; every set also moves RenderVersion and the tree's TreeGeneration.
	internal bool IsRenderDirty
	{
		get => field;
		set
		{
			field = value;
			if ( value )
			{
				RenderVersion++;
				MoveGeneration();
			}
		}
	} = true;

	//Counts the times this panel was marked dirty to draw, so a cache of what it drew can tell whether it is still the same.
	internal uint RenderVersion;

	//Bumped when anything drawn or hit-testable in this root's tree may have changed; read on roots only
	internal uint TreeGeneration;

	//Moves the TreeGeneration of the tree this panel is in.
	internal void MoveGeneration()
	{
		var root = this;
		while ( root.Parent is { } up ) root = up;
		root.TreeGeneration++;
	}

	//What the flat render walk emitted for this panel last, kept by CoreUiRenderer while nothing about it changed.
	internal CoreUiRenderer.PanelGeometry Geometry;

	internal readonly CommandList LayerCommandList;

	internal int _lastScissorHash;
	internal Matrix? _lastLayerMatrix;

	internal enum RenderMode : byte { Inline, Batched, Layer }

	internal int CachedBackgroundVersion;
	internal RenderLayer CachedDescriptors;
	internal RenderMode CachedRenderMode;
	internal float CachedRenderOpacity = 1.0f;
	internal BlendMode CachedOverrideBlendMode = BlendMode.Normal;

	public void MarkRenderDirty()
	{
		IsRenderDirty = true;
	}

	/// <summary>Override this to draw custom graphics for this panel using the <see cref="Draw"/> API. <example> <code> public override void OnDraw() { var r = Box.RectInner; Draw.Rect( r, Color.Blue.WithAlpha( 0.2f ), cornerRadius: 4 ); Draw.Text( "Score: 100", r, 16, Color.White, TextFlag.Center ); } </code> </example></summary>
	public virtual void OnDraw()
	{
	}

	//Whether OnDraw only depends on state reported via MarkRenderDirty, so its output can be reused
	protected internal virtual bool RetainsDrawing => false;

	//Whether a walk that draws this panel itself should skip its OnDraw rather than draw it twice.
	internal virtual bool DrawnByWalk => false;

	[Obsolete( "Use Draw" )]
	public virtual void BuildContentCommandList( CommandList commandList, ref RenderState state )
	{
	}

	[Obsolete( "Use Draw" )]
	public virtual void BuildCommandList( CommandList commandList )
	{
	}

	[Obsolete( "Use Draw" )]
	public virtual void DrawContent( ref RenderState state )
	{
	}

	[Obsolete( "Use Draw" )]
	public virtual void DrawBackground( ref RenderState state )
	{
	}

	[Obsolete( "Use Draw" )]
	internal virtual void DrawContent( PanelRenderer renderer, ref RenderState state )
	{
	}

	//Build descriptors for all children. Called during tick phase.
	internal void BuildDescriptorsForChildren( PanelRenderer render, ref RenderState state )
	{
		using var _ = render.Clip( this );

		if ( _renderChildrenDirty )
		{
			_renderChildren.Sort( ( x, y ) => x.GetRenderOrderIndex() - y.GetRenderOrderIndex() );
			_renderChildrenDirty = false;
		}

		for ( int i = 0; i < _renderChildren.Count; i++ )
		{
			render.BuildDescriptors( _renderChildren[i], state );
		}
	}

}
