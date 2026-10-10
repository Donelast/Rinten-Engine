using Editor;
using EditorTests;
using Rinten;
using Rinten.Internal;
using System;
using static Rinten.Internal.GlobalToolsNamespace;

namespace EngineTests;

[TestClass]
public class EditorInspectorControlSheetTests
{
	[TestMethod]
	public void MultiSerializedObjectCannotRoundTripThroughTypeLibrary()
	{
		var a = new ControlWidgetsTest.Example { Int32 = 1 };
		var b = new ControlWidgetsTest.Example { Int32 = 2 };
		var mso = new MultiSerializedObject();
		mso.Add( GlobalToolsNamespace.EditorTypeLibrary.GetSerializedObject( a ) );
		mso.Add( GlobalToolsNamespace.EditorTypeLibrary.GetSerializedObject( b ) );
		mso.Rebuild();

		Assert.ThrowsException<Exception>( () => Game.TypeLibrary.GetSerializedObject( mso ) );
		Assert.IsTrue( mso.ToList().Count > 0, "sheet should use SerializedObject.ToList() directly" );
	}
}
