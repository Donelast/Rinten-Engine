using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rinten.Native.Box2D;
using static Rinten.Native.Box2D.Box2D;

namespace Rinten;

//Box2D callbacks; wasm32 passes structs by pointer so the browser gets separate entries
//Single worker, so a [ThreadStatic] holds the context
internal sealed unsafe partial class Box2DWorldBackend
{
	private static bool StructsByPointer => OperatingSystem.IsBrowser();

	//Collision rules

	//A shape's tags, as the bits of the compiled rules they take part in, live in its
	//user data - Box2D stores a pointer there and never reads it, and a 64-bit mask fits.
	private static ulong TagMask( b2ShapeId shape ) => (ulong)b2Shape_GetUserData( shape );

	//Whether two shapes collide by the rules - see CoreCollisionRules.Resolve. Only Collide lets them:
	//trigger and ignore both keep the pair apart here, Box2D having no way to touch without pushing.
	private static bool RulesAllow( byte* table, b2ShapeId a, b2ShapeId b )
		=> CoreCollisionRules.Resolve( table, TagMask( a ), TagMask( b ) ) == CoreCollisionRules.Collide;

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B2Bool OnCustomFilter( b2ShapeId a, b2ShapeId b, void* context )
		=> RulesAllow( (byte*)context, a, b );

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B2Bool OnCustomFilterByPointer( b2ShapeId* a, b2ShapeId* b, void* context )
		=> RulesAllow( (byte*)context, *a, *b );

	private static delegate* unmanaged[Cdecl]<b2ShapeId, b2ShapeId, void*, B2Bool> CustomFilterEntry()
		=> StructsByPointer
			? (delegate* unmanaged[Cdecl]<b2ShapeId, b2ShapeId, void*, B2Bool>)(void*)(delegate* unmanaged[Cdecl]<b2ShapeId*, b2ShapeId*, void*, B2Bool>)&OnCustomFilterByPointer
			: &OnCustomFilter;

	//Overlaps

	//Where an overlap query collects, for the thread running it.
	[ThreadStatic] private static List<b2ShapeId> overlapHits;

	private static B2Bool CollectOverlap( b2ShapeId shape )
	{
		overlapHits?.Add( shape );
		return true;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B2Bool OnOverlap( b2ShapeId shape, void* context ) => CollectOverlap( shape );

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static B2Bool OnOverlapByPointer( b2ShapeId* shape, void* context ) => CollectOverlap( *shape );

	private static delegate* unmanaged[Cdecl]<b2ShapeId, void*, B2Bool> OverlapEntry()
		=> StructsByPointer
			? (delegate* unmanaged[Cdecl]<b2ShapeId, void*, B2Bool>)(void*)(delegate* unmanaged[Cdecl]<b2ShapeId*, void*, B2Bool>)&OnOverlapByPointer
			: &OnOverlap;

	//Casts

	//One thing a cast passed through.
	internal struct CastHit
	{
		public b2ShapeId Shape;
		public Vector2 Point;
		public Vector2 Normal;
		public float Fraction;
	}

	//What the cast running on this thread is collecting, and who decides which shapes count. The decision is made inside
	//the callback, so the closest hit Box2D clips to is the closest one that passes - not the closest one there is.
	private sealed class CastState
	{
		public readonly List<CastHit> Hits = new();
		public Func<b2ShapeId, bool> Accept;
		public bool All;
	}

	[ThreadStatic] private static CastState castState;

	private static float CollectCast( b2ShapeId shape, b2Vec2 point, b2Vec2 normal, float fraction )
	{
		var state = castState;
		if ( state is null ) return -1.0f;

		try
		{
			if ( state.Accept is not null && !state.Accept( shape ) ) return -1.0f;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error in a 2D trace filter: {e.Message}" );
			return -1.0f;
		}

		state.Hits.Add( new CastHit
		{
			Shape = shape,
			Point = Box2DPlane.Flat( point ),
			Normal = Box2DPlane.Flat( normal ),
			Fraction = fraction,
		} );

		//All hits: keep going the whole way. Closest: clip the rest of the cast to this one, so anything further is never reported.
		return state.All ? 1.0f : fraction;
	}

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static float OnCast( b2ShapeId shape, b2Vec2 point, b2Vec2 normal, float fraction, void* context )
		=> CollectCast( shape, point, normal, fraction );

	[UnmanagedCallersOnly( CallConvs = [typeof( CallConvCdecl )] )]
	private static float OnCastByPointer( b2ShapeId* shape, b2Vec2* point, b2Vec2* normal, float fraction, void* context )
		=> CollectCast( *shape, *point, *normal, fraction );

	private static delegate* unmanaged[Cdecl]<b2ShapeId, b2Vec2, b2Vec2, float, void*, float> CastEntry()
		=> StructsByPointer
			? (delegate* unmanaged[Cdecl]<b2ShapeId, b2Vec2, b2Vec2, float, void*, float>)(void*)(delegate* unmanaged[Cdecl]<b2ShapeId*, b2Vec2*, b2Vec2*, float, void*, float>)&OnCastByPointer
			: &OnCast;
}
