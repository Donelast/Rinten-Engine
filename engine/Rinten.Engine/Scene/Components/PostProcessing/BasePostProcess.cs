using Rinten.MovieMaker;
using Rinten.Rendering;

namespace Rinten;

/// <summary>The base class for all post process effects.</summary>
public abstract class BasePostProcess : Component, Component.ExecuteInEditor, Component.DontExecuteOnServer
{
	PostProcessContext? _currentContext;

	internal PostProcessContext context
	{
		get
		{
			if ( !_currentContext.HasValue ) throw new System.Exception( "Should only be called during build" );
			return _currentContext.Value;
		}
	}

	/// <summary>Whether drawn in VR; false for effects that cause sickness or look wrong per eye</summary>
	public virtual bool AllowedInHeadset => true;

	//The camera we're being applied to. This is only valid during the Render call.
	protected CameraComponent Camera => context.Camera;

	//The default attributes for this post process. This will be used by helper functions like Blit.
	protected readonly RenderAttributes Attributes = new();

	//The command lists this effect's blits are recorded into, kept from frame to frame rather than made each time: an effect blits the same number
	//of times every frame, and ten effects a frame were ten lists, ten entry arrays and ten layers allocated and dropped sixty times a second.
	private readonly List<CommandList> lists = new();
	private int used;

	internal void Build( PostProcessContext ctx )
	{
		_currentContext = ctx;

		try
		{
			//always cleared before build
			Attributes.Clear();
			used = 0;
			Render();
		}
		finally
		{
			_currentContext = default;
		}
	}

	/// <summary>Override in your implementation to do your rendering</summary>
	public abstract void Render();


	/// <summary>How a full-screen pass lands on the picture it draws into.</summary>
	public enum PassBlend
	{
		/// <summary>What is drawn is what ends up there.</summary>
		Replace = 0,

		/// <summary>Added to what is there, which may take it past white.</summary>
		Additive = 2,

		/// <summary>Added into what is left before white, so it cannot pass it.</summary>
		Screen = 3,

		/// <summary>Laid over by its own alpha: what is drawn where the alpha is one, the frame where it is nought, and between the two between - how a depth of field lays its blurred frame over the sharp one.</summary>
		Mix = 4,

		/// <summary>Added by its own alpha: as much of what is drawn as the alpha says, on top of what is there - how reflections are laid over the frame.</summary>
		AddByAlpha = 5,

		/// <summary>Multiplied into what is there: the frame darkened by what is drawn, and left alone where that is white.</summary>
		Multiply = 6,
	}

	public ref struct BlitMode
	{
		/// <summary>The material to use for the blit.</summary>
		public Material Material;

		/// <summary>We'll use this instead of BasePostProcess.Attributes if set.</summary>
		public RenderAttributes Attributes;

		/// <summary>Where to place this in the render pipeline</summary>
		public Stage RenderStage;

		/// <summary>The order within the stage. Lower numbers get rendered first.</summary>
		public int Order;

		/// <summary>If true, the backbuffer will be copied to a texture called "ColorBuffer" before the blit.</summary>
		public bool WantsBackbuffer;

		/// <summary>If both WantsBackbuffer and this is true the backbuffer will be mipped after being copied.</summary>
		public bool WantsBackbufferMips;

		/// <summary>Whether this pass reads blurred copies of what it is handed.</summary>
		public bool WantsFrameMips;

		/// <summary>How this pass lands on what is already in the picture.</summary>
		public PassBlend Blend;

		/// <summary>Whether the pass reads how far what moved by itself moved - SceneMotion in postprocess.slang. The meshes that moved are drawn into the motion picture only on a frame where something asks.</summary>
		public bool WantsMotion;

		/// <summary>Which of the shader file's fragment entries draws the pass, where the file holds more than one. Null is MainPs.</summary>
		public string Entry;

		/// <summary>Shortcut to build a simple blit mode</summary>
		public static BlitMode Simple( Material m, Stage stage, int order = 0 ) => new BlitMode { Material = m, RenderStage = stage, Order = order };

		/// <summary>Shortcut to build a blit mode that copies the backbuffer first</summary>
		public static BlitMode WithBackbuffer( Material m, Stage stage, int order = 0, bool mip = false ) => new BlitMode { Material = m, RenderStage = stage, Order = order, WantsBackbuffer = true, WantsBackbufferMips = mip };
	}

	//Helper to do a blit with the current camera's post process
	protected void Blit( BlitMode blit, string debugName )
	{
		var material = blit.Material;

		if ( !material.IsValid() ) return;

		var cl = NextList( debugName );

		if ( blit.WantsBackbuffer )
		{
			cl.Attributes.GrabFrameTexture( "ColorBuffer", blit.WantsBackbufferMips );
		}

		var attributes = blit.Attributes ?? Attributes;

		//A backbuffer copied with its mips is, here, the frame's own chain of halvings: an effect written
		//against their renderer that asked for a blurred copy of the screen gets one, out of the same pictures.
		if ( blit.WantsBackbuffer && blit.WantsBackbufferMips ) blit.WantsFrameMips = true;

		if ( blit.WantsMotion ) context.Camera.PostProcess.WantsMotion = true;

		//How it lands and whether it reads the chain of halvings go with the blit itself - not on the attributes, which outlive it.
		cl.Blit( material, attributes, blit.Entry, (int)blit.Blend, blit.WantsFrameMips );

		InsertCommandList( cl, blit.RenderStage, blit.Order, debugName );
	}

	//Lays a point effect (shader with Apply) at a stage; folded into one pass, see CorePointChain
	protected void Apply( Material shader, Stage stage, int order, string debugName )
	{
		if ( !shader.IsValid() ) return;

		var cl = NextList( debugName );

		cl.Apply( shader, Attributes );

		InsertCommandList( cl, stage, order, debugName );
	}


	//This frame's next command list, one kept from the last frame where there is one.
	protected CommandList NextList( string debugName )
	{
		CommandList cl;

		if ( used < lists.Count )
		{
			cl = lists[used];
			cl.Reset();
			cl.DebugName = debugName;
		}
		else
		{
			cl = new CommandList( debugName );
			lists.Add( cl );
		}

		used++;

		return cl;
	}

	//Helper to do a blit with the current camera's post process
	protected void BlitSimple( Material shader, Stage stage, int order, string debugName )
	{
		Blit( BlitMode.Simple( shader, stage, order ), debugName );
	}

	//Helper to add a command list to the current camera's post process
	protected void InsertCommandList( CommandList cl, Rinten.Rendering.Stage stage, int order, string debugName )
	{
		var layer = context.Camera.PostProcess.CreateLayer( stage );
		layer.Order = order;
		layer.CommandList = cl;
		layer.Name = debugName;
	}
}

/// <summary>Like BasePostProcess but enables access to helper methods for accessing from multiple instances using GetWeighted.</summary>
public abstract class BasePostProcess<T> : BasePostProcess where T : BasePostProcess
{
	//Helper to get a weighted value from all active post process volumes
	protected U GetWeighted<U>( System.Func<T, U> value, U defaultVal = default, bool onlyLerpBetweenVolumes = false )
	{
		U v = defaultVal;
		var lerper = Interpolator.GetDefault<U>();

		int i = 0;
		foreach ( var e in context.Components )
		{
			var target = value( (T)e.Effect );
			v = lerper.Interpolate( v, target, e.Weight );

			if ( onlyLerpBetweenVolumes && i == 0 )
				v = target;

			i++;
		}

		return v;
	}
}
