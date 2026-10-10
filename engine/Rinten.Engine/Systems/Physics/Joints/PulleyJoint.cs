namespace Rinten.Physics;

/// <summary>A pulley constraint. Consists of 2 ropes which share same length, and the ratio changes via physics interactions. Typical setup looks like this: <code> @-----------------@ | | | | Object A Object B </code></summary>
public partial class PulleyJoint : PhysicsJoint
{
	internal PulleyJoint( HandleCreationData _ ) { }
}
