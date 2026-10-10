using System;
using System.IO.Compression;
using System.Linq;
using Rinten;
using Rinten.Compression;

namespace EngineTests;

//Vendored LZ4 and Yoga: check the bindings (functions, argument order, enum values), not the libs
[TestClass]
public class VendoredLibraryTests
{
	private static byte[] Compressible( int length )
	{
		//Repetitive enough to compress, varied enough not to be one run.
		var data = new byte[length];

		for ( var i = 0; i < length; i++ )
			data[i] = (byte)( ( i / 7 ) % 13 );

		return data;
	}

	[TestMethod]
	public void CompressesAndDecompressesABlock()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var original = Compressible( 64 * 1024 );

		foreach ( var level in new[] { CompressionLevel.Fastest, CompressionLevel.Optimal, CompressionLevel.SmallestSize } )
		{
			var packed = LZ4.CompressBlock( original, level );

			Assert.IsTrue( packed.Length > 0, $"{level} produced nothing" );
			Assert.IsTrue( packed.Length < original.Length, $"{level} made it no smaller" );

			var unpacked = new byte[original.Length];
			var written = LZ4.DecompressBlock( packed, unpacked );

			Assert.AreEqual( original.Length, written, $"{level}" );
			Assert.IsTrue( original.SequenceEqual( unpacked ), $"{level} came back different" );
		}
	}

	[TestMethod]
	public void CompressesAndDecompressesAFrame()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		var original = Compressible( 200 * 1024 );

		var packed = LZ4.CompressFrame( original );

		Assert.IsTrue( packed.Length > 0 );
		Assert.IsTrue( packed.Length < original.Length );

		//A frame carries its own header, and the size of what is in it - which is the whole reason to use one rather than a bare block.
		var unpacked = LZ4.DecompressFrame( packed );

		Assert.AreEqual( original.Length, unpacked.Length );
		Assert.IsTrue( original.SequenceEqual( unpacked ) );
	}

	[TestMethod]
	public void EmptyInputStaysEmpty()
	{
		if ( !CoreNative.Available )
		{
			Assert.Inconclusive( "rintencore is not loaded" );
			return;
		}

		Assert.AreEqual( 0, LZ4.CompressBlock( Array.Empty<byte>() ).Length );
		Assert.AreEqual( 0, LZ4.CompressFrame( Array.Empty<byte>() ).Length );
		Assert.AreEqual( 0, LZ4.DecompressFrame( Array.Empty<byte>() ).Length );
	}

	//Panel enums must map onto Yoga's; an off-by-one justify value once centred start-aligned rows
	[TestMethod]
	public void LaysOutARow()
	{
		var root = new Rinten.UI.PanelLayout( null );
		var left = new Rinten.UI.PanelLayout( null );
		var right = new Rinten.UI.PanelLayout( null );

		root.FlexDirection = Rinten.UI.FlexDirection.Row;
		root.Width = 200;
		root.Height = 100;

		foreach ( var child in new[] { left, right } )
		{
			child.Width = 40;
			child.Height = 20;
		}

		root.AddChild( left );
		root.AddChild( right );

		//Packed to the start: the first child at the left edge, the second against it.
		root.JustifyContent = Rinten.UI.Justify.FlexStart;
		root.CalculateLayout( 200, 100 );

		Assert.AreEqual( 200.0f, root.LayoutWidth, 0.01f, "the root is the size it was told" );
		Assert.AreEqual( 0.0f, left.LayoutX, 0.01f, "start means start" );
		Assert.AreEqual( 40.0f, right.LayoutX, 0.01f, "and the next one follows it" );

		//Centred: eighty points of children in two hundred, so sixty either side.
		root.JustifyContent = Rinten.UI.Justify.Center;
		root.CalculateLayout( 200, 100 );

		Assert.AreEqual( 60.0f, left.LayoutX, 0.01f, "centre means centre" );

		//And to the end.
		root.JustifyContent = Rinten.UI.Justify.FlexEnd;
		root.CalculateLayout( 200, 100 );

		Assert.AreEqual( 160.0f, right.LayoutX, 0.01f, "end means end" );
	}

	//Padding and a column, so that more than one enumeration is exercised - the edges, and the direction.
	[TestMethod]
	public void LaysOutAPaddedColumn()
	{
		var root = new Rinten.UI.PanelLayout( null );
		var child = new Rinten.UI.PanelLayout( null );

		root.FlexDirection = Rinten.UI.FlexDirection.Column;
		root.Width = 100;
		root.Height = 100;
		root.PaddingLeft = 10;
		root.PaddingTop = 20;

		child.Height = 30;
		child.Width = Rinten.UI.Length.Percent( 50 );

		root.AddChild( child );
		root.CalculateLayout( 100, 100 );

		Assert.AreEqual( 10.0f, child.LayoutX, 0.01f, "left padding" );
		Assert.AreEqual( 20.0f, child.LayoutY, 0.01f, "top padding" );

		//Half of the content box, which the padding has narrowed.
		Assert.AreEqual( 45.0f, child.LayoutWidth, 0.01f, "half of what is left" );
		Assert.AreEqual( 30.0f, child.LayoutHeight, 0.01f );
	}
}
