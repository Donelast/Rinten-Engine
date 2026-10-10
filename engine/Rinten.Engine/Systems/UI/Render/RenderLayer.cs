using Rinten.Rendering;

namespace Rinten.UI;

//All descriptor types build into this This is higher than the GPUBoxInstance because of the texture refs but I want the texture refs so we can
//refresh bindless indices at render time But this should be refactored down into the descriptor itself somehow, since we want to support custom ones..
internal struct RenderInstance
{
	public GPUBoxInstance GPU;
	public BlendMode BlendMode;
	public int Pass;
	public Texture BackgroundImage;
	public Texture BorderImage;
	public bool HasInverseScissor;
	public PanelRenderer.GPUScissor InverseScissor;

	/// <summary>Index into <see cref="RenderLayer.LocalTransforms"/>, plus one; zero for none.</summary>
	public int LocalTransform;
}

//Pairs a user draw descriptor with its position in the instance stream.
internal struct UserRenderEntry
{
	public IPanelDraw Descriptor;
	public int InsertionIndex;
}

//Collects all draw instances for a panel's cached render data. Rebuilt when dirty, drawn during the gather phase.
internal class RenderLayer
{
	//Per-panel spatial/render state
	public Matrix TransformMat;
	public PanelRenderer.GPUScissor Scissor;

	//Draw descriptors
	public List<RenderInstance> Instances = [];

	//These should be in the above list, but I'm bored of coding this
	public List<BackdropDrawDescriptor> Backdrops = [];

	/// <summary>Transforms belonging to single boxes rather than to the panel.</summary>
	public List<Matrix> LocalTransforms = [];

	//User draw descriptors, ordered by InsertionIndex into Instances
	public List<UserRenderEntry> CustomEntries = [];

	//Trackers whilst building
	int _buildPass;
	BlendMode _buildBlendMode = BlendMode.Normal;
	bool _buildAnyBox;

	public int Total => Instances.Count + Backdrops.Count + CustomEntries.Count;
	public bool IsEmpty => Instances.Count == 0 && Backdrops.Count == 0 && CustomEntries.Count == 0;

	public void AddShadow( in ShadowDrawDescriptor desc )
	{
		Instances.Add( new RenderInstance
		{
			GPU = GPUBoxInstance.FromShadow( desc ),
			BlendMode = desc.Inset ? _buildBlendMode : BlendMode.Normal,
			Pass = desc.Inset ? _buildPass : 0,
			HasInverseScissor = !desc.Inset,
			InverseScissor = !desc.Inset ? new PanelRenderer.GPUScissor
			{
				Rect = new Rect( desc.ScissorRect.x, desc.ScissorRect.y, desc.ScissorRect.z - desc.ScissorRect.x, desc.ScissorRect.w - desc.ScissorRect.y ),
				CornerRadius = desc.ScissorCornerRadius,
				Matrix = desc.ScissorTransformMat,
				Invert = true,
			} : default,
		} );
	}

	public void AddBox( in BoxDrawDescriptor desc )
	{
		if ( desc.IsTwoPass ) return;

		if ( _buildAnyBox && desc.OverrideBlendMode != _buildBlendMode )
			_buildPass++;

		_buildBlendMode = desc.OverrideBlendMode;
		_buildAnyBox = true;

		Instances.Add( new RenderInstance
		{
			GPU = GPUBoxInstance.From( desc ),
			BlendMode = desc.OverrideBlendMode,
			Pass = _buildPass,
			BackgroundImage = desc.HasImage ? desc.BackgroundImage : null,
			BorderImage = desc.HasBorderImage ? desc.BorderImageTexture : null,
			LocalTransform = AddLocalTransform( desc.LocalTransform ),
		} );
	}

	//Keeps a box's own transform, and answers with the index it is under.
	private int AddLocalTransform( in Matrix? transform )
	{
		if ( !transform.HasValue ) return 0;

		LocalTransforms.Add( transform.Value );

		return LocalTransforms.Count;
	}

	public void AddOutline( in OutlineDrawDescriptor desc )
	{
		Instances.Add( new RenderInstance
		{
			GPU = GPUBoxInstance.FromOutline( desc ),
			BlendMode = _buildBlendMode,
			Pass = _buildPass,
		} );
	}

	public void AddCustom( IPanelDraw desc )
	{
		if ( desc is null ) return;

		CustomEntries.Add( new UserRenderEntry
		{
			Descriptor = desc,
			InsertionIndex = Instances.Count,
		} );
	}

	/// <summary>Clear all instances from this layer.</summary>
	public void Clear()
	{
		Instances.Clear();
		Backdrops.Clear();
		LocalTransforms.Clear();
		CustomEntries.Clear();
		_buildPass = 0;
		_buildBlendMode = BlendMode.Normal;
		_buildAnyBox = false;
	}

	//Pool management
	static readonly List<RenderLayer> Pool = new();
	static int activeCount;

	internal static int ActiveCount => activeCount;
	internal static int PoolCount => Pool.Count;

	public static RenderLayer Rent()
	{
		activeCount++;

		if ( Pool.Count > 0 )
		{
			var layer = Pool[^1];
			Pool.RemoveAt( Pool.Count - 1 );
			layer.Clear();
			return layer;
		}

		return new RenderLayer();
	}

	public static void Return( RenderLayer layer )
	{
		activeCount--;
		layer.Clear();
		Pool.Add( layer );
	}
}
