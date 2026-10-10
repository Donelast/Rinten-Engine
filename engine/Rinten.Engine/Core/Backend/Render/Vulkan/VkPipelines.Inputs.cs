using Silk.NET.Vulkan;

namespace Rinten;

//Drops vertex attributes a vertex stage never reads, which validation would warn about
internal static unsafe partial class VkPipelines
{
	//Bitmask of input locations the vertex entry reads; all bits if a multi-location input or unreadable
	private static ulong VertexInputsRead( byte* spirv, int size, string entry )
	{
		const ulong Everything = ulong.MaxValue;

		if ( spirv is null || size < 20 || ( size & 3 ) != 0 || entry is null ) return Everything;

		var words = (uint*)spirv;
		var count = size / 4;

		if ( words[0] != 0x07230203u ) return Everything;

		var bound = (int)words[3];
		if ( bound <= 0 || bound > 1 << 22 ) return Everything;

		//What each id is, as far as this needs to know.
		var location = new int[bound];
		var inputPointee = new int[bound];
		var pointee = new int[bound];
		var simple = new bool[bound];
		var interfaceIds = new List<int>();

		System.Array.Fill( location, -1 );

		var entryFound = false;

		for ( var at = 5; at < count; )
		{
			var op = words[at] & 0xFFFF;
			var length = (int)( words[at] >> 16 );

			if ( length == 0 || at + length > count ) return Everything;

			switch ( op )
			{
				case 15: //OpEntryPoint: model, id, name..., interface ids...
				{
					if ( words[at + 1] != 0 ) break; //Vertex only

					var name = ReadString( words, at + 3, at + length, out var after );
					if ( name != entry ) break;

					entryFound = true;
					for ( var i = after; i < at + length; ++i ) interfaceIds.Add( (int)words[i] );
					break;
				}

				case 71: //OpDecorate: target, decoration, operands
				{
					if ( length >= 4 && words[at + 2] == 30 ) //Location
					{
						var target = (int)words[at + 1];
						if ( target < bound ) location[target] = (int)words[at + 3];
					}

					break;
				}

				case 21: //OpTypeInt: id, width
				case 22: //OpTypeFloat: id, width
				{
					var id = (int)words[at + 1];
					if ( id < bound ) simple[id] = words[at + 2] <= 32;
					break;
				}

				case 23: //OpTypeVector: id, component, count
				{
					var id = (int)words[at + 1];
					var component = (int)words[at + 2];
					if ( id < bound && component < bound ) simple[id] = simple[component];
					break;
				}

				case 32: //OpTypePointer: id, storage class, type
				{
					var id = (int)words[at + 1];
					if ( id < bound ) pointee[id] = (int)words[at + 3];
					break;
				}

				case 59: //OpVariable: result type, id, storage class
				{
					var type = (int)words[at + 1];
					var id = (int)words[at + 2];

					if ( words[at + 3] == 1 && id < bound && type < bound ) //Input
						inputPointee[id] = pointee[type] == 0 ? -1 : pointee[type];

					break;
				}
			}

			at += length;
		}

		if ( !entryFound ) return Everything;

		var read = 0ul;

		foreach ( var id in interfaceIds )
		{
			if ( id >= bound || inputPointee[id] == 0 ) continue; //not an input

			//Built-ins - the vertex index, the instance - carry no location.
			var at = location[id];
			if ( at < 0 ) continue;

			var type = inputPointee[id];
			if ( type < 0 || type >= bound || !simple[type] || at >= 64 ) return Everything;

			read |= 1ul << at;
		}

		return read;
	}

	//A SPIR-V literal string, and the word after it.
	private static string ReadString( uint* words, int from, int end, out int after )
	{
		var bytes = new List<byte>( 32 );

		for ( var i = from; i < end; ++i )
		{
			var word = words[i];

			for ( var b = 0; b < 4; ++b )
			{
				var c = (byte)( word >> ( b * 8 ) );

				if ( c == 0 )
				{
					after = i + 1;
					return System.Text.Encoding.UTF8.GetString( bytes.ToArray() );
				}

				bytes.Add( c );
			}
		}

		after = end;
		return null;
	}

	//Drops the attributes whose location is not in read, keeping the rest in order. Returns how many are left.
	private static uint KeepRead( VertexInputAttributeDescription* attributes, uint count, ulong read )
	{
		if ( read == ulong.MaxValue ) return count;

		var kept = 0u;

		for ( var i = 0u; i < count; ++i )
		{
			var at = attributes[i].Location;
			if ( at < 64 && ( read & ( 1ul << (int)at ) ) == 0 ) continue;

			attributes[kept++] = attributes[i];
		}

		return kept;
	}
}
