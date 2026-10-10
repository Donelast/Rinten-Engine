using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Rinten;

//One node of an FBX document: its name, its values, and the nodes under it. A value is written as the type it is:
//bool, short, int, long, float, double, string, a byte array for raw bytes, and int, long, float and double arrays.
internal sealed class FbxNode
{
	public string Name { get; }
	public List<object> Values { get; } = new();
	public List<FbxNode> Children { get; } = new();

	public FbxNode( string name, params object[] values )
	{
		Name = name;
		Values.AddRange( values );
	}

	/// <summary>A node under this one.</summary>
	public FbxNode Add( string name, params object[] values )
	{
		var node = new FbxNode( name, values );
		Children.Add( node );
		return node;
	}

	/// <summary>A row of a <c>Properties70</c> block: the property's name, its type as FBX spells it, a label, its flags, and its value.</summary>
	public FbxNode Property( string name, string type, string label, string flags, params object[] values )
	{
		var row = Add( "P", name, type, label, flags );
		row.Values.AddRange( values );
		return row;
	}
}

//Writes binary FBX 7.4 (32-bit offsets), laid out the way Blender's exporter writes it
internal static class FbxBinary
{
	public const uint Version = 7400;

	//What ends a list of children: a record that is all zeros - 13 bytes before 7.5.
	private const int NullRecord = 13;

	//Arrays longer than this many bytes go in compressed.
	private const int CompressAbove = 128;

	/// <summary>The file id and creation time every Blender export carries, which go with the footer id below: a reader that checks one against the other finds them matching.</summary>
	public static readonly byte[] FileId = [0x28, 0xb3, 0x2a, 0xeb, 0xb6, 0x24, 0xcc, 0xc2, 0xbf, 0xc8, 0xb0, 0x2a, 0xa9, 0x2b, 0xfc, 0xf1];
	public const string CreationTime = "1970-01-01 10:00:00:000";

	private static readonly byte[] FooterId = [0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e];
	private static readonly byte[] FooterMagic = [0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b];

	/// <summary>The document, into a new file at <paramref name="path"/>.</summary>
	public static void Save( string path, IReadOnlyList<FbxNode> nodes )
	{
		//Into memory first: every record's end offset is written back once its children are out, and a half-written file is worse than none.
		using var memory = new MemoryStream();

		Write( memory, nodes );

		File.WriteAllBytes( path, memory.ToArray() );
	}

	/// <summary>The document, into a stream that starts at the beginning of the file.</summary>
	public static void Write( Stream stream, IReadOnlyList<FbxNode> nodes )
	{
		using var w = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true );

		w.Write( Encoding.ASCII.GetBytes( "Kaydara FBX Binary  " ) );
		w.Write( (byte)0x00 );
		w.Write( (byte)0x1a );
		w.Write( (byte)0x00 );
		w.Write( Version );

		foreach ( var node in nodes )
			WriteNode( w, node );

		w.Write( new byte[NullRecord] );

		WriteFooter( w );
	}

	private static void WriteNode( BinaryWriter w, FbxNode node )
	{
		var stream = w.BaseStream;
		var start = stream.Position;

		w.Write( 0u ); //where the record ends, once that is known
		w.Write( (uint)node.Values.Count );
		w.Write( 0u ); //how long its values are, likewise

		var name = Encoding.ASCII.GetBytes( node.Name );
		w.Write( (byte)name.Length );
		w.Write( name );

		var valuesStart = stream.Position;

		foreach ( var value in node.Values )
			WriteValue( w, value );

		var valuesEnd = stream.Position;

		foreach ( var child in node.Children )
			WriteNode( w, child );

		//A list of children ends in a null record, and so does a node with nothing in it at all - that is how the Autodesk SDK writes an empty block.
		if ( node.Children.Count > 0 || node.Values.Count == 0 )
			w.Write( new byte[NullRecord] );

		var end = stream.Position;

		stream.Position = start;
		w.Write( checked((uint)end) );

		stream.Position = start + 8;
		w.Write( checked((uint)(valuesEnd - valuesStart)) );

		stream.Position = end;
	}

	private static void WriteValue( BinaryWriter w, object value )
	{
		switch ( value )
		{
			case bool b:
				w.Write( (byte)'C' );
				w.Write( (byte)(b ? 1 : 0) );
				break;

			case short s:
				w.Write( (byte)'Y' );
				w.Write( s );
				break;

			case int i:
				w.Write( (byte)'I' );
				w.Write( i );
				break;

			case long l:
				w.Write( (byte)'L' );
				w.Write( l );
				break;

			case float f:
				w.Write( (byte)'F' );
				w.Write( f );
				break;

			case double d:
				w.Write( (byte)'D' );
				w.Write( d );
				break;

			case string text:
				var bytes = Encoding.UTF8.GetBytes( text );
				w.Write( (byte)'S' );
				w.Write( (uint)bytes.Length );
				w.Write( bytes );
				break;

			case byte[] raw:
				w.Write( (byte)'R' );
				w.Write( (uint)raw.Length );
				w.Write( raw );
				break;

			case int[] ints:
				WriteArray<int>( w, 'i', ints );
				break;

			case long[] longs:
				WriteArray<long>( w, 'l', longs );
				break;

			case float[] floats:
				WriteArray<float>( w, 'f', floats );
				break;

			case double[] doubles:
				WriteArray<double>( w, 'd', doubles );
				break;

			default:
				throw new ArgumentException( $"FBX has no way to write a {value?.GetType().Name ?? "null"}" );
		}
	}

	//An array: its type, how many there are, then the values - through zlib when there are enough of them for that to be worth it.
	private static void WriteArray<T>( BinaryWriter w, char type, T[] values ) where T : unmanaged
	{
		var raw = MemoryMarshal.AsBytes( values.AsSpan() );

		w.Write( (byte)type );
		w.Write( (uint)values.Length );

		if ( raw.Length <= CompressAbove )
		{
			w.Write( 0u );
			w.Write( (uint)raw.Length );
			w.Write( raw );
			return;
		}

		using var packed = new MemoryStream();

		using ( var zlib = new ZLibStream( packed, CompressionLevel.Optimal, leaveOpen: true ) )
			zlib.Write( raw );

		w.Write( 1u );
		w.Write( (uint)packed.Length );
		w.Write( packed.GetBuffer(), 0, (int)packed.Length );
	}

	//The footer: its id, padding to sixteen bytes, the version, then a run of zeros and a magic nobody has explained - byte for byte what the SDK writes.
	private static void WriteFooter( BinaryWriter w )
	{
		w.Write( FooterId );
		w.Write( new byte[4] );

		var offset = w.BaseStream.Position;
		var pad = (int)(((offset + 15) & ~15L) - offset);
		if ( pad == 0 ) pad = 16;

		w.Write( new byte[pad] );
		w.Write( Version );
		w.Write( new byte[120] );
		w.Write( FooterMagic );
	}
}
