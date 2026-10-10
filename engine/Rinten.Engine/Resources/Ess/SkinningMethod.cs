namespace Rinten;

/// <summary>How a model's skin follows its bones - see <see cref="ModelDefinition.Skinning"/>.</summary>
public enum SkinningMethod
{
	/// <summary>The bones' matrices blended by weight: the cheapest, and the usual. A joint twisted half round folds its skin to nothing - the "candy wrapper" at an elbow or a wrist.</summary>
	Linear = 0,

	/// <summary>The bones blended as rotations (dual quaternions): a twisted joint keeps its volume. A little more arithmetic a vertex, and a bone's scale is blended apart from its turn.</summary>
	DualQuaternion = 1,
}
