using System.Linq.Expressions;
using System.Reflection;
using Rinten.Internal;

namespace Editor.Inspectors;

/// <summary>SerializedProperty backed by a static or captured property getter/setter.</summary>
public sealed class ReflectionSerializedProperty<T> : SerializedProperty
{
	private readonly PropertyInfo _prop;
	private readonly DisplayInfo _info;
	private readonly Func<T> _get;
	private readonly Action<T> _set;

	public ReflectionSerializedProperty( PropertyInfo prop, Func<T> get, Action<T> set )
	{
		_prop = prop;
		_info = DisplayInfo.ForMember( _prop );

		_get = get;
		_set = set;
	}

	public override SerializedObject Parent => null;
	public override bool IsMethod => false;
	public override string Name => _info.Name;
	public override string DisplayName => _info.Name;
	public override string Description => _info.Description;
	public override string GroupName => _info.Group;
	public override bool IsEditable => true;
	public override int Order => _info.Order;
	public override Type PropertyType => typeof( T );
	public override string SourceFile => default;
	public override int SourceLine => default;
	public override bool HasChanges => false;

	public override ref AsAccessor As => ref base.As;

	public override bool TryGetAsObject( out SerializedObject obj ) => base.TryGetAsObject( out obj );
	public override U GetValue<U>( U defaultValue = default ) => ValueToType<U>( _get() );
	public override void SetValue<U>( U value ) => _set( ValueToType<T>( value ) );

	public override IEnumerable<Attribute> GetAttributes()
	{
		return _prop?.GetCustomAttributes() ?? base.GetAttributes();
	}
}

/// <summary>Makes a <see cref="ReflectionSerializedProperty{T}"/> out of the property an expression names.</summary>
public static class ReflectionSerializedProperty
{
	/// <summary>A property of a static class, read and written through its own accessors.</summary>
	public static SerializedProperty For<T>( Expression<Func<T>> property )
	{
		var info = MemberOf( property );

		if ( info.GetMethod is null || info.SetMethod is null )
			throw new InvalidOperationException( $"Property '{info.Name}' must have a getter and a setter." );

		var getter = (Func<T>)Delegate.CreateDelegate( typeof( Func<T> ), info.GetMethod );
		var setter = (Action<T>)Delegate.CreateDelegate( typeof( Action<T> ), info.SetMethod );

		return new ReflectionSerializedProperty<T>( info, getter, setter );
	}

	static PropertyInfo MemberOf<T>( Expression<Func<T>> property )
	{
		if ( property.Body is not MemberExpression member || member.Member is not PropertyInfo info )
			throw new ArgumentException( "Expression must target a property.", nameof( property ) );

		return info;
	}
}
