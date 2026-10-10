namespace Rinten;

[Expose]
public struct PhysicsLock
{
	public PhysicsLock()
	{

	}

	public bool X { get; set; }
	public bool Y { get; set; }
	public bool Z { get; set; }
	public bool Pitch { get; set; }
	public bool Yaw { get; set; }
	public bool Roll { get; set; }
}


/// <summary>Represents a physics object. An entity can have multiple physics objects. See <see cref="PhysicsGroup">PhysicsGroup</see>. A physics objects consists of one or more <see cref="PhysicsShape">PhysicsShape</see>s.</summary>
public sealed partial class PhysicsBody : IHandle
{
	PhysicsLock _locks;

	public PhysicsLock Locking
	{
		get => _locks;

		set
		{
			_locks = value;
			Backend.SetMotionLocks( value.X, value.Y, value.Z, value.Pitch, value.Yaw, value.Roll );
		}
	}
}
