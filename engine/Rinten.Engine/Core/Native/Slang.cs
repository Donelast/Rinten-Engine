using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rinten.Native.Slang;

//Slang's COM-style API called through vtable slots in slang.h order; only used slots are bound
internal static unsafe partial class Slang
{
	private const string Library = "slang";

	/// <summary>SLANG_API_VERSION. Checked inside the call against the binary.</summary>
	public const nint SLANG_API_VERSION = 0;

	/// <summary>Create a global session, with the built-in core module.</summary>
	[LibraryImport( Library )]
	public static partial int slang_createGlobalSession( nint apiVersion, IGlobalSession** outGlobalSession );

	/// <summary>SLANG_FAILED: a negative result is a failure, as with an HRESULT.</summary>
	public static bool SLANG_FAILED( int result ) => result < 0;
}

/// <summary>SlangCompileTarget: the language a target produces.</summary>
public enum SlangCompileTarget
{
	SLANG_TARGET_UNKNOWN = 0,
	SLANG_SPIRV = 6,
	SLANG_WGSL = 28,
}

/// <summary>SlangStage: which stage of the pipeline an entry point is.</summary>
public enum SlangStage : uint
{
	SLANG_STAGE_NONE = 0,
	SLANG_STAGE_VERTEX = 1,
	SLANG_STAGE_FRAGMENT = 5,
	SLANG_STAGE_COMPUTE = 6,
	SLANG_STAGE_MESH = 13,
}

/// <summary>slang::CompilerOptionName: the ones the engine sets.</summary>
public enum CompilerOptionName
{
	VulkanUseEntryPointName = 52,
}

/// <summary>slang::CompilerOptionValueKind.</summary>
public enum CompilerOptionValueKind
{
	Int = 0,
	String = 1,
}

/// <summary>slang::CompilerOptionValue.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct CompilerOptionValue
{
	public CompilerOptionValueKind kind;
	public int intValue0;
	public int intValue1;
	public byte* stringValue0;
	public byte* stringValue1;
}

/// <summary>slang::CompilerOptionEntry.</summary>
[StructLayout( LayoutKind.Sequential )]
public struct CompilerOptionEntry
{
	public CompilerOptionName name;
	public CompilerOptionValue value;
}

/// <summary>slang::PreprocessorMacroDesc: a #define the session starts with.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct PreprocessorMacroDesc
{
	public byte* name;
	public byte* value;
}

/// <summary>slang::TargetDesc. The C++ default-initialises every field; the same values are set by <see cref="Default"/> here, because a zeroed struct is not what the header means by empty.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct TargetDesc
{
	public nuint structureSize;
	public SlangCompileTarget format;
	public uint profile;
	public uint flags;
	public uint floatingPointMode;
	public uint lineDirectiveMode;
	public byte forceGLSLScalarBufferLayout;
	public CompilerOptionEntry* compilerOptionEntries;
	public uint compilerOptionEntryCount;

	/// <summary>SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY, the header's kDefaultTargetFlags.</summary>
	public const uint kDefaultTargetFlags = 1u << 10;

	/// <summary>What <c>slang::TargetDesc{}</c> is in C++.</summary>
	public static TargetDesc Default => new()
	{
		structureSize = (nuint)sizeof( TargetDesc ),
		flags = kDefaultTargetFlags,
	};
}

/// <summary>slang::SessionDesc. See <see cref="TargetDesc"/> about the defaults.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct SessionDesc
{
	public nuint structureSize;
	public TargetDesc* targets;
	public nint targetCount;
	public uint flags;
	public uint defaultMatrixLayoutMode;
	public byte** searchPaths;
	public nint searchPathCount;
	public PreprocessorMacroDesc* preprocessorMacros;
	public nint preprocessorMacroCount;
	public void* fileSystem;
	public byte enableEffectAnnotations;
	public byte allowGLSLSyntax;
	public CompilerOptionEntry* compilerOptionEntries;
	public uint compilerOptionEntryCount;
	public byte skipSPIRVValidation;

	/// <summary>SLANG_MATRIX_LAYOUT_ROW_MAJOR, the header's default.</summary>
	public const uint SLANG_MATRIX_LAYOUT_ROW_MAJOR = 1;

	/// <summary>What <c>slang::SessionDesc{}</c> is in C++.</summary>
	public static SessionDesc Default => new()
	{
		structureSize = (nuint)sizeof( SessionDesc ),
		defaultMatrixLayoutMode = SLANG_MATRIX_LAYOUT_ROW_MAJOR,
	};
}

/// <summary>ISlangUnknown: the three slots every Slang interface starts with.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct ISlangUnknown
{
	public void** vtbl;

	/// <summary>Slot 2.</summary>
	public uint release()
		=> ((delegate* unmanaged[MemberFunction]<ISlangUnknown*, uint>)vtbl[2])( (ISlangUnknown*)Unsafe.AsPointer( ref this ) );
}

/// <summary>ISlangBlob: bytes with a size. Diagnostics and compiled code come back as one.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct ISlangBlob
{
	public void** vtbl;

	public uint release()
		=> ((delegate* unmanaged[MemberFunction]<ISlangBlob*, uint>)vtbl[2])( (ISlangBlob*)Unsafe.AsPointer( ref this ) );

	/// <summary>Slot 3.</summary>
	public void* getBufferPointer()
		=> ((delegate* unmanaged[MemberFunction]<ISlangBlob*, void*>)vtbl[3])( (ISlangBlob*)Unsafe.AsPointer( ref this ) );

	/// <summary>Slot 4.</summary>
	public nuint getBufferSize()
		=> ((delegate* unmanaged[MemberFunction]<ISlangBlob*, nuint>)vtbl[4])( (ISlangBlob*)Unsafe.AsPointer( ref this ) );
}

/// <summary>slang::IGlobalSession: one per process, or per thread here - see CoreShaderCompiler.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct IGlobalSession
{
	public void** vtbl;

	public uint release()
		=> ((delegate* unmanaged[MemberFunction]<IGlobalSession*, uint>)vtbl[2])( (IGlobalSession*)Unsafe.AsPointer( ref this ) );

	/// <summary>Slot 3: createSession( SessionDesc const&amp;, ISession** ).</summary>
	public int createSession( SessionDesc* desc, ISession** outSession )
		=> ((delegate* unmanaged[MemberFunction]<IGlobalSession*, SessionDesc*, ISession**, int>)vtbl[3])(
			(IGlobalSession*)Unsafe.AsPointer( ref this ), desc, outSession );

	/// <summary>Slot 4: findProfile( char const* ).</summary>
	public uint findProfile( byte* name )
		=> ((delegate* unmanaged[MemberFunction]<IGlobalSession*, byte*, uint>)vtbl[4])(
			(IGlobalSession*)Unsafe.AsPointer( ref this ), name );

	/// <summary>Slot 8: getBuildTagString(). After setDownstreamCompilerPath (5), setDownstreamCompilerPrelude (6) and getDownstreamCompilerPrelude (7).</summary>
	public byte* getBuildTagString()
		=> ((delegate* unmanaged[MemberFunction]<IGlobalSession*, byte*>)vtbl[8])(
			(IGlobalSession*)Unsafe.AsPointer( ref this ) );
}

/// <summary>slang::ISession: a scope for loaded modules, made for one compile.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct ISession
{
	public void** vtbl;

	public uint release()
		=> ((delegate* unmanaged[MemberFunction]<ISession*, uint>)vtbl[2])( (ISession*)Unsafe.AsPointer( ref this ) );

	/// <summary>Slot 6: createCompositeComponentType( IComponentType* const*, SlangInt, IComponentType**, ISlangBlob** ). After getGlobalSession (3), loadModule (4), loadModuleFromSource (5).</summary>
	public int createCompositeComponentType( IComponentType** componentTypes, nint componentTypeCount,
		IComponentType** outCompositeComponentType, ISlangBlob** outDiagnostics )
		=> ((delegate* unmanaged[MemberFunction]<ISession*, IComponentType**, nint, IComponentType**, ISlangBlob**, int>)vtbl[6])(
			(ISession*)Unsafe.AsPointer( ref this ), componentTypes, componentTypeCount, outCompositeComponentType, outDiagnostics );

	/// <summary>Slot 20: loadModuleFromSourceString; slots 7-19 are other ISession methods, see slang.h</summary>
	public IModule* loadModuleFromSourceString( byte* moduleName, byte* path, byte* source, ISlangBlob** outDiagnostics )
		=> ((delegate* unmanaged[MemberFunction]<ISession*, byte*, byte*, byte*, ISlangBlob**, IModule*>)vtbl[20])(
			(ISession*)Unsafe.AsPointer( ref this ), moduleName, path, source, outDiagnostics );
}

/// <summary>slang::IComponentType: a module, an entry point, or several linked together.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct IComponentType
{
	public void** vtbl;

	public uint release()
		=> ((delegate* unmanaged[MemberFunction]<IComponentType*, uint>)vtbl[2])( (IComponentType*)Unsafe.AsPointer( ref this ) );

	/// <summary>Slot 6: getEntryPointCode( SlangInt entryPointIndex, SlangInt targetIndex, IBlob** outCode, IBlob** outDiagnostics ). After getSession (3), getLayout (4), getSpecializationParamCount (5).</summary>
	public int getEntryPointCode( nint entryPointIndex, nint targetIndex, ISlangBlob** outCode, ISlangBlob** outDiagnostics )
		=> ((delegate* unmanaged[MemberFunction]<IComponentType*, nint, nint, ISlangBlob**, ISlangBlob**, int>)vtbl[6])(
			(IComponentType*)Unsafe.AsPointer( ref this ), entryPointIndex, targetIndex, outCode, outDiagnostics );

	/// <summary>Slot 10: link( IComponentType** outLinked, ISlangBlob** outDiagnostics ). After getResultAsFileSystem (7), getEntryPointHash (8), specialize (9).</summary>
	public int link( IComponentType** outLinkedComponentType, ISlangBlob** outDiagnostics )
		=> ((delegate* unmanaged[MemberFunction]<IComponentType*, IComponentType**, ISlangBlob**, int>)vtbl[10])(
			(IComponentType*)Unsafe.AsPointer( ref this ), outLinkedComponentType, outDiagnostics );

	/// <summary>How many slots IComponentType has in all, which is where IModule's and IEntryPoint's own begin: the ten above, then getEntryPointHostCallable, renameEntryPoint, linkWithOptions, getTargetCode, getTargetMetadata, getEntryPointMetadata.</summary>
	public const int SlotCount = 17;
}

/// <summary>slang::IEntryPoint. Only ever passed on as an IComponentType.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct IEntryPoint
{
	public void** vtbl;

	public uint release()
		=> ((delegate* unmanaged[MemberFunction]<IEntryPoint*, uint>)vtbl[2])( (IEntryPoint*)Unsafe.AsPointer( ref this ) );
}

/// <summary>slang::IModule: a loaded source file, and the entry points in it.</summary>
[StructLayout( LayoutKind.Sequential )]
public unsafe struct IModule
{
	public void** vtbl;

	/// <summary>Slot 17: findEntryPointByName( char const*, IEntryPoint** ) - the first of IModule's own.</summary>
	public int findEntryPointByName( byte* name, IEntryPoint** outEntryPoint )
		=> ((delegate* unmanaged[MemberFunction]<IModule*, byte*, IEntryPoint**, int>)vtbl[IComponentType.SlotCount + 0])(
			(IModule*)Unsafe.AsPointer( ref this ), name, outEntryPoint );

	/// <summary>Slot 25: findAndCheckEntryPoint( char const*, SlangStage, IEntryPoint**, ISlangBlob** ). After getDefinedEntryPointCount (18), getDefinedEntryPoint (19), serialize (20), writeToFile (21), getName (22), getFilePath (23), getUniqueIdentity (24).</summary>
	public int findAndCheckEntryPoint( byte* name, SlangStage stage, IEntryPoint** outEntryPoint, ISlangBlob** outDiagnostics )
		=> ((delegate* unmanaged[MemberFunction]<IModule*, byte*, SlangStage, IEntryPoint**, ISlangBlob**, int>)vtbl[IComponentType.SlotCount + 8])(
			(IModule*)Unsafe.AsPointer( ref this ), name, stage, outEntryPoint, outDiagnostics );
}
