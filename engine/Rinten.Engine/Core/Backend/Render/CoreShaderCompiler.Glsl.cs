using System.Runtime.InteropServices;
using Rinten.Native.SpirvCross;

namespace Rinten;

unsafe partial class CoreShaderCompiler
{
	//Portable SPIR-V as GLSL ES 3.00 for WebGL2; bindings are encoded in names (B/T{group}_{binding})
	//Varyings are named by location; position is flipped to GL's Y-up and depth remapped to -1..1
	private static byte[] Glsl( byte[] spirv, Stage stage, int views )
	{
		spirv = WithoutDrawParameters( spirv );

		nint context = 0;

		try
		{
			if ( Spvc.spvc_context_create( &context ) != Spvc.Success )
			{
				lastError = "SPIRV-Cross would not make a context";
				return null;
			}
		}
		catch ( Exception e ) when ( e is DllNotFoundException or EntryPointNotFoundException )
		{
			lastError = "SPIRV-Cross could not be loaded - GLSL is written by the editor, which has it";
			return null;
		}

		try
		{
			nint parsed, compiler, options, resources;

			fixed ( byte* words = spirv )
			{
				if ( Spvc.spvc_context_parse_spirv( context, (uint*)words, (nuint)( spirv.Length / 4 ), &parsed ) != Spvc.Success )
					return Failed( context );
			}

			if ( Spvc.spvc_context_create_compiler( context, SpvcBackend.Glsl, parsed, CaptureMode.TakeOwnership, &compiler ) != Spvc.Success
				|| Spvc.spvc_compiler_create_compiler_options( compiler, &options ) != Spvc.Success )
				return Failed( context );

			Spvc.spvc_compiler_options_set_uint( options, Option.GlslVersion, 300 );
			Spvc.spvc_compiler_options_set_bool( options, Option.GlslEs, 1 );
			Spvc.spvc_compiler_options_set_bool( options, Option.GlslVulkanSemantics, 0 );
			Spvc.spvc_compiler_options_set_bool( options, Option.GlslEsDefaultFloatPrecisionHighp, 1 );
			Spvc.spvc_compiler_options_set_bool( options, Option.GlslEsDefaultIntPrecisionHighp, 1 );
			Spvc.spvc_compiler_options_set_bool( options, Option.GlslEmitPushConstantAsUniformBuffer, 1 );
			Spvc.spvc_compiler_options_set_bool( options, Option.FlipVertexY, 1 );
			Spvc.spvc_compiler_options_set_bool( options, Option.FixupDepthConvention, 1 );

			//Vertex stage only: SPIRV-Cross rejects multiview elsewhere; the pixel stage gets it as a varying
			if ( views > 1 && stage == Stage.Vertex )
				Spvc.spvc_compiler_options_set_uint( options, Option.GlslOvrMultiviewViewCount, (uint)views );

			if ( Spvc.spvc_compiler_install_compiler_options( compiler, options ) != Spvc.Success )
				return Failed( context );

			//A name the shader gave that GLSL keeps for itself - a local called flat, which the sky and the sprites both have
			//- is written as it is, and the page's compiler stops at it. SPIRV-Cross knows most of them; these it lets through.
			foreach ( var (id, name) in Named( spirv ) )
			{
				if ( GlslKeeps.Contains( name ) ) SetName( compiler, id, name + "_" );
			}

			//GLSL samples a picture and its sampler as one thing. A picture read with no sampler at all - a Load - is given one that does nothing.
			uint dummy;
			if ( Spvc.spvc_compiler_build_dummy_sampler_for_combined_images( compiler, &dummy ) != Spvc.Success
				|| Spvc.spvc_compiler_build_combined_image_samplers( compiler ) != Spvc.Success
				|| Spvc.spvc_compiler_create_shader_resources( compiler, &resources ) != Spvc.Success )
				return Failed( context );

			var names = new HashSet<string>();

			foreach ( var block in Resources( resources, ResourceType.UniformBuffer ) )
				NameBlock( compiler, block, Place( compiler, block.Id ) );

			foreach ( var block in Resources( resources, ResourceType.PushConstant ) )
				NameBlock( compiler, block, (0, BrowserDevice.PushBinding) );

			//The samplers made just now are named after the picture they read; the ones the SPIR-V already had carry their own place.
			CombinedSampler* combined;
			nuint count;

			if ( Spvc.spvc_compiler_get_combined_image_samplers( compiler, &combined, &count ) != Spvc.Success )
				count = 0;

			var made = new HashSet<uint>();

			for ( nuint i = 0; i < count; i++ )
			{
				made.Add( combined[i].CombinedId );
				NameSampler( compiler, combined[i].CombinedId, Place( compiler, combined[i].ImageId ), names );
			}

			foreach ( var picture in Resources( resources, ResourceType.SampledImage ) )
			{
				if ( !made.Contains( picture.Id ) )
					NameSampler( compiler, picture.Id, Place( compiler, picture.Id ), names );
			}

			//What passes from one stage to the next, by location.
			var passed = stage == Stage.Vertex ? ResourceType.StageOutput : stage == Stage.Pixel ? ResourceType.StageInput : (ResourceType)0;

			if ( passed != 0 )
			{
				foreach ( var varying in Resources( resources, passed ) )
				{
					var location = Spvc.spvc_compiler_get_decoration( compiler, varying.Id, Decoration.Location );
					SetName( compiler, varying.Id, $"v{location}" );
				}
			}

			byte* text;
			if ( Spvc.spvc_compiler_compile( compiler, &text ) != Spvc.Success || text is null )
				return Failed( context );

			var length = 0;
			while ( text[length] != 0 ) length++;

			return new ReadOnlySpan<byte>( text, length ).ToArray();
		}
		finally
		{
			Spvc.spvc_context_destroy( context );
		}
	}

	//The SPIR-V words this reads and writes.
	private const int OpNop = 0;
	private const int OpExtension = 10;
	private const int OpEntryPoint = 15;
	private const int OpCapability = 17;
	private const int OpConstant = 43;
	private const int OpFunction = 54;
	private const int OpLoad = 61;
	private const int OpDecorate = 71;
	private const int OpCopyObject = 83;
	private const int DecorationBuiltIn = 11;
	private const int BuiltInBaseVertex = 4424;
	private const int BuiltInBaseInstance = 4425;
	private const int CapabilityDrawParameters = 4427;

	//Replaces base vertex/instance reads with zero: WebGL passes the base vertex as the vertex offset
	private static byte[] WithoutDrawParameters( byte[] spirv )
	{
		var words = new List<uint>( MemoryMarshal.Cast<byte, uint>( spirv ).ToArray() );
		if ( words.Count < 5 ) return spirv;

		var drawn = new HashSet<uint>();

		for ( var at = 5; at < words.Count; at += (int)( words[at] >> 16 ) )
		{
			if ( ( words[at] & 0xffff ) == OpDecorate && words[at + 2] == DecorationBuiltIn
				&& words[at + 3] is BuiltInBaseVertex or BuiltInBaseInstance )
				drawn.Add( words[at + 1] );

			if ( words[at] >> 16 == 0 ) return spirv;
		}

		if ( drawn.Count == 0 ) return spirv;

		//Nought, of each type it is loaded as - made once, before the first function.
		var zeros = new Dictionary<uint, uint>();
		var firstFunction = -1;

		for ( var at = 5; at < words.Count; at += (int)( words[at] >> 16 ) )
		{
			var op = words[at] & 0xffff;

			if ( op == OpFunction && firstFunction < 0 ) firstFunction = at;
			if ( op == OpLoad && drawn.Contains( words[at + 3] ) && !zeros.ContainsKey( words[at + 1] ) )
				zeros[words[at + 1]] = words[3]++;
		}

		for ( var at = 5; at < words.Count; )
		{
			var count = (int)( words[at] >> 16 );
			var op = words[at] & 0xffff;

			if ( op == OpLoad && drawn.Contains( words[at + 3] ) )
			{
				//The load becomes a copy of the constant, in the same words - a load with a memory operand has one to spare, which becomes a nop.
				words[at] = ( 4u << 16 ) | OpCopyObject;
				words[at + 3] = zeros[words[at + 1]];

				for ( var i = 4; i < count; i++ )
					words[at + i] = ( 1u << 16 ) | OpNop;
			}
			else if ( op == OpEntryPoint )
			{
				//The interface, less the two. The name before it is a string of words ending in a zero byte; everything after it is an id.
				var name = at + 3;
				while ( ( words[name] >> 24 ) != 0 ) name++;

				var kept = new List<uint>();
				for ( var i = at + 1; i <= name; i++ ) kept.Add( words[i] );
				for ( var i = name + 1; i < at + count; i++ )
					if ( !drawn.Contains( words[i] ) ) kept.Add( words[i] );

				words.RemoveRange( at, count );
				words.Insert( at, ( (uint)( kept.Count + 1 ) << 16 ) | OpEntryPoint );
				words.InsertRange( at + 1, kept );

				if ( firstFunction > at ) firstFunction -= count - ( kept.Count + 1 );
				count = kept.Count + 1;
			}
			else if ( ( op == OpCapability && words[at + 1] == CapabilityDrawParameters )
				|| ( op == OpExtension && ExtensionName( words, at, count ) == "SPV_KHR_shader_draw_parameters" ) )
			{
				words.RemoveRange( at, count );
				if ( firstFunction > at ) firstFunction -= count;
				continue;
			}

			at += count;
		}

		var constants = new List<uint>();
		foreach ( var (type, id) in zeros )
			constants.AddRange( [( 4u << 16 ) | OpConstant, type, id, 0u] );

		words.InsertRange( firstFunction, constants );

		return MemoryMarshal.AsBytes( CollectionsMarshal.AsSpan( words ) ).ToArray();
	}

	//The words GLSL ES keeps that SPIRV-Cross writes as names anyway.
	private static readonly HashSet<string> GlslKeeps =
	[
		"flat", "smooth", "centroid", "invariant", "precision", "lowp", "mediump", "highp",
		"sample", "patch", "layout", "filter", "active", "common", "partition", "input", "output",
		"superp", "noperspective", "subroutine", "attribute", "varying", "coherent", "restrict",
		"readonly", "writeonly", "resource", "shared", "buffer", "texture", "sampler",
	];

	//Every OpName in a module: the id, and the name it was given.
	private static List<(uint Id, string Name)> Named( byte[] spirv )
	{
		var words = MemoryMarshal.Cast<byte, uint>( spirv );
		var named = new List<(uint, string)>();

		for ( var at = 5; at < words.Length; )
		{
			var count = (int)( words[at] >> 16 );
			if ( count == 0 ) break;

			if ( ( words[at] & 0xffff ) == OpName && count > 2 )
			{
				var bytes = MemoryMarshal.AsBytes( words.Slice( at + 2, count - 2 ) );
				var end = bytes.IndexOf( (byte)0 );

				named.Add( (words[at + 1], System.Text.Encoding.UTF8.GetString( end < 0 ? bytes : bytes[..end] )) );
			}

			at += count;
		}

		return named;
	}

	private const int OpName = 5;

	private static string ExtensionName( List<uint> words, int at, int count )
	{
		var bytes = new byte[( count - 1 ) * 4];

		for ( var i = 1; i < count; i++ )
			BitConverter.GetBytes( words[at + i] ).CopyTo( bytes, ( i - 1 ) * 4 );

		return System.Text.Encoding.UTF8.GetString( bytes ).TrimEnd( '\0' );
	}

	private static byte[] Failed( nint context )
	{
		var said = Spvc.spvc_context_get_last_error_string( context );
		lastError = said is null ? "SPIRV-Cross would not write GLSL" : "GLSL: " + Marshal.PtrToStringUTF8( (nint)said );
		return null;
	}

	private static ReadOnlySpan<ReflectedResource> Resources( nint resources, ResourceType type )
	{
		ReflectedResource* list;
		nuint count;

		if ( Spvc.spvc_resources_get_resource_list_for_type( resources, type, &list, &count ) != Spvc.Success || list is null )
			return ReadOnlySpan<ReflectedResource>.Empty;

		return new ReadOnlySpan<ReflectedResource>( list, (int)count );
	}

	private static (uint Group, uint Binding) Place( nint compiler, uint id )
		=> (Spvc.spvc_compiler_get_decoration( compiler, id, Decoration.DescriptorSet ),
			Spvc.spvc_compiler_get_decoration( compiler, id, Decoration.Binding ));

	//A uniform block, by where it is bound. The block's own name is what a program is asked about;
	//the instance is named too, so the two stages declare the same thing in the same words.
	private static void NameBlock( nint compiler, ReflectedResource block, (uint Group, uint Binding) at )
	{
		SetName( compiler, block.BaseTypeId, $"B{at.Group}_{at.Binding}" );
		SetName( compiler, block.Id, $"b{at.Group}_{at.Binding}" );
	}

	//A sampler, by where its picture is bound. The same picture sampled two ways is two samplers, numbered after the first.
	private static void NameSampler( nint compiler, uint id, (uint Group, uint Binding) at, HashSet<string> names )
	{
		var name = $"T{at.Group}_{at.Binding}";

		for ( var n = 1; !names.Add( name ); n++ )
			name = $"T{at.Group}_{at.Binding}_{n}";

		SetName( compiler, id, name );
	}

	private static void SetName( nint compiler, uint id, string name )
	{
		var bytes = System.Text.Encoding.UTF8.GetBytes( name + "\0" );

		fixed ( byte* p = bytes )
			Spvc.spvc_compiler_set_name( compiler, id, p );
	}
}
