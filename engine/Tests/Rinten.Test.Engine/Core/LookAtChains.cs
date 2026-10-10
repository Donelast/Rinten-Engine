using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten;
using Rinten.Porting.SBox;

namespace EngineTests.Core;

//What a character bends when it looks at something. Their model keeps this as a block of text inside itself - which bones turn, and
//how much of the turn each takes - and until it is read a ported character stares straight ahead whatever the game tells it to look at.
[TestClass]
[DoNotParallelize]
public class LookAtChainTests
{
	private static string Citizen
	{
		get
		{
			var install = SBoxBaseContent.Find();
			if ( install is null ) return null;

			var path = Path.Combine( install, "addons", "citizen", "Assets",
				"models", "citizen", "citizen.vmdl_c" );

			return File.Exists( path ) ? path : null;
		}
	}

	//The one place their whole model file is read as text rather than unpacked, which is how these are stored.
	[TestMethod]
	public void TheirWrittenKeyValuesRead()
	{
		var text = """
			<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} -->
			{
				count = 3
				fraction = 0.45
				name = "spine_2"
				flag = true
				missing = null
				list =
				[
					{ name = "head", weight = 0.6 },
					{ name = "neck_0", weight = 0.35 },
				]
				nested = { deep = { deeper = "here" } }
			}
			""";

		var read = CoreImportedKeyValues3.ReadText( text );

		Assert.IsNotNull( read, "a document of theirs would not read" );

		Assert.AreEqual( 3, read.Int( "count" ) );
		Assert.AreEqual( 0.45f, read.Float( "fraction" ), 0.0001f );
		Assert.AreEqual( "spine_2", read.String( "name" ) );
		Assert.IsTrue( read.Bool( "flag" ) );

		var list = read.Array( "list" );

		Assert.AreEqual( 2, list.Count );
		Assert.AreEqual( "head", list[0].String( "name" ) );
		Assert.AreEqual( 0.35f, list[1].Float( "weight" ), 0.0001f );

		Assert.AreEqual( "here", read["nested"]["deep"].String( "deeper" ) );
	}
}
