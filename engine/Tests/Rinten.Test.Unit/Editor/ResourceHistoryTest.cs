using System;
using System.IO;
using Editor;

namespace UnitTests.Editor;

//The way back for a resource: every version it has been, and the rule about when stepping back is allowed. A resource is written as it is edited, so
//the file is never behind - which leaves "put it back" as the thing that had to be built, and the thing that can do damage if it is built carelessly.
[TestClass]
[DoNotParallelize]
public class ResourceHistoryTest
{
	private string file;

	[TestInitialize]
	public void MakeAResource()
	{
		var directory = Path.Combine( Path.GetTempPath(), "rinten-resource-history", Guid.NewGuid().ToString( "N" ) );

		Directory.CreateDirectory( directory );

		file = Path.Combine( directory, "thing.material" );

		File.WriteAllText( file, "{\"Colour\":\"red\"}" );

		//Somewhere to keep the history that is not a project: these tests are about the rules, and the rules do not care whose folder it is in.
		ResourceHistory.RootOverride = directory;
	}

	[TestCleanup]
	public void ForgetWhereHistoriesGo()
	{
		ResourceHistory.RootOverride = null;
	}

	//Without a project - and without the override above - there is nowhere to keep
	//a history, and everything answers with "no history" rather than throwing.
	[TestMethod]
	public void WithNowhereToWriteNothingIsRememberedAndNothingBreaks()
	{
		ResourceHistory.RootOverride = null;

		ResourceHistory.Record( file, "Edit", "{}", "{\"Colour\":\"blue\"}" );

		Assert.AreEqual( 0, ResourceHistory.Count( file ) );
		Assert.IsFalse( ResourceHistory.TryStepBack( file, out _, out _ ) );
	}

	//The ordinary way back: the version before the one on the disk.
	[TestMethod]
	public void TheVersionBeforeTheLastOneComesBack()
	{
		var before = File.ReadAllText( file );
		var after = "{\"Colour\":\"blue\"}";

		File.WriteAllText( file, after );
		ResourceHistory.Record( file, "Paint it blue", before, after );

		Assert.IsTrue( ResourceHistory.TryStepBack( file, out var document, out var name ) );
		Assert.AreEqual( before, document );
		Assert.AreEqual( "Paint it blue", name );
	}

	//And the step back is itself remembered, so the history stays a straight line of what the file has been.
	[TestMethod]
	public void SteppingBackIsItselfAVersion()
	{
		var first = File.ReadAllText( file );
		var second = "{\"Colour\":\"blue\"}";

		File.WriteAllText( file, second );
		ResourceHistory.Record( file, "Paint it blue", first, second );

		Assert.IsTrue( ResourceHistory.TryStepBack( file, out var back, out _ ) );

		//What the caller does with it: writes it out.
		File.WriteAllText( file, back );

		Assert.AreEqual( 2, ResourceHistory.Count( file ) );
		Assert.IsTrue( ResourceHistory.TryStepBack( file, out var forward, out _ ),
			"and from there the way back is the version it was undone from" );
		Assert.AreEqual( second, forward );
	}

	[TestMethod]
	public void AVersionThatChangesNothingIsNotAVersion()
	{
		var same = "{\"Colour\":\"red\"}";

		ResourceHistory.Record( file, "Edit", same, same );

		Assert.AreEqual( 0, ResourceHistory.Count( file ),
			"writing the same document again is not something to step back over" );
	}

	[TestMethod]
	public void ThereIsNoWayBackFromAFileNobodyWroteThroughTheEditor()
	{
		Assert.IsFalse( ResourceHistory.TryStepBack( file, out _, out _ ) );
	}

	[TestMethod]
	public void AFileChangedElsewhereIsNotSteppedBackOver()
	{
		var before = File.ReadAllText( file );
		var after = "{\"Colour\":\"blue\"}";

		File.WriteAllText( file, after );

		ResourceHistory.Record( file, "Edit", before, after );

		//Somebody else - a port, a checkout, another editor - writes the file.
		File.WriteAllText( file, "{\"Colour\":\"green\"}" );

		Assert.IsFalse( ResourceHistory.TryStepBack( file, out _, out _ ),
			"the history ends at a version that is no longer what the file is" );
	}
}
