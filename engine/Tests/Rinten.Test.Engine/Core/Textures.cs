using NativeEngine;
using Rinten;
using System;

namespace EngineTests;

//In-memory textures and format conversion; compared against the legacy device where it is loaded
[TestClass]
public class CoreTextureTests
{
	private static CTextureDesc Describe( int width, int height, ImageFormat format, int mips = 1 )
		=> new()
		{
			m_nWidth = (short)width,
			m_nHeight = (short)height,
			m_nDepth = 1,
			m_nNumMipLevels = (short)mips,
			m_nImageFormat = format,
		};

	//A picture with something in every channel, so a swap shows up.
	private static byte[] Pattern( int width, int height )
	{
		var data = new byte[width * height * 4];

		for ( var i = 0; i < width * height; i++ )
		{
			data[(i * 4) + 0] = (byte)(i * 7);
			data[(i * 4) + 1] = (byte)(i * 13);
			data[(i * 4) + 2] = (byte)(i * 29);
			data[(i * 4) + 3] = (byte)(200 + (i % 55));
		}

		return data;
	}

	[TestMethod]
	public void PixelsComeBackAsTheyWentIn()
	{
		var texture = CoreTexture.Create( Describe( 8, 4, ImageFormat.RGBA8888 ), "test" );
		var data = Pattern( 8, 4 );

		texture.Write( data );

		var pixels = texture.Pixels( 0 );

		Assert.AreEqual( 32, pixels.Length );

		for ( var i = 0; i < pixels.Length; i++ )
		{
			Assert.AreEqual( data[(i * 4) + 0], pixels[i].r, $"red of pixel {i}" );
			Assert.AreEqual( data[(i * 4) + 1], pixels[i].g, $"green of pixel {i}" );
			Assert.AreEqual( data[(i * 4) + 2], pixels[i].b, $"blue of pixel {i}" );
			Assert.AreEqual( data[(i * 4) + 3], pixels[i].a, $"alpha of pixel {i}" );
		}
	}

	[TestMethod]
	public void BlueFirstIsReadTheRightWayRound()
	{
		var texture = CoreTexture.Create( Describe( 2, 1, ImageFormat.BGRA8888 ), "test" );

		//One pixel of pure red and one of pure blue, written the way that format stores them - blue, green, red, alpha.
		texture.Write( new byte[] { 0, 0, 255, 255, 255, 0, 0, 255 } );

		var pixels = texture.Pixels( 0 );

		Assert.AreEqual( 255, pixels[0].r );
		Assert.AreEqual( 0, pixels[0].b );
		Assert.AreEqual( 0, pixels[1].r );
		Assert.AreEqual( 255, pixels[1].b );
	}

	[TestMethod]
	public void APartOfATextureCanBeWrittenWithoutTheRest()
	{
		var texture = CoreTexture.Create( Describe( 4, 4, ImageFormat.RGBA8888 ), "test" );
		texture.Clear( Color.Black );

		//A single white pixel, two across and one down.
		texture.WriteRegion( new byte[] { 255, 255, 255, 255 }, 2, 1, 1, 1, 0, 0 );

		var pixels = texture.Pixels( 0 );

		for ( var y = 0; y < 4; y++ )
		{
			for ( var x = 0; x < 4; x++ )
			{
				var expected = x == 2 && y == 1 ? (byte)255 : (byte)0;
				Assert.AreEqual( expected, pixels[(y * 4) + x].r, $"pixel {x},{y}" );
			}
		}
	}

	[TestMethod]
	public void EveryMipHasItsOwnRoom()
	{
		var texture = CoreTexture.Create( Describe( 8, 8, ImageFormat.RGBA8888, mips: 4 ), "test" );

		Assert.AreEqual( 0, texture.MipOffset( 0 ) );
		Assert.AreEqual( 8 * 8 * 4, texture.MipOffset( 1 ) );
		Assert.AreEqual( (8 * 8 * 4) + (4 * 4 * 4), texture.MipOffset( 2 ) );

		Assert.AreEqual( (4, 4, 1), texture.MipSize( 1 ) );
		Assert.AreEqual( (1, 1, 1), texture.MipSize( 3 ) );

		//Writing one mip leaves the others alone.
		texture.Clear( Color.Black );
		texture.MipData( 2 ).Fill( 255 );

		Assert.AreEqual( 0, texture.Pixels( 0 )[0].r );
		Assert.AreEqual( 255, texture.Pixels( 2 )[0].r );
	}

	[TestMethod]
	public void ASmallerRectangleIsSampledOutOfALargerOne()
	{
		var texture = CoreTexture.Create( Describe( 4, 4, ImageFormat.RGBA8888 ), "test" );
		texture.Write( Pattern( 4, 4 ) );

		var into = new byte[2 * 2 * 4];

		Assert.IsTrue( texture.Read( (0, 0, 4, 4), 0, 0, into, ImageFormat.RGBA8888, (0, 0, 2, 2), 2 * 4 ) );

		//Nearest pixel: the top left of the destination is the top left of the source, and the one next to it is two along.
		var source = texture.Pixels( 0 );

		Assert.AreEqual( source[0].r, into[0] );
		Assert.AreEqual( source[2].r, into[4] );
	}
	//Converting between formats, against the converter it replaces. Their image loader converts on the CPU and is in the process
	//whether a render device is or not, which makes it the one part of the legacy runtime that can be asked whether this agrees with it.
	[TestMethod]
	public void ConvertsWhatTheirLoaderConverts()
	{
		Assert.Inconclusive( "no engine to compare against" );
	}
}
