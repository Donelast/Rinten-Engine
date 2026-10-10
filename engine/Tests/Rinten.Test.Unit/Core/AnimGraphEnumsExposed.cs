using System;
using System.Collections.Generic;
using System.Linq;
using Rinten;

namespace UnitTests.Core;

//Anim graph builder enums must be exposed to the type library, or their editors show empty rows
[TestClass]
public class AnimGraphEnumsExposedTest
{
	[TestMethod]
	public void EveryGraphEnumIsExposedToTheEditor()
	{
		var enums = typeof( AnimGraphBuilder )
			.GetNestedTypes()
			.Where( type => type.IsEnum )
			.ToArray();

		Assert.AreNotEqual( 0, enums.Length, "the builder declares enums" );

		var hidden = new List<string>();

		foreach ( var type in enums )
		{
			//By name: the attribute is internal to the reflection assembly, and what matters here is the same thing ShouldExposeType reads.
			var exposed = type.GetCustomAttributes( false )
				.Any( attribute => attribute.GetType().Name is "ExposeAttribute" or "LibraryAttribute" );

			if ( !exposed ) hidden.Add( type.Name );
		}

		Assert.AreEqual( 0, hidden.Count,
			$"no editor can be made for these, so their rows come up empty: {string.Join( ", ", hidden )}" );
	}
}
