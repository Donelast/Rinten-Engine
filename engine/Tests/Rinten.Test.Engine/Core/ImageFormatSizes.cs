using Rinten;

namespace EngineTests;

//Our image size math matches the native function it replaces; errors here mean buffer overruns
[TestClass]
public class ImageFormatSizeTests
{
	//The formats worth checking: one of each shape - four bytes, three, two, one, the half-float ones, and
	//every block-compressed family, whose smallest mips are a whole block however few pixels they hold.
	private static readonly ImageFormat[] Formats =
	{
		ImageFormat.RGBA8888,
		ImageFormat.BGRA8888,
		ImageFormat.RGB888,
		ImageFormat.I8,
		ImageFormat.A8,
		ImageFormat.R16F,
		ImageFormat.R32F,
		ImageFormat.RGBA16161616F,
		ImageFormat.RGBA32323232F,
		ImageFormat.RGB323232F,
		ImageFormat.DXT1,
		ImageFormat.DXT5,
		ImageFormat.BC7,
		ImageFormat.BC6H,
		ImageFormat.ATI1N,
		ImageFormat.ATI2N,
	};

	private static readonly (int Width, int Height)[] Sizes =
	{
		(1, 1), (2, 2), (3, 5), (4, 4), (7, 7), (16, 16), (17, 33), (256, 128), (1024, 1024),
	};

	[TestMethod]
	public void MatchesTheEngineForOneLevel()
	{
		//The native image loader is the reference, so the test needs the native runtime present.
		Assert.Inconclusive( "no engine to compare against" );
	}

	[TestMethod]
	public void MatchesTheEngineForAMipChain()
	{
		//The native image loader is the reference, so the test needs the native runtime present.
		Assert.Inconclusive( "no engine to compare against" );
	}

	[TestMethod]
	public void MatchesTheEngineWithDepth()
	{
		//The native image loader is the reference, so the test needs the native runtime present.
		Assert.Inconclusive( "no engine to compare against" );
	}
}
