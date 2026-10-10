using Facepunch.ActionGraphs;
using Rinten.ActionGraphs;
using Rinten.Internal;
using System.Collections.Generic;

namespace SceneTests.GameObjects;

//A component's lists and arrays: edited the way the inspector edits them, written into a scene, and read back.
[TestClass]
[DoNotParallelize]
public class CollectionPropertiesTest : SceneTest
{
	private TypeLibrary _oldTypeLibrary;
	private NodeLibrary _oldNodeLibrary;
	private TypeLibrary typeLibrary;

	[TestInitialize]
	public void TestInitialize()
	{
		_oldTypeLibrary = Game.TypeLibrary;
		_oldNodeLibrary = Game.NodeLibrary;

		//As the editor has it: the engine's exposed types, the intrinsic ones - Vector3 among them - and a game's own code with everything in it.
		typeLibrary = new TypeLibrary();
		typeLibrary.AddIntrinsicTypes();
		typeLibrary.AddAssembly( typeof( ModelRenderer ).Assembly, false );
		typeLibrary.AddAssembly( typeof( ComponentWithCollections ).Assembly, true );
		JsonUpgrader.UpdateUpgraders( typeLibrary );

		Game.TypeLibrary = typeLibrary;
		Game.NodeLibrary = new NodeLibrary( new TypeLoader( () => typeLibrary ) );
	}

	[TestCleanup]
	public void Cleanup()
	{
		Game.TypeLibrary = _oldTypeLibrary;
		Game.NodeLibrary = _oldNodeLibrary;
	}

	private static ComponentWithCollections RoundTrip( ComponentWithCollections source )
	{
		var node = source.GameObject.Serialize();
		System.Console.WriteLine( node );

		SceneUtility.MakeIdGuidsUnique( node );

		var copy = new GameObject();
		copy.Deserialize( node );

		return copy.GetComponent<ComponentWithCollections>();
	}

	[TestMethod]
	public void ListsAndArraysSetInCode_SurviveASaveAndLoad()
	{
		using var scope = new Scene().Push();

		var comp = new GameObject().AddComponent<ComponentWithCollections>();

		comp.Numbers = [1, 2, 3];
		comp.NumberArray = [4, 5];
		comp.Names = ["a", "b"];
		comp.Points = [new Vector3( 1, 2, 3 )];

		var copy = RoundTrip( comp );

		CollectionAssert.AreEqual( new List<int> { 1, 2, 3 }, copy.Numbers );
		CollectionAssert.AreEqual( new[] { 4, 5 }, copy.NumberArray );
		CollectionAssert.AreEqual( new List<string> { "a", "b" }, copy.Names );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), copy.Points[0] );
	}

	//The collection behind a property, the way the inspector's list control asks for it.
	private static SerializedCollection CollectionOf( Component comp, string property )
	{
		var so = Game.TypeLibrary.GetSerializedObject( comp );
		var prop = so.GetProperty( property );

		Assert.IsNotNull( prop, $"no property {property}" );
		Assert.IsTrue( prop.TryGetAsObject( out var inner ), $"{property} gave no object to edit" );
		Assert.IsInstanceOfType( inner, typeof( SerializedCollection ) );

		return (SerializedCollection)inner;
	}

	[TestMethod]
	public void AListEditedThroughItsSerializedObject_SurvivesASaveAndLoad()
	{
		using var scope = new Scene().Push();

		var comp = new GameObject().AddComponent<ComponentWithCollections>();
		comp.Numbers = [];

		var list = CollectionOf( comp, nameof( ComponentWithCollections.Numbers ) );
		list.Add( null );
		list.Add( null );
		list.First().SetValue( 7 );

		CollectionAssert.AreEqual( new List<int> { 7, 0 }, comp.Numbers, "the edit reached the component" );
		CollectionAssert.AreEqual( new List<int> { 7, 0 }, RoundTrip( comp ).Numbers, "and the scene" );
	}

	[TestMethod]
	public void AnArrayEditedThroughItsSerializedObject_SurvivesASaveAndLoad()
	{
		using var scope = new Scene().Push();

		var comp = new GameObject().AddComponent<ComponentWithCollections>();
		comp.NumberArray = [];

		var array = CollectionOf( comp, nameof( ComponentWithCollections.NumberArray ) );
		array.Add( null );
		array.Add( null );
		array.First().SetValue( 9 );

		CollectionAssert.AreEqual( new[] { 9, 0 }, comp.NumberArray, "the edit reached the component" );
		CollectionAssert.AreEqual( new[] { 9, 0 }, RoundTrip( comp ).NumberArray, "and the scene" );
	}

	[TestMethod]
	public void ANullListCanBeAddedTo()
	{
		using var scope = new Scene().Push();

		var comp = new GameObject().AddComponent<ComponentWithCollections>();
		Assert.IsNull( comp.Numbers );

		var list = CollectionOf( comp, nameof( ComponentWithCollections.Numbers ) );
		list.Add( 3 );

		CollectionAssert.AreEqual( new List<int> { 3 }, comp.Numbers );
	}

	[TestMethod]
	public void ANullArrayCanBeAddedTo()
	{
		using var scope = new Scene().Push();

		var comp = new GameObject().AddComponent<ComponentWithCollections>();
		Assert.IsNull( comp.NumberArray );

		var array = CollectionOf( comp, nameof( ComponentWithCollections.NumberArray ) );
		array.Add( 3 );

		CollectionAssert.AreEqual( new[] { 3 }, comp.NumberArray );
	}

	[TestMethod]
	public void TheComponentHearsOfEveryListEdit()
	{
		using var scope = new Scene().Push();

		var comp = new GameObject().AddComponent<ComponentWithCollections>();
		comp.Numbers = [1];

		var so = Game.TypeLibrary.GetSerializedObject( comp );
		var changed = 0;
		var started = 0;
		so.OnPropertyChanged += _ => changed++;
		so.OnPropertyStartEdit += _ => started++;

		so.GetProperty( nameof( ComponentWithCollections.Numbers ) ).TryGetAsObject( out var inner );
		var list = (SerializedCollection)inner;

		list.Add( null );
		Assert.IsTrue( changed > 0, "an entry added" );

		changed = 0;
		list.First().SetValue( 5 );
		Assert.IsTrue( changed > 0, "an entry changed" );

		changed = 0;
		list.RemoveAt( 0 );
		Assert.IsTrue( changed > 0, "an entry removed" );
	}

	[TestMethod]
	public void AStructInAList_EditedByItsMember_IsWrittenBack()
	{
		using var scope = new Scene().Push();

		var comp = new GameObject().AddComponent<ComponentWithCollections>();
		comp.Pairs = [new CollectionPair()];

		var list = CollectionOf( comp, nameof( ComponentWithCollections.Pairs ) );

		Assert.IsTrue( list.First().TryGetAsObject( out var pair ), "an entry that is a struct can be opened" );
		pair.GetProperty( nameof( CollectionPair.Weight ) ).SetValue( 4.0f );

		Assert.AreEqual( 4.0f, comp.Pairs[0].Weight, "the member edit reached the list, not a copy of the entry" );
		Assert.AreEqual( 4.0f, RoundTrip( comp ).Pairs[0].Weight, "and the scene" );
	}

	[TestMethod]
	public void AClassInAList_EditedByItsMember_SurvivesASaveAndLoad()
	{
		using var scope = new Scene().Push();

		var comp = new GameObject().AddComponent<ComponentWithCollections>();
		comp.Entries = [new CollectionEntry()];

		var list = CollectionOf( comp, nameof( ComponentWithCollections.Entries ) );

		Assert.IsTrue( list.First().TryGetAsObject( out var entry ), "an entry that is a class can be opened" );
		entry.GetProperty( nameof( CollectionEntry.Label ) ).SetValue( "hello" );

		Assert.AreEqual( "hello", comp.Entries[0].Label );
		Assert.AreEqual( "hello", RoundTrip( comp ).Entries[0].Label );
	}

	[TestMethod]
	public void SeveralSelected_TheirListsAreEditedTogether()
	{
		using var scope = new Scene().Push();

		var a = new GameObject().AddComponent<ComponentWithCollections>();
		var b = new GameObject().AddComponent<ComponentWithCollections>();
		a.Numbers = [];
		b.Numbers = [];

		var mso = new MultiSerializedObject();
		mso.Add( Game.TypeLibrary.GetSerializedObject( a ) );
		mso.Add( Game.TypeLibrary.GetSerializedObject( b ) );
		mso.Rebuild();

		var changed = 0;
		mso.OnPropertyChanged += _ => changed++;

		Assert.IsTrue( mso.GetProperty( nameof( ComponentWithCollections.Numbers ) ).TryGetAsObject( out var inner ) );
		Assert.IsInstanceOfType( inner, typeof( SerializedCollection ), "the list control only takes a collection" );

		var list = (SerializedCollection)inner;
		list.Add( null );
		list.First().SetValue( 6 );

		CollectionAssert.AreEqual( new List<int> { 6 }, a.Numbers );
		CollectionAssert.AreEqual( new List<int> { 6 }, b.Numbers );
		Assert.IsTrue( changed > 0, "the selection heard of it" );
	}

	//What the list control does around an entry added or taken away - see ListControl.Edit - heard where
	//the component inspector opens and closes its undo scope, which is what marks the scene as changed.
	[TestMethod]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public void AnEntryAddedAsAnEdit_IsHeardAsOneBySelection( int selected )
	{
		using var scope = new Scene().Push();

		var mso = new MultiSerializedObject();
		var comps = new List<ComponentWithCollections>();

		for ( var i = 0; i < selected; i++ )
		{
			var comp = new GameObject().AddComponent<ComponentWithCollections>();
			comp.Numbers = [];
			comps.Add( comp );
			mso.Add( Game.TypeLibrary.GetSerializedObject( comp ) );
		}

		mso.Rebuild();

		var started = 0;
		var finished = 0;
		mso.OnPropertyStartEdit += _ => started++;
		mso.OnPropertyFinishEdit += _ => finished++;

		var property = mso.GetProperty( nameof( ComponentWithCollections.Numbers ) );
		Assert.IsTrue( property.TryGetAsObject( out var inner ) );
		var list = (SerializedCollection)inner;

		property.Parent.NoteStartEdit( property );
		list.Add( null );
		property.Parent.NoteFinishEdit( property );

		Assert.IsTrue( started > 0, "the edit was started" );
		Assert.AreEqual( started, finished, "and finished as often" );

		foreach ( var comp in comps ) Assert.AreEqual( 1, comp.Numbers.Count );
	}
}

public class ComponentWithCollections : Component
{
	[Rinten.Property] public List<int> Numbers { get; set; }
	[Rinten.Property] public int[] NumberArray { get; set; }
	[Rinten.Property] public List<string> Names { get; set; }
	[Rinten.Property] public List<Vector3> Points { get; set; }
	[Rinten.Property] public List<CollectionEntry> Entries { get; set; }
	[Rinten.Property] public List<CollectionPair> Pairs { get; set; }
}

public struct CollectionPair
{
	[Rinten.Property] public string Name { get; set; }
	[Rinten.Property] public float Weight { get; set; }
}

public class CollectionEntry
{
	[Rinten.Property] public string Label { get; set; }
	[Rinten.Property] public int Count { get; set; }
}
