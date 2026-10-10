using NativeEngine;

namespace Rinten;

public static partial class Graphics
{
	/// <summary>Draws a single model at the given Transform immediately.</summary>
	public static void DrawModel( Model model, Transform transform, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		attributes ??= Attributes;

		DrawModelInstanced( model, new Span<Transform>( ref transform ), attributes );
	}

	/// <summary>Draws multiple instances of a model using GPU instancing, assuming standard implemented shaders. Use `GetTransformMatrix( int instance )` in shaders to access the instance transform. There is a limit of 1,048,576 transform slots per frame when using this method.</summary>
	public static unsafe void DrawModelInstanced( Model model, Span<Transform> transforms, int lodLevel, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		//A level of the model's chain, the finest being nought. A model whose levels are a DAG has none to ask for and is drawn at its finest.
		DrawModelsAt( model, transforms, attributes, Math.Max( lodLevel, 0 ) );
	}

	/// <summary>Draws multiple instances of a model using GPU instancing, assuming standard implemented shaders. Use `GetTransformMatrix( int instance )` in shaders to access the instance transform. There is a limit of 1,048,576 transform slots per frame when using this method.</summary>
	public static unsafe void DrawModelInstanced( Model model, Span<Transform> transforms, RenderAttributes attributes = null )
	{
		DrawModelsAt( model, transforms, attributes );
	}

	//The same for transforms nobody may write to - what a command list hands over.
	internal static void DrawModelsAt( Model model, ReadOnlySpan<Transform> transforms, RenderAttributes attributes, int level = -1 )
	{
		AssertRenderBlock();

		if ( model is null || transforms.IsEmpty ) return;
		if ( !CoreImmediate.Capturing ) return;

		//One entry per transform, each with its own world matrix in the push block; lit like the world
		foreach ( var transform in transforms )
		{
			CoreImmediate.Add( new CoreImmediate.ModelBatch( model, transform, Color.White, Attributes: attributes, Level: level ) );
		}
	}

	/// <summary>Draws the model with GPU indirect instance count (use SV_InstanceID); buffer read at gather time</summary>
	public static void DrawModelInstancedIndirect( Model model, GpuBuffer buffer, int bufferOffset = 0, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		ArgumentNullException.ThrowIfNull( buffer );

		if ( bufferOffset < 0 || bufferOffset + 8L > (long)buffer.ElementCount * buffer.ElementSize )
			throw new ArgumentOutOfRangeException( nameof( bufferOffset ) );

		if ( model is null || !CoreImmediate.Capturing ) return;

		CoreImmediate.Add( new CoreImmediate.ModelBatch( model, Transform.Zero, Color.White,
			CountFrom: buffer, CountAt: bufferOffset, Attributes: attributes ) );
	}

	/// <summary>Not supported: no vertex stage reads per-instance transforms from a buffer</summary>
	public static void DrawModelInstancedIndirect( Model model, GpuBuffer transformBuffer, GpuBuffer indirectArgs, int argsOffset = 0, int lodLevel = 0, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		Warn( "Graphics.DrawModelInstancedIndirect with a buffer of transforms is not drawn: no vertex stage here reads a world matrix per instance out of a buffer" );
	}

	/// <summary>Draws the model count times in one draw at the origin; use SV_InstanceID to tell copies apart</summary>
	public static unsafe void DrawModelInstanced( Model model, int count, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		if ( model is null || count <= 0 || !CoreImmediate.Capturing ) return;

		CoreImmediate.Add( new CoreImmediate.ModelBatch( model, Transform.Zero, Color.White, Instances: count, Attributes: attributes ) );
	}
}
