using System;
using System.Collections.Generic;

namespace JsonTests;


[TestClass]
[DoNotParallelize]
public class TypeSerializationTest
{
	private static Type RoundTrip( Type type ) => Json.Deserialize<Type>( Json.Serialize( type ) );

	//A generic with one argument left for somebody to fill in later.
	private static Type HalfFilledDictionary => typeof( Dictionary<,> )
		.MakeGenericType( typeof( string ), typeof( Dictionary<,> ).GetGenericArguments()[1] );

	[TestMethod]
	public void RoundTrip_SystemType()
	{
		Assert.AreEqual( typeof( int ), RoundTrip( typeof( int ) ) );
		Assert.AreEqual( typeof( string ), RoundTrip( typeof( string ) ) );
		Assert.AreEqual( typeof( object ), RoundTrip( typeof( object ) ) );
	}

	[TestMethod]
	public void RoundTrip_EngineType()
	{
		Assert.AreEqual( typeof( GameObject ), RoundTrip( typeof( GameObject ) ) );
	}

	[TestMethod]
	public void PlainTypeIsStoredAsItsName()
	{
		Assert.AreEqual( "\"System.Int32\"", Json.Serialize( typeof( int ) ) );
	}

	[TestMethod]
	public void RoundTrip_ConstructedGeneric()
	{
		Assert.AreEqual( typeof( List<int> ), RoundTrip( typeof( List<int> ) ) );
		Assert.AreEqual( typeof( Dictionary<string, float> ), RoundTrip( typeof( Dictionary<string, float> ) ) );
	}

	[TestMethod]
	public void RoundTrip_NestedGeneric()
	{
		Assert.AreEqual( typeof( List<List<int>> ), RoundTrip( typeof( List<List<int>> ) ) );
	}

	[TestMethod]
	public void RoundTrip_OpenGeneric()
	{
		Assert.AreEqual( typeof( List<> ), RoundTrip( typeof( List<> ) ) );
		Assert.AreEqual( typeof( Dictionary<,> ), RoundTrip( typeof( Dictionary<,> ) ) );
	}

	[TestMethod]
	public void RoundTrip_HalfFilledGeneric()
	{
		var result = RoundTrip( HalfFilledDictionary );

		Assert.AreEqual( HalfFilledDictionary, result );
		Assert.AreEqual( typeof( string ), result.GetGenericArguments()[0] );
		Assert.IsTrue( result.GetGenericArguments()[1].IsGenericParameter, "the second argument should still be unfilled" );
	}

	[TestMethod]
	public void HalfFilledGenericLeavesAnEmptySlot()
	{
		Assert.AreEqual( "\"System.Collections.Generic.Dictionary`2<System.String,>\"", Json.Serialize( HalfFilledDictionary ) );
	}

	[TestMethod]
	public void RoundTrip_Array()
	{
		Assert.AreEqual( typeof( int[] ), RoundTrip( typeof( int[] ) ) );
		Assert.AreEqual( typeof( int[,] ), RoundTrip( typeof( int[,] ) ) );
		Assert.AreEqual( typeof( List<int>[] ), RoundTrip( typeof( List<int>[] ) ) );
	}

	[TestMethod]
	public void Reads_NameWrittenBeforeGenericsWereSupported()
	{
		Assert.AreEqual( typeof( int ), Json.Deserialize<Type>( "\"System.Int32\"" ) );
		Assert.AreEqual( typeof( GameObject ), Json.Deserialize<Type>( $"\"{typeof( GameObject ).FullName}\"" ) );
	}

	[TestMethod]
	public void UnknownTypeReadsAsNullRatherThanThrowing()
	{
		Assert.IsNull( Json.Deserialize<Type>( "\"Definitely.Not.A.Type\"" ) );
		Assert.IsNull( Json.Deserialize<Type>( "null" ) );
		Assert.IsNull( Json.Deserialize<Type>( "\"\"" ) );
	}

	[TestMethod]
	public void UnknownGenericArgumentLeavesTheSlotEmpty()
	{
		var result = Json.Deserialize<Type>( "\"System.Collections.Generic.Dictionary`2<System.String,Definitely.Not.A.Type>\"" );

		Assert.AreEqual( HalfFilledDictionary, result );
	}

	[TestMethod]
	public void RoundTrip_AsAPropertyOfAnObject()
	{
		var json = Json.Serialize( new TypeHolder { Value = typeof( List<int> ) } );
		var result = Json.Deserialize<TypeHolder>( json );

		Assert.AreEqual( typeof( List<int> ), result.Value );
	}

	private class TypeHolder
	{
		public Type Value { get; set; }
	}
}
