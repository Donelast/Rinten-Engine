namespace Rinten;

/// <summary>Holds any concrete implementation of T; the inspector lets you pick which type</summary>
[Obsolete( "AnyOfType<T> is obsolete and will be removed." )]
[Library]
public readonly struct AnyOfType<T> where T : class
{
	/// <summary>The concrete instance, or null if no type is selected.</summary>
	public T Value { get; init; }

	/// <summary>Returns true if <see cref="Value"/> is not null.</summary>
	public bool HasValue => Value is not null;

	//The concrete type name for serialization. When null, falls back to Value's runtime type.
	internal string TypeName { get; init; }

	public AnyOfType( T value )
	{
		Value = value;
		TypeName = value?.GetType().FullName;
	}

	internal AnyOfType( T value, string typeName )
	{
		Value = value;
		TypeName = typeName;
	}

	/// <summary>Implicitly convert a concrete instance to <see cref="AnyOfType{T}"/>.</summary>
	public static implicit operator AnyOfType<T>( T value ) => new( value );

	/// <summary>Implicitly unwrap to the underlying value.</summary>
	public static implicit operator T( AnyOfType<T> wrapper ) => wrapper.Value;

	public override string ToString() => Value?.ToString() ?? "(none)";
}
