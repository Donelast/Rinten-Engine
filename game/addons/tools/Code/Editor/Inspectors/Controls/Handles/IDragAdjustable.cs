namespace Editor.Inspectors;

/// <summary>A value that a sideways drag over its control's handle moves.</summary>
public interface IDragAdjustable
{
	/// <summary>False when there is nothing a drag could move; a read-only property is the usual reason. Asked again at every press, because it changes.</summary>
	bool CanAdjust { get; }

	/// <summary>A drag has begun. Nothing has moved yet.</summary>
	void BeginAdjust();

	/// <summary>The pointer has gone <paramref name="pixels"/> to the right since the last call, negative for left, already scaled by the precision keys.</summary>
	void Adjust( float pixels );

	/// <summary>The drag is over, whether or not it moved anything.</summary>
	void EndAdjust();
}
