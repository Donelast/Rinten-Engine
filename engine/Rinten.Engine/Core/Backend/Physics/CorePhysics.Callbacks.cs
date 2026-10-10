using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rinten.Native.Box3D;

namespace Rinten;

//Box3D callbacks; wasm32 passes structs by pointer, so the browser gets entries declared that way
internal static unsafe partial class CorePhysics
{
	//True where a struct argument arrives as a pointer to a copy.
	private static bool StructsByPointer => OperatingSystem.IsBrowser();

	//Collision rules

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B3Bool OnCustomFilterByPointer( b3ShapeId* a, b3ShapeId* b, void* context )
		=> ResolveRule( (byte*)context, *a, *b ) != Ignore;

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B3Bool OnPreSolveByPointer( b3ShapeId* a, b3ShapeId* b, b3Vec3* point, b3Vec3* normal, void* context )
		=> ResolveRule( (byte*)context, *a, *b ) != Trigger;

	private static delegate* unmanaged[Cdecl]<b3ShapeId, b3ShapeId, void*, B3Bool> CustomFilterEntry()
		=> StructsByPointer
			? (delegate* unmanaged[Cdecl]<b3ShapeId, b3ShapeId, void*, B3Bool>)(void*)(delegate* unmanaged[Cdecl]<b3ShapeId*, b3ShapeId*, void*, B3Bool>)&OnCustomFilterByPointer
			: &OnCustomFilter;

	private static delegate* unmanaged[Cdecl]<b3ShapeId, b3ShapeId, b3Vec3, b3Vec3, void*, B3Bool> PreSolveEntry()
		=> StructsByPointer
			? (delegate* unmanaged[Cdecl]<b3ShapeId, b3ShapeId, b3Vec3, b3Vec3, void*, B3Bool>)(void*)(delegate* unmanaged[Cdecl]<b3ShapeId*, b3ShapeId*, b3Vec3*, b3Vec3*, void*, B3Bool>)&OnPreSolveByPointer
			: &OnPreSolve;

	//Queries

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static float OnTraceHitByPointer( b3ShapeId* shapeId, b3Vec3* point, b3Vec3* normal, float fraction,
		ulong userMaterialId, int triangleIndex, int childIndex, void* context )
		=> CollectTraceHit( *shapeId, *point, *normal, fraction, userMaterialId, triangleIndex, childIndex, context );

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B3Bool OnOverlapByPointer( b3ShapeId* shapeId, void* context )
		=> CollectOverlap( *shapeId, context );

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B3Bool OnQueryTriangleByPointer( b3Vec3* a, b3Vec3* b, b3Vec3* c, int index, void* context )
		=> WriteTriangle( *a, *b, *c, index, context );

	private static delegate* unmanaged[Cdecl]<b3ShapeId, b3Vec3, b3Vec3, float, ulong, int, int, void*, float> TraceHitEntry()
		=> StructsByPointer
			? (delegate* unmanaged[Cdecl]<b3ShapeId, b3Vec3, b3Vec3, float, ulong, int, int, void*, float>)(void*)(delegate* unmanaged[Cdecl]<b3ShapeId*, b3Vec3*, b3Vec3*, float, ulong, int, int, void*, float>)&OnTraceHitByPointer
			: &OnTraceHit;

	private static delegate* unmanaged[Cdecl]<b3ShapeId, void*, B3Bool> OverlapEntry()
		=> StructsByPointer
			? (delegate* unmanaged[Cdecl]<b3ShapeId, void*, B3Bool>)(void*)(delegate* unmanaged[Cdecl]<b3ShapeId*, void*, B3Bool>)&OnOverlapByPointer
			: &OnOverlap;

	private static delegate* unmanaged[Cdecl]<b3Vec3, b3Vec3, b3Vec3, int, void*, B3Bool> QueryTriangleEntry()
		=> StructsByPointer
			? (delegate* unmanaged[Cdecl]<b3Vec3, b3Vec3, b3Vec3, int, void*, B3Bool>)(void*)(delegate* unmanaged[Cdecl]<b3Vec3*, b3Vec3*, b3Vec3*, int, void*, B3Bool>)&OnQueryTriangleByPointer
			: &OnQueryTriangle;
}
