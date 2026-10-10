
using System;

namespace Rinten.Diagnostics;

public static class Assert
{
	/// <summary>Throws an exception when the given object is null.</summary>
	public static void NotNull<T>( T obj, string message )
	{
		if ( obj == null )
			throw new System.Exception( message );
	}

	/// <summary>Throws an exception when the given object is null.</summary>
	public static void NotNull<T>( T obj ) => NotNull( obj, "Object should not be null" );

	/// <summary>Throws an exception when the given object is not null.</summary>
	public static void IsNull<T>( T obj, string message )
	{
		if ( obj != null )
			throw new System.Exception( message );
	}

	/// <summary>Throws an exception when the given object is not null.</summary>
	public static void IsNull<T>( T obj ) => IsNull( obj, "Object should be null" );

	/// <summary>Throws an exception when the given object is not valid.</summary>
	public static void IsValid( IValid obj )
	{
		if ( !obj.IsValid() )
			throw new System.Exception( "Assert: IsValid" );
	}

	/// <summary>Throws an exception when the 2 given objects are not equal to each other.</summary>
	public static void AreEqual<T>( T a, T b, string message = null )
	{
		//The comparer, not object.Equals, which boxes both sides of every value-type assert - and asserts run in shipped code, some of them every frame.
		if ( !EqualityComparer<T>.Default.Equals( a, b ) )
			throw new System.Exception( $"Assert: AreEqual {message}" );
	}

	/// <summary>Throws an exception when the 2 given objects are equal to each other.</summary>
	public static void AreNotEqual<T>( T a, T b, string message = null )
	{
		if ( EqualityComparer<T>.Default.Equals( a, b ) )
			throw new System.Exception( $"Assert: AreNotEqual {message}" );
	}

	/// <summary>Throws an exception when given expression does not resolve to <b>true</b>.</summary>
	public static void True( bool isValid, string message = null )
	{
		if ( !isValid )
			throw new System.Exception( $"Assert: {message ?? "True"}" );
	}

	/// <summary>Throws an exception when given expression does not resolve to <b>false</b>.</summary>
	public static void False( bool isValid, string message = null )
	{
		if ( isValid )
			throw new System.Exception( $"Assert: {message ?? "False"}" );
	}
}
