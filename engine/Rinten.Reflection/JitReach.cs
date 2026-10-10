using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Rinten;

//Reads metadata to tell which methods or types would load a not-yet-loaded assembly if compiled
internal sealed class JitReach : IDisposable
{
	private static readonly OperandType[] OneByte = new OperandType[256];
	private static readonly OperandType[] TwoByte = new OperandType[256];

	static JitReach()
	{
		Array.Fill( OneByte, OperandType.InlineNone );
		Array.Fill( TwoByte, OperandType.InlineNone );

		foreach ( var field in typeof( OpCodes ).GetFields( BindingFlags.Public | BindingFlags.Static ) )
		{
			if ( field.GetValue( null ) is not OpCode code ) continue;

			var table = code.Size == 1 ? OneByte : TwoByte;
			table[code.Value & 0xFF] = code.OperandType;
		}
	}

	private readonly PEReader image;
	private readonly MetadataReader metadata;
	private readonly Module module;
	private readonly HashSet<string> unloaded;
	private readonly Dictionary<EntityHandle, bool> known = new();
	private readonly Types types;

	private JitReach( Assembly assembly, HashSet<string> unloaded, PEReader image )
	{
		this.image = image;
		this.unloaded = unloaded;
		metadata = image.GetMetadataReader();
		module = assembly.ManifestModule;
		types = new Types( this );
	}

	//Null where every assembly it names is loaded already, and where there is no file to read it from - an assembly loaded from bytes, which is a package's, and names the engine and little else.
	/// <summary>What to check this assembly's methods against, or null where there is nothing to hold back.</summary>
	public static JitReach For( Assembly assembly )
	{
		if ( assembly.IsDynamic || string.IsNullOrEmpty( assembly.Location ) || !File.Exists( assembly.Location ) )
			return null;

		var loaded = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var each in AppDomain.CurrentDomain.GetAssemblies() )
		{
			if ( each.GetName().Name is { } name ) loaded.Add( name );
		}

		var unloaded = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var reference in assembly.GetReferencedAssemblies() )
		{
			if ( reference.Name is { } name && !loaded.Contains( name ) ) unloaded.Add( name );
		}

		if ( unloaded.Count == 0 ) return null;

		return new JitReach( assembly, unloaded, new PEReader( File.OpenRead( assembly.Location ), PEStreamOptions.PrefetchEntireImage ) );
	}

	/// <summary>Whether compiling this method would load an assembly that is not loaded: through its signature, its locals, what it catches, or anything its body names.</summary>
	public bool Reaches( MethodBase method )
	{
		//Inherited from another assembly, whose tokens mean nothing here.
		if ( method.Module != module ) return false;

		var definition = metadata.GetMethodDefinition( (MethodDefinitionHandle)MetadataTokens.EntityHandle( method.MetadataToken ) );

		if ( Of( definition.DecodeSignature( types, null ) ) ) return true;

		if ( definition.RelativeVirtualAddress == 0 ) return false;

		var body = image.GetMethodBody( definition.RelativeVirtualAddress );

		if ( !body.LocalSignature.IsNil && Reaches( (EntityHandle)body.LocalSignature ) ) return true;

		foreach ( var region in body.ExceptionRegions )
		{
			if ( Reaches( region.CatchType ) ) return true;
		}

		return BodyReaches( body.GetILReader() );
	}

	private bool BodyReaches( BlobReader il )
	{
		while ( il.RemainingBytes > 0 )
		{
			var first = il.ReadByte();
			var operand = first == 0xFE ? TwoByte[il.ReadByte()] : OneByte[first];

			switch ( operand )
			{
				case OperandType.InlineField:
				case OperandType.InlineMethod:
				case OperandType.InlineSig:
				case OperandType.InlineTok:
				case OperandType.InlineType:
					if ( Reaches( MetadataTokens.EntityHandle( il.ReadInt32() ) ) ) return true;
					break;

				case OperandType.InlineSwitch:
					il.Offset += 4 * il.ReadInt32();
					break;

				default:
					il.Offset += SizeOf( operand );
					break;
			}
		}

		return false;
	}

	private static int SizeOf( OperandType operand ) => operand switch
	{
		OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
		OperandType.InlineVar => 2,
		OperandType.InlineBrTarget or OperandType.InlineI or OperandType.ShortInlineR or OperandType.InlineString => 4,
		OperandType.InlineI8 or OperandType.InlineR => 8,
		_ => 0,
	};

	private bool Reaches( EntityHandle handle )
	{
		if ( handle.IsNil ) return false;
		if ( known.TryGetValue( handle, out var answer ) ) return answer;

		//A type that names itself is not a way out of the assembly.
		known[handle] = false;

		answer = handle.Kind switch
		{
			HandleKind.TypeReference => ScopeReaches( metadata.GetTypeReference( (TypeReferenceHandle)handle ).ResolutionScope ),

			//This assembly's own types were all loaded before anything was compiled - the pre-JIT asks
			//for every one of them - and what each is built from with them. Naming one loads nothing more.
			HandleKind.TypeDefinition => false,

			HandleKind.TypeSpecification => metadata.GetTypeSpecification( (TypeSpecificationHandle)handle ).DecodeSignature( types, null ),
			HandleKind.MemberReference => MemberReaches( metadata.GetMemberReference( (MemberReferenceHandle)handle ) ),
			HandleKind.MethodDefinition => Of( metadata.GetMethodDefinition( (MethodDefinitionHandle)handle ).DecodeSignature( types, null ) ),
			HandleKind.FieldDefinition => metadata.GetFieldDefinition( (FieldDefinitionHandle)handle ).DecodeSignature( types, null ),
			HandleKind.MethodSpecification => SpecificationReaches( metadata.GetMethodSpecification( (MethodSpecificationHandle)handle ) ),
			HandleKind.StandaloneSignature => StandaloneReaches( metadata.GetStandaloneSignature( (StandaloneSignatureHandle)handle ) ),
			_ => false,
		};

		known[handle] = answer;
		return answer;
	}

	/// <summary>Whether asking this field for its <see cref="FieldInfo.FieldType"/> would load an assembly that is not loaded - see ReflectionUtility.NullStaticReferencesOfType, which walks every static field of the engine at shutdown and would otherwise map the compiler for a cached lambda of type <c>Action&lt;Compiler&gt;</c>.</summary>
	public bool FieldReaches( FieldInfo field )
	{
		if ( field.Module != module ) return false;

		return Reaches( MetadataTokens.EntityHandle( field.MetadataToken ) );
	}

	/// <summary>Whether describing this type like System.Text.Json does would load an unloaded assembly</summary>
	public bool DescribingReaches( Type type )
	{
		if ( type.Module != module ) return false;

		var definition = metadata.GetTypeDefinition( (TypeDefinitionHandle)MetadataTokens.EntityHandle( type.MetadataToken ) );

		if ( AttributesReach( definition.GetCustomAttributes() ) ) return true;

		foreach ( var handle in definition.GetProperties() )
		{
			var property = metadata.GetPropertyDefinition( handle );

			if ( Of( property.DecodeSignature( types, null ) ) ) return true;
			if ( AttributesReach( property.GetCustomAttributes() ) ) return true;
		}

		foreach ( var handle in definition.GetFields() )
		{
			if ( AttributesReach( metadata.GetFieldDefinition( handle ).GetCustomAttributes() ) ) return true;
		}

		foreach ( var handle in definition.GetMethods() )
		{
			var method = metadata.GetMethodDefinition( handle );

			if ( ( method.Attributes & MethodAttributes.MemberAccessMask ) != MethodAttributes.Public ) continue;
			if ( ( method.Attributes & MethodAttributes.Static ) != 0 ) continue;
			if ( !metadata.StringComparer.Equals( method.Name, ".ctor" ) ) continue;

			if ( Of( method.DecodeSignature( types, null ) ) ) return true;
		}

		return false;
	}

	private bool AttributesReach( CustomAttributeHandleCollection attributes )
	{
		foreach ( var handle in attributes )
		{
			if ( Reaches( metadata.GetCustomAttribute( handle ).Constructor ) ) return true;
		}

		return false;
	}

	private bool ScopeReaches( EntityHandle scope ) => scope.Kind switch
	{
		HandleKind.AssemblyReference => unloaded.Contains( metadata.GetString( metadata.GetAssemblyReference( (AssemblyReferenceHandle)scope ).Name ) ),
		HandleKind.TypeReference => Reaches( scope ),
		_ => false,
	};

	private bool MemberReaches( MemberReference member )
	{
		if ( Reaches( member.Parent ) ) return true;

		return member.GetKind() == MemberReferenceKind.Method
			? Of( member.DecodeMethodSignature( types, null ) )
			: member.DecodeFieldSignature( types, null );
	}

	private bool SpecificationReaches( MethodSpecification specification )
		=> Reaches( specification.Method ) || specification.DecodeSignature( types, null ).Contains( true );

	private bool StandaloneReaches( StandaloneSignature signature )
		=> signature.GetKind() == StandaloneSignatureKind.LocalVariables
			? signature.DecodeLocalSignature( types, null ).Contains( true )
			: Of( signature.DecodeMethodSignature( types, null ) );

	private static bool Of( MethodSignature<bool> signature )
		=> signature.ReturnType || signature.ParameterTypes.Contains( true );

	public void Dispose() => image.Dispose();

	//Reads a signature as the one answer wanted of it: does any type in it come from an assembly that is not loaded.
	private sealed class Types : ISignatureTypeProvider<bool, object>
	{
		private readonly JitReach owner;

		public Types( JitReach owner ) => this.owner = owner;

		public bool GetPrimitiveType( PrimitiveTypeCode typeCode ) => false;
		public bool GetTypeFromDefinition( MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind ) => owner.Reaches( handle );
		public bool GetTypeFromReference( MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind ) => owner.Reaches( handle );
		public bool GetTypeFromSpecification( MetadataReader reader, object genericContext, TypeSpecificationHandle handle, byte rawTypeKind ) => owner.Reaches( handle );
		public bool GetSZArrayType( bool elementType ) => elementType;
		public bool GetArrayType( bool elementType, ArrayShape shape ) => elementType;
		public bool GetByReferenceType( bool elementType ) => elementType;
		public bool GetPointerType( bool elementType ) => elementType;
		public bool GetPinnedType( bool elementType ) => elementType;
		public bool GetGenericInstantiation( bool genericType, ImmutableArray<bool> typeArguments ) => genericType || typeArguments.Contains( true );
		public bool GetGenericMethodParameter( object genericContext, int index ) => false;
		public bool GetGenericTypeParameter( object genericContext, int index ) => false;
		public bool GetFunctionPointerType( MethodSignature<bool> signature ) => Of( signature );
		public bool GetModifiedType( bool modifier, bool unmodifiedType, bool isRequired ) => modifier || unmodifiedType;
	}
}
