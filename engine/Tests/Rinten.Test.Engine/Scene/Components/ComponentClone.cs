using System;
using System.Collections.Generic;

namespace SceneTests.Components;

//Clone copies value members directly, deep-copies refs via JSON, and remaps in-hierarchy refs
[TestClass]
[DoNotParallelize]
public class ComponentCloneTest : SceneTest
{
	//Value-type properties and fields (and strings) are copied from the original onto the
	//clone, and the copy goes in the right direction - the original is never written to.
	[TestMethod]
	public void ValueMembersAreCopiedToClone()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<CloneDataComponent>();
		comp.Number = 42;
		comp.Offset = new Vector3( 1, 2, 3 );
		comp.Title = "hello";

		var clone = go.Clone();
		var cloneComp = clone.GetComponent<CloneDataComponent>();

		Assert.IsNotNull( cloneComp );
		Assert.AreEqual( 42, cloneComp.Number );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), cloneComp.Offset );
		Assert.AreEqual( "hello", cloneComp.Title );

		cloneComp.Number = 7;
		Assert.AreEqual( 42, comp.Number );
	}

	//Mutable reference-type properties like lists must be deep-copied, so mutating the clone's list doesn't change the original.
	[TestMethod]
	public void ListPropertyIsDeepCopied()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<CloneDataComponent>();
		comp.Values = new List<int> { 1, 2, 3 };

		var clone = go.Clone();
		var cloneComp = clone.GetComponent<CloneDataComponent>();

		Assert.AreNotSame( comp.Values, cloneComp.Values );
		CollectionAssert.AreEqual( comp.Values, cloneComp.Values );

		cloneComp.Values.Add( 4 );
		Assert.AreEqual( 3, comp.Values.Count );
	}

	//A member type that declares its own Clone() (ICloneable) is cloned via that method, producing an equal but distinct instance.
	[TestMethod]
	public void ICloneableMemberIsClonedViaCloneMethod()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<CloneDataComponent>();
		comp.Bag = new CloneableBag { Value = 5 };

		var clone = go.Clone();
		var cloneComp = clone.GetComponent<CloneDataComponent>();

		Assert.IsNotNull( cloneComp.Bag );
		Assert.AreNotSame( comp.Bag, cloneComp.Bag );
		Assert.AreEqual( 5, cloneComp.Bag.Value );
	}

	//Null reference members stay null on the clone - cloning must not fabricate instances.
	[TestMethod]
	public void NullMembersStayNull()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<CloneDataComponent>();
		comp.Title = null;
		comp.Bag = null;

		var clone = go.Clone();
		var cloneComp = clone.GetComponent<CloneDataComponent>();

		Assert.IsNull( cloneComp.Title );
		Assert.IsNull( cloneComp.Bag );
	}

	//A component property referencing a component inside the cloned hierarchy is remapped to the cloned
	//instance - here a self-reference must point at the clone itself, never back at the original.
	[TestMethod]
	public void ComponentReferenceIsRemappedToClone()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();

		var go = scene.CreateObject();
		var comp = go.Components.Create<CloneDataComponent>();
		comp.Buddy = comp;

		var clone = go.Clone();
		var cloneComp = clone.GetComponent<CloneDataComponent>();

		Assert.AreSame( cloneComp, cloneComp.Buddy );
		Assert.AreNotSame( comp, cloneComp.Buddy );
	}
}

/// <summary>Component whose members cover the different cloning strategies: value-type property and field, string, mutable list, ICloneable class and a component reference.</summary>
public class CloneDataComponent : Component
{
	[Property]
	public int Number { get; set; }

	[Property]
	public Vector3 Offset;

	[Property]
	public string Title { get; set; }

	[Property]
	public List<int> Values { get; set; } = new();

	[Property]
	public CloneableBag Bag { get; set; }

	[Property]
	public CloneDataComponent Buddy { get; set; }
}

//Plain class implementing ICloneable so the clone system uses its Clone() instead of a JSON round trip.
public class CloneableBag : ICloneable
{
	public int Value { get; set; }

	//Returns a copy of this bag carrying the same value.
	public object Clone() => new CloneableBag { Value = Value };
}
