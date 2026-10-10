using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TestFixtures;

//Writes a tiny .vmap map (one quad brush, one prop) to test the reader's byte arithmetic
public static class HammerMap
{
	private const byte Element = 1;
	private const byte Int = 2;
	private const byte Float = 3;
	private const byte String = 5;
	private const byte Vector2Type = 9;
	private const byte Vector3Type = 10;
	private const byte Vector4Type = 11;

	//What is added to a type to make it an array of that type, verified against a real map file.
	private const byte ArrayOf = 32;

	//Map bytes with one prop and an optional brush; brush needs the material system, so it can be skipped
	public static byte[] Build( bool withBrush = true )
	{
		var strings = new Strings();
		var elements = new List<Node>();

		//The order the elements go in is the order they are indexed by, and an attribute points at one by that index.
		var root = Add( elements, "CMapRootElement", "" );
		var world = Add( elements, "CMapWorld", "" );
		var brush = Add( elements, "CMapMesh", "" );
		var data = Add( elements, "CDmePolygonMesh", "" );
		var vertices = Add( elements, "CDmePolygonMeshDataArray", "" );
		var faces = Add( elements, "CDmePolygonMeshDataArray", "" );
		var positions = Add( elements, "CDmePolygonMeshDataStream", "position:0" );
		var materials = Add( elements, "CDmePolygonMeshDataStream", "materialindex:0" );
		var axisU = Add( elements, "CDmePolygonMeshDataStream", "textureAxisU:0" );
		var axisV = Add( elements, "CDmePolygonMeshDataStream", "textureAxisV:0" );
		var scales = Add( elements, "CDmePolygonMeshDataStream", "textureScale:0" );
		var prop = Add( elements, "CMapEntity", "" );
		var properties = Add( elements, "EditGameClassProps", "" );

		root.Points( "world", world );
		world.PointsAt( "children", withBrush ? new[] { brush, prop } : new[] { prop } );

		brush.Vector3( "origin", 128, 0, 64 );
		brush.Vector3( "angles", 0, 0, 0 );
		brush.Vector3( "scales", 1, 1, 1 );
		brush.Number( "smoothingAngle", 40 );
		brush.Word( "physicsType", "default" );
		brush.Points( "meshData", data );

		//Four half edges round one face, each pointing at the corner it starts from and at the one that follows it.
		data.Numbers( "vertexEdgeIndices", 0, 1, 2, 3 );
		data.Numbers( "vertexDataIndices", 0, 1, 2, 3 );
		data.Numbers( "edgeVertexIndices", 0, 1, 2, 3 );
		data.Numbers( "edgeNextIndices", 1, 2, 3, 0 );
		data.Numbers( "faceEdgeIndices", 0 );
		data.Numbers( "faceDataIndices", 0 );
		data.Words( "materials" );
		data.Points( "vertexData", vertices );
		data.Points( "faceData", faces );

		vertices.PointsAt( "streams", new[] { positions } );
		faces.PointsAt( "streams", new[] { materials, axisU, axisV, scales } );

		//A 64 inch square on the ground, which is what the mesh has to come out 1.6256 metres across.
		positions.Vector3s( "data", (0, 0, 0), (64, 0, 0), (64, 64, 0), (0, 64, 0) );
		materials.Numbers( "data", 0 );
		axisU.Vector4s( "data", (1, 0, 0, 0) );
		axisV.Vector4s( "data", (0, 1, 0, 0) );
		scales.Vector2s( "data", (0.25f, 0.25f) );

		prop.Vector3( "origin", 0, 0, 32 );
		prop.Vector3( "angles", 0, 0, 0 );
		prop.Vector3( "scales", 1, 1, 1 );
		prop.Points( "entity_properties", properties );

		properties.Word( "classname", "prop_static" );
		properties.Word( "model", "models/chair.vmdl" );

		foreach ( var element in elements )
		{
			strings.Of( element.TypeName );
			strings.Of( element.Name );

			foreach ( var attribute in element.Attributes )
			{
				strings.Of( attribute.Name );

				if ( attribute.Type == String ) strings.Of( (string)attribute.Value );
			}
		}

		using var body = new MemoryStream();
		using var write = new BinaryWriter( body, Encoding.UTF8 );

		write.Write( Encoding.ASCII.GetBytes( "<!-- dmx encoding binary 9 format vmap 30 -->\n" ) );
		write.Write( (byte)0 );

		write.Write( 0 ); //No prefix elements, which version 9 allows.

		write.Write( strings.All.Count );

		foreach ( var value in strings.All ) Cstring( write, value );

		write.Write( elements.Count );

		foreach ( var element in elements )
		{
			write.Write( strings.Of( element.TypeName ) );
			write.Write( strings.Of( element.Name ) );
			write.Write( new byte[16] ); //The guid, which nothing here points by.
		}

		foreach ( var element in elements )
		{
			write.Write( element.Attributes.Count );

			foreach ( var attribute in element.Attributes )
			{
				write.Write( strings.Of( attribute.Name ) );
				write.Write( attribute.Type );

				Value( write, attribute, strings, elements );
			}
		}

		write.Flush();

		return body.ToArray();
	}

	private static void Value( BinaryWriter write, Attribute attribute, Strings strings, List<Node> elements )
	{
		switch ( attribute.Type )
		{
			case Element:
				write.Write( elements.IndexOf( (Node)attribute.Value ) );
				break;

			case Int:
				write.Write( (int)attribute.Value );
				break;

			case Float:
				write.Write( (float)attribute.Value );
				break;

			case String:
				write.Write( strings.Of( (string)attribute.Value ) );
				break;

			case Vector3Type:
				foreach ( var number in (float[])attribute.Value ) write.Write( number );
				break;

			case Element + ArrayOf:
				var pointed = (Node[])attribute.Value;
				write.Write( pointed.Length );
				foreach ( var one in pointed ) write.Write( elements.IndexOf( one ) );
				break;

			case Int + ArrayOf:
				var integers = (int[])attribute.Value;
				write.Write( integers.Length );
				foreach ( var number in integers ) write.Write( number );
				break;

			//A string inside an array is written out in full even where a lone string of the same type would have been an index into the table.
			case String + ArrayOf:
				var words = (string[])attribute.Value;
				write.Write( words.Length );
				foreach ( var word in words ) Cstring( write, word );
				break;

			//Every other array in a map is a run of floats, and how many of them each item is is the only thing that differs.
			default:
				var rows = (float[][])attribute.Value;
				write.Write( rows.Length );
				foreach ( var row in rows )
					foreach ( var number in row ) write.Write( number );
				break;
		}
	}

	private static void Cstring( BinaryWriter write, string value )
	{
		write.Write( Encoding.UTF8.GetBytes( value ) );
		write.Write( (byte)0 );
	}

	private static Node Add( List<Node> elements, string typeName, string name )
	{
		var element = new Node( typeName, name );
		elements.Add( element );
		return element;
	}

	//The table every name in the file is an index into.
	private sealed class Strings
	{
		private readonly Dictionary<string, int> known = new( StringComparer.Ordinal );

		public List<string> All { get; } = new();

		public int Of( string value )
		{
			value ??= "";

			if ( known.TryGetValue( value, out var index ) ) return index;

			index = All.Count;
			All.Add( value );
			known[value] = index;

			return index;
		}
	}

	private sealed record Attribute( string Name, byte Type, object Value );

	private sealed class Node
	{
		public Node( string typeName, string name )
		{
			TypeName = typeName;
			Name = name;
		}

		public string TypeName { get; }
		public string Name { get; }
		public List<Attribute> Attributes { get; } = new();

		public void Word( string name, string value ) => Attributes.Add( new( name, String, value ) );
		public void Number( string name, float value ) => Attributes.Add( new( name, Float, value ) );
		public void Points( string name, Node value ) => Attributes.Add( new( name, Element, value ) );

		public void Vector3( string name, float x, float y, float z )
			=> Attributes.Add( new( name, Vector3Type, new[] { x, y, z } ) );

		public void PointsAt( string name, Node[] values )
			=> Attributes.Add( new( name, Element + ArrayOf, values ) );

		public void Numbers( string name, params int[] values )
			=> Attributes.Add( new( name, Int + ArrayOf, values ) );

		public void Words( string name, params string[] values )
			=> Attributes.Add( new( name, String + ArrayOf, values ) );

		public void Vector2s( string name, params (float X, float Y)[] values )
			=> Attributes.Add( new( name, Vector2Type + ArrayOf,
				values.Select( v => new[] { v.X, v.Y } ).ToArray() ) );

		public void Vector3s( string name, params (float X, float Y, float Z)[] values )
			=> Attributes.Add( new( name, Vector3Type + ArrayOf,
				values.Select( v => new[] { v.X, v.Y, v.Z } ).ToArray() ) );

		public void Vector4s( string name, params (float X, float Y, float Z, float W)[] values )
			=> Attributes.Add( new( name, Vector4Type + ArrayOf,
				values.Select( v => new[] { v.X, v.Y, v.Z, v.W } ).ToArray() ) );
	}
}
