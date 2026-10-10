namespace Rinten.MovieMaker;

#nullable enable

/// <summary>Scene object or property controlled by an ITrack, from TrackBinder.Get; IsBound means live</summary>
public interface ITrackTarget
{
	/// <summary>The binder that created this target.</summary>
	TrackBinder Binder { get; }

	/// <summary>Name of this target, for debugging and editing.</summary>
	string Name { get; }

	/// <summary>Value type of this target.</summary>
	Type TargetType { get; }

	/// <summary>If true, this target is connected to a real object in the scene, so can be accessed.</summary>
	bool IsBound { get; }

	/// <summary>If true, the target is bound and active in the scene hierarchy.</summary>
	bool IsActive { get; }

	/// <summary>If bound, the current value of this target in the scene.</summary>
	object? Value { get; }

	/// <summary>Component / game object / property that contains this target, if from a nested track.</summary>
	ITrackTarget? Parent { get; }
}

public interface ITrackTarget<out T> : ITrackTarget
{
	new T Value { get; }

	Type ITrackTarget.TargetType => typeof( T );
	object? ITrackTarget.Value => Value;
}

/// <summary>A target referencing a <see cref="GameObject"/> or <see cref="Component"/> in the scene.</summary>
public interface ITrackReference : ITrackTarget
{
	/// <summary>The <see cref="IReferenceTrack.Id"/> of the reference track this target was created from.</summary>
	Guid Id { get; }

	/// <summary>Optional game object target that contains this one, if from a nested track.</summary>
	new ITrackReference<GameObject>? Parent { get; }

	/// <summary>Explicitly bind this reference to a particular object in the scene, or null to force it to stay unbound.</summary>
	void Bind( IValid? value );

	/// <summary>Clear any explicit binding, so this reference will auto-bind based on its name, type, and parent.</summary>
	void Reset();

	ITrackTarget? ITrackTarget.Parent => Parent;
}

public interface ITrackReference<T> : ITrackReference, ITrackTarget<T?>
	where T : class, IValid
{
	void Bind( T? value );

	void ITrackReference.Bind( IValid? value ) => Bind( (T?)value );
}

/// <summary>A target referencing a member property or field of another target.</summary>
public partial interface ITrackProperty : ITrackTarget, IValid
{
	/// <summary>Target that this member belongs to.</summary>
	new ITrackTarget Parent { get; }

	/// <summary>False if this member is write-only.</summary>
	bool CanRead => true;

	/// <summary>False if this member is read-only.</summary>
	bool CanWrite => true;

	/// <summary>If bound, gets or sets the current value of this member.</summary>
	new object? Value { get; set; }

	/// <summary>Does this property have a default value that should be applied at times when controlling tracks have no data?</summary>
	bool HasDefaultValue => false;

	/// <summary>If <see cref="HasDefaultValue"/> is true, what default value should be used for this property.</summary>
	object? DefaultValue { get; }

	bool IValid.IsValid => true;
	TrackBinder ITrackTarget.Binder => Parent.Binder;
	bool ITrackTarget.IsBound => Parent is { IsBound: true };
	bool ITrackTarget.IsActive => Parent is { IsActive: true };
	ITrackTarget ITrackTarget.Parent => Parent;
}

public partial interface ITrackProperty<T> : ITrackProperty, ITrackTarget<T>
{
	new T Value { get; set; }

	new T DefaultValue => default!;

	T ITrackTarget<T>.Value => Value;

	object? ITrackProperty.Value
	{
		get => Value;
		set => Value = (T)value!;
	}

	object? ITrackProperty.DefaultValue => DefaultValue;
}
