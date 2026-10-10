using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rinten.Porting.SBox;

namespace EngineTests.Core;

//What a legacy compiled model reports it needs: materials, textures and its anim graph
[TestClass]
[DoNotParallelize]
public class CarriedNameTests
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

	[TestMethod]
	public void ACharacterNamesEverythingItNeeds()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var named = SBoxBaseContent.Names( Citizen ).ToList();

		Assert.IsTrue( named.Count > 5, $"only {named.Count} names came out of a character" );

		//Its own graph, which is what animates it and what the port translates.
		Assert.IsTrue( named.Any( x => x.EndsWith( ".vanmgrph", StringComparison.OrdinalIgnoreCase ) ),
			"no animation graph among what the character names: " + string.Join( ", ", named.Take( 10 ) ) );

		//And its materials, which is what it is drawn with.
		Assert.IsTrue( named.Any( x => x.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase ) ),
			"no materials either, so this is finding nothing rather than filtering" );

		//Paths from a content root. Anything else is somebody's own machine or a word that happened to end in the right four letters.
		foreach ( var path in named )
		{
			Assert.IsFalse( path.Contains( ':' ), $"'{path}' is a path on whoever compiled it" );
			Assert.IsFalse( path.StartsWith( '/' ), $"'{path}' is rooted" );
			Assert.IsTrue( path.Contains( '/' ), $"'{path}' names no folder" );
		}
	}

	//And the graph it names is one the port can find and translate.
	[TestMethod]
	public void TheGraphItNamesIsTheOneThatIsThere()
	{
		if ( Citizen is null )
		{
			Assert.Inconclusive( "s&box is not installed here" );
			return;
		}

		var install = SBoxBaseContent.Find();

		var graph = SBoxBaseContent.Names( Citizen )
			.FirstOrDefault( x => x.EndsWith( ".vanmgrph", StringComparison.OrdinalIgnoreCase ) );

		Assert.IsNotNull( graph );

		//Where the port looks: under some addon's assets, compiled.
		var found = Directory.EnumerateDirectories( Path.Combine( install, "addons" ) )
			.Select( x => Path.Combine( x, "Assets", graph.Replace( '/', Path.DirectorySeparatorChar ) + "_c" ) )
			.FirstOrDefault( File.Exists );

		Assert.IsNotNull( found, $"the character names {graph}, and it is nowhere under any addon" );
	}
}
