using System.Runtime.InteropServices;
using Rinten.Native.Box3D;
using static Rinten.Native.Box3D.Box3D;

namespace Rinten;

//What touched what during the last step. Box3D buffers these for one step and hands back a view of
//its own arrays, so they are copied out into the caller's buffer before anything can step again.
internal static unsafe partial class CorePhysics
{
	/// <summary>Which of the five things happened. Matches CorePhysicsEvents.Kind.</summary>
	public enum ContactKind
	{
		TouchBegin = 0,
		TouchEnd = 1,
		Hit = 2,
		TriggerBegin = 3,
		TriggerEnd = 4,
	}

	/// <summary>One thing that happened, as the dispatcher reads it.</summary>
	[StructLayout( LayoutKind.Sequential )]
	public struct ContactEvent
	{
		public int Kind;
		public int Padding;
		public ulong ShapeA;
		public ulong ShapeB;
		public float PointX, PointY, PointZ;
		public float NormalX, NormalY, NormalZ;
		public float Speed;
		public int Padding2;
	}

	/// <summary>Takes the events buffered by the last step. They are only valid until the next one.</summary>
	public static int WorldDrainContacts( ulong world, Span<ContactEvent> outEvents )
	{
		if ( outEvents.IsEmpty ) return 0;
		if ( !ResolveWorld( world, out var id ) ) return 0;

		int count = 0;

		b3ContactEvents contacts = b3World_GetContactEvents( id );

		for ( int i = 0; i < contacts.beginCount && count < outEvents.Length; ++i )
			Push( outEvents, ref count, ContactKind.TouchBegin, contacts.beginEvents[i].shapeIdA, contacts.beginEvents[i].shapeIdB );

		for ( int i = 0; i < contacts.endCount && count < outEvents.Length; ++i )
			Push( outEvents, ref count, ContactKind.TouchEnd, contacts.endEvents[i].shapeIdA, contacts.endEvents[i].shapeIdB );

		for ( int i = 0; i < contacts.hitCount; ++i )
		{
			if ( count >= outEvents.Length ) break;

			ref b3ContactHitEvent hit = ref contacts.hitEvents[i];

			int at = count;
			Push( outEvents, ref count, ContactKind.Hit, hit.shapeIdA, hit.shapeIdB );

			ref ContactEvent e = ref outEvents[at];

			e.PointX = hit.point.x; e.PointY = hit.point.y; e.PointZ = hit.point.z;

			//A normal is a direction; scaling it would only denormalise it.
			e.NormalX = hit.normal.x; e.NormalY = hit.normal.y; e.NormalZ = hit.normal.z;

			e.Speed = hit.approachSpeed;
		}

		b3SensorEvents sensors = b3World_GetSensorEvents( id );

		//Sensor events name the sensor first, which is the order the managed side wants too: the trigger is "self".
		for ( int i = 0; i < sensors.beginCount && count < outEvents.Length; ++i )
			Push( outEvents, ref count, ContactKind.TriggerBegin, sensors.beginEvents[i].sensorShapeId, sensors.beginEvents[i].visitorShapeId );

		for ( int i = 0; i < sensors.endCount && count < outEvents.Length; ++i )
			Push( outEvents, ref count, ContactKind.TriggerEnd, sensors.endEvents[i].sensorShapeId, sensors.endEvents[i].visitorShapeId );

		return count;
	}

	private static void Push( Span<ContactEvent> into, ref int count, ContactKind kind, b3ShapeId a, b3ShapeId b )
	{
		ref ContactEvent e = ref into[count++];

		e = default;
		e.Kind = (int)kind;
		e.ShapeA = Pack( a );
		e.ShapeB = Pack( b );
	}
}
