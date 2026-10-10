using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Rinten;

//The attributes below say what each field of a vertex struct means: what is at what offset, and what it is for.
/// <summary>Allows for the definition of custom vertex layouts</summary>
public static class VertexLayout
{
	//One field of a vertex struct, as the renderer needs it.
	internal readonly record struct Element( string Semantic, int Index, Type Type, int Offset, int Size );

	private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Element[]> entries = new();

	internal static Element[] Get<T>() where T : unmanaged => Get( typeof( T ) );

	internal static Element[] Get( Type t ) => entries.GetOrAdd( t, Describe );

	private static Element[] Describe( Type t )
	{
		var elements = new List<Element>();
		var slots = new List<string>();

		var offset = 0;

		foreach ( var f in t.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
		{
			var attr = f.GetCustomAttribute<BaseAttribute>();
			if ( attr == null )
			{
				throw new NotImplementedException( $"Vertex struct '{t.FullName}' is missing layout attributes on '{f.Name}'" );
			}

			//RuntimeHelpers.SizeOf, not Marshal.SizeOf, which throws on NativeAOT without interop data
			var size = System.Runtime.CompilerServices.RuntimeHelpers.SizeOf( f.FieldType.TypeHandle );
			var index = attr.Index;

			//If it is not given, the fields take their slots in the order they are written in.
			if ( index == -1 )
			{
				for ( var i = 0; i < 32; i++ )
				{
					if ( slots.Contains( $"{attr.Semantic}{i}" ) ) continue;

					index = i;
					break;
				}
			}

			var name = $"{attr.Semantic}{index}";

			if ( slots.Contains( name ) )
				throw new NotImplementedException( $"Vertex struct '{t.FullName}' contains '{name}' multiple times" );

			slots.Add( name );
			elements.Add( new Element( attr.Semantic, index, f.FieldType, offset, size ) );

			offset += size;
		}

		return elements.ToArray();
	}

	//Per-field vertex attributes so custom vertex structs can be read back; null on unknown types
	internal static VertexAttribute[] Attributes( Type t )
	{
		Element[] elements;

		try
		{
			elements = Get( t );
		}
		catch ( Exception )
		{
			//A struct with a field nobody labelled. It was drawn without a layout before this existed and still can be.
			return null;
		}

		var attributes = new VertexAttribute[elements.Length];

		for ( var i = 0; i < elements.Length; i++ )
		{
			var element = elements[i];

			var type = element.Semantic switch
			{
				"position" => VertexAttributeType.Position,
				"normal" => VertexAttributeType.Normal,
				"tangent" => VertexAttributeType.Tangent,
				"texcoord" => VertexAttributeType.TexCoord,
				"color" => VertexAttributeType.Color,
				"blendweight" => VertexAttributeType.BlendWeights,
				"blendindices" => VertexAttributeType.BlendIndices,
				_ => (VertexAttributeType?)null,
			};

			if ( type is null ) return null;

			var format = Format( element.Type );
			if ( format is null ) return null;

			attributes[i] = new VertexAttribute( type.Value, format.Value.Format,
				format.Value.Components, element.Index );
		}

		return attributes;
	}

	//What one field of a vertex is, as the renderer counts it. Null for a type this cannot measure - see Attributes for why that is not a guess.
	private static (VertexAttributeFormat Format, int Components)? Format( Type type )
	{
		if ( type == typeof( Vector4 ) ) return (VertexAttributeFormat.Float32, 4);
		if ( type == typeof( Vector3 ) ) return (VertexAttributeFormat.Float32, 3);
		if ( type == typeof( Vector2 ) ) return (VertexAttributeFormat.Float32, 2);
		if ( type == typeof( float ) ) return (VertexAttributeFormat.Float32, 1);

		if ( type == typeof( Color ) ) return (VertexAttributeFormat.Float32, 4);
		if ( type == typeof( Color32 ) ) return (VertexAttributeFormat.UInt8, 4);

		if ( type == typeof( int ) ) return (VertexAttributeFormat.SInt32, 1);
		if ( type == typeof( uint ) ) return (VertexAttributeFormat.UInt32, 1);

		return null;
	}

	//Drop every cached layout so the next Get rebuilds it. Called on hotload, where
	//a vertex struct's byte layout can change while its Type identity is preserved.
	internal static void FreeAll() => entries.Clear();

	[EditorBrowsable( EditorBrowsableState.Never )]
	public abstract class BaseAttribute : System.Attribute
	{
		internal int Index = -1;

		internal virtual string Semantic { get; }
	}

	public class Position : BaseAttribute
	{
		public Position() { }
		public Position( int index ) { Index = index; }
		internal override string Semantic => "position";
	}

	public class Normal : BaseAttribute
	{
		public Normal() { }
		public Normal( int index ) { Index = index; }
		internal override string Semantic => "normal";
	}

	public class Color : BaseAttribute
	{
		public Color() { }
		public Color( int index ) { Index = index; }
		internal override string Semantic => "color";
	}

	public class TexCoord : BaseAttribute
	{
		public TexCoord() { }
		public TexCoord( int index ) { Index = index; }
		internal override string Semantic => "texcoord";
	}

	public class Tangent : BaseAttribute
	{
		public Tangent() { }
		public Tangent( int index ) { Index = index; }
		internal override string Semantic => "tangent";
	}

	public class BlendWeight : BaseAttribute
	{
		public BlendWeight() { }
		public BlendWeight( int index ) { Index = index; }
		internal override string Semantic => "blendweight";
	}

	public class BlendIndices : BaseAttribute
	{
		public BlendIndices() { }
		public BlendIndices( int index ) { Index = index; }
		internal override string Semantic => "blendindices";
	}
}
