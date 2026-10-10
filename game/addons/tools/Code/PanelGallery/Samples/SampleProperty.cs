using Editor.Inspectors;
using System.Reflection;

namespace Editor.Gallery;

//Built on ReflectionSerializedProperty over a SampleHolder closed at run time - the same property the preferences pages hand their rows, so a control here sees exactly what it sees there, including a null parent.
/// <summary>Turns a type and a value into the <see cref="SerializedProperty"/> a control edits.</summary>
public static class SampleProperty
{
	public static SerializedProperty Create( Type type, object value )
	{
		ArgumentNullException.ThrowIfNull( type );

		var holderType = typeof( SampleHolder<> ).MakeGenericType( type );
		var holder = Activator.CreateInstance( holderType );
		var property = holderType.GetProperty( nameof( SampleHolder<object>.Value ) )
			?? throw new InvalidOperationException( "SampleHolder lost its Value" );

		if ( value is not null && type.IsInstanceOfType( value ) )
			property.SetValue( holder, value );

		var getter = Delegate.CreateDelegate( typeof( Func<> ).MakeGenericType( type ), holder, property.GetMethod );
		var setter = Delegate.CreateDelegate( typeof( Action<> ).MakeGenericType( type ), holder, property.SetMethod );

		var serializedType = typeof( ReflectionSerializedProperty<> ).MakeGenericType( type );

		return (SerializedProperty)Activator.CreateInstance( serializedType, property, getter, setter );
	}

	/// <summary>A property of a known type, for the hand-written pages.</summary>
	public static SerializedProperty Create<T>( T value ) => Create( typeof( T ), value );
}
