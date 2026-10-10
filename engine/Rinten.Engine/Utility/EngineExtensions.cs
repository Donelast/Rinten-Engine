using NativeEngine;
using System.Runtime.CompilerServices;

namespace Rinten;

internal static partial class RintenEngineExtensions
{
	internal static RenderMultisampleType ToEngine( this MultisampleAmount self )
	{
		switch ( self )
		{
			case MultisampleAmount.MultisampleNone: return RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
			case MultisampleAmount.Multisample2x: return RenderMultisampleType.RENDER_MULTISAMPLE_2X;
			case MultisampleAmount.Multisample4x: return RenderMultisampleType.RENDER_MULTISAMPLE_4X;
			case MultisampleAmount.Multisample6x: return RenderMultisampleType.RENDER_MULTISAMPLE_6X;
			case MultisampleAmount.Multisample8x: return RenderMultisampleType.RENDER_MULTISAMPLE_8X;
			case MultisampleAmount.Multisample16x: return RenderMultisampleType.RENDER_MULTISAMPLE_16X;
		}

		return RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
	}

	internal static MultisampleAmount FromEngine( this RenderMultisampleType self )
	{
		switch ( self )
		{
			case RenderMultisampleType.RENDER_MULTISAMPLE_NONE: return MultisampleAmount.MultisampleNone;
			case RenderMultisampleType.RENDER_MULTISAMPLE_2X: return MultisampleAmount.Multisample2x;
			case RenderMultisampleType.RENDER_MULTISAMPLE_4X: return MultisampleAmount.Multisample4x;
			case RenderMultisampleType.RENDER_MULTISAMPLE_6X: return MultisampleAmount.Multisample6x;
			case RenderMultisampleType.RENDER_MULTISAMPLE_8X: return MultisampleAmount.Multisample8x;
			case RenderMultisampleType.RENDER_MULTISAMPLE_16X: return MultisampleAmount.Multisample16x;
			default: throw new System.Exception( "Unknown multisample amount" );
		}
	}

	//MSAA sample count as a number (None and screen default are 0); the enum's raw values aren't counts
	internal static int SampleCount( this MultisampleAmount self ) => self switch
	{
		MultisampleAmount.Multisample2x => 2,
		MultisampleAmount.Multisample4x => 4,
		MultisampleAmount.Multisample6x => 6,
		MultisampleAmount.Multisample8x => 8,
		MultisampleAmount.Multisample16x => 16,

		//Screen is "whatever the window is", which is none until a window is multisampled, and None is none.
		_ => 0
	};

	/// <summary>Convert to a FloatSpan, which allows easy SIMD/AVX2 instructions</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static FloatSpan AsFloatSpan( this Span<float> span ) => new FloatSpan( span );
}
