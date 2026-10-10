using System.Collections.Generic;
using System.Linq;
using Rinten;
using Rinten.Internal;
using static Rinten.Internal.GlobalToolsNamespace;

namespace EngineTests;

[TestClass]
public class EditorButtonOverloadTests
{
	class ButtonOverloadTarget
	{
		public int NoArgCalls;
		public int FloatArgCalls;
		public float LastFloatArg;
		public int TwoArgCalls;

		[Button]
		public void TestOverload() => NoArgCalls++;

		[Button]
		public void TestOverload( float arg )
		{
			FloatArgCalls++;
			LastFloatArg = arg;
		}

		[Button]
		public void TestOverloadWithDefaultArg( float arg = 67 )
		{
			FloatArgCalls++;
			LastFloatArg = arg;
		}

		[Button]
		public void TestOverloadWithDefaultArg( float arg, float arg2 ) => TwoArgCalls++;

		[Button]
		public void BadOverload( float arg ) => FloatArgCalls++;

		[Button]
		public void BadOverload( float arg, float arg2 ) => TwoArgCalls++;
	}

	[TestMethod]
	public void RemoveUnusedMethods_KeepsAllOverloadsButDisablesLosers()
	{
		var props = EditorTypeLibrary.GetSerializedObject( new ButtonOverloadTarget() ).ToList();
		RemoveUnusedMethods( props );

		var overloads = props.Where( p => p.IsMethod && p.Name == nameof( ButtonOverloadTarget.TestOverload ) ).ToList();
		Assert.AreEqual( 2, overloads.Count );

		var noArg = overloads.Single( p => p.MethodParameters.Length == 0 );
		var withArg = overloads.Single( p => p.MethodParameters.Length == 1 );

		Assert.IsTrue( noArg.ShouldEnable() );
		Assert.IsFalse( withArg.ShouldEnable() );
	}

	[TestMethod]
	public void RemoveUnusedMethods_KeepsDefaultArgOverloadEnabled()
	{
		var props = EditorTypeLibrary.GetSerializedObject( new ButtonOverloadTarget() ).ToList();
		RemoveUnusedMethods( props );

		var overloads = props.Where( p => p.IsMethod && p.Name == nameof( ButtonOverloadTarget.TestOverloadWithDefaultArg ) ).ToList();
		Assert.AreEqual( 2, overloads.Count );

		var withDefault = overloads.Single( p => p.MethodParameters.Length == 1 );
		var withTwoArgs = overloads.Single( p => p.MethodParameters.Length == 2 );

		Assert.IsTrue( withDefault.ShouldEnable() );
		Assert.IsFalse( withTwoArgs.ShouldEnable() );
		Assert.IsTrue( withDefault.MethodParameters[0].HasDefaultValue );
	}

	[TestMethod]
	public void RemoveUnusedMethods_DisablesUncallableOverloads()
	{
		var props = EditorTypeLibrary.GetSerializedObject( new ButtonOverloadTarget() ).ToList();
		RemoveUnusedMethods( props );

		var overloads = props.Where( p => p.IsMethod && p.Name == nameof( ButtonOverloadTarget.BadOverload ) ).ToList();
		Assert.AreEqual( 2, overloads.Count );
		Assert.IsTrue( overloads.All( p => !p.ShouldEnable() ) );
	}

	[TestMethod]
	public void MultiSerializedObject_KeepsOverloadsSeparate()
	{
		var mso = BuildMultiSerializedObject( new ButtonOverloadTarget() );
		var overloads = mso.ToList().Where( p => p.Name == nameof( ButtonOverloadTarget.TestOverload ) ).ToList();

		Assert.AreEqual( 2, overloads.Count );
		Assert.IsTrue( overloads.Any( p => p.MethodParameters.Length == 0 ) );
		Assert.IsTrue( overloads.Any( p => p.MethodParameters.Length == 1 ) );
	}

	[TestMethod]
	public void Invoke_NoArgOverload_DoesNotThrow()
	{
		var target = new ButtonOverloadTarget();
		var mso = BuildMultiSerializedObject( target );
		var props = mso.ToList();
		RemoveUnusedMethods( props );

		var overload = props.Single( p => p.IsMethod && p.Name == nameof( ButtonOverloadTarget.TestOverload ) && p.ShouldEnable() );
		overload.Invoke();

		Assert.AreEqual( 1, target.NoArgCalls );
		Assert.AreEqual( 0, target.FloatArgCalls );
	}

	[TestMethod]
	public void Invoke_DefaultArgOverload_UsesDefaultValue()
	{
		var target = new ButtonOverloadTarget();
		var mso = BuildMultiSerializedObject( target );
		var props = mso.ToList();
		RemoveUnusedMethods( props );

		var overload = props.Single( p => p.IsMethod && p.Name == nameof( ButtonOverloadTarget.TestOverloadWithDefaultArg ) && p.ShouldEnable() );
		overload.Invoke();

		Assert.AreEqual( 1, target.FloatArgCalls );
		Assert.AreEqual( 67f, target.LastFloatArg );
		Assert.AreEqual( 0, target.TwoArgCalls );
	}

	[TestMethod]
	public void Invoke_MultiTarget_CallsEachNoArgOverload()
	{
		var a = new ButtonOverloadTarget();
		var b = new ButtonOverloadTarget();
		var mso = new MultiSerializedObject();
		mso.Add( EditorTypeLibrary.GetSerializedObject( a ) );
		mso.Add( EditorTypeLibrary.GetSerializedObject( b ) );
		mso.Rebuild();

		var props = mso.ToList();
		RemoveUnusedMethods( props );

		var overload = props.Single( p => p.IsMethod && p.Name == nameof( ButtonOverloadTarget.TestOverload ) && p.ShouldEnable() );
		overload.Invoke();

		Assert.AreEqual( 1, a.NoArgCalls );
		Assert.AreEqual( 1, b.NoArgCalls );
	}

	static MultiSerializedObject BuildMultiSerializedObject( ButtonOverloadTarget target )
	{
		var mso = new MultiSerializedObject();
		mso.Add( EditorTypeLibrary.GetSerializedObject( target ) );
		mso.Rebuild();
		return mso;
	}

	static void RemoveUnusedMethods( List<SerializedProperty> properties )
	{
		IControlSheet sheet = new StubControlSheet();
		sheet.RemoveUnusedMethods( properties );
	}

	sealed class StubControlSheet : IControlSheet
	{
		public bool TestFilter( SerializedProperty prop ) => true;
		public void AddFeature( IControlSheet.Feature feature ) { }
		public void AddGroup( IControlSheet.Group group ) { }
	}
}
