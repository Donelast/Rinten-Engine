using Editor;
using Rinten;
using Rinten.Internal;

namespace EngineTests;

//DisableIfAttribute keeps a row visible but read-only.
[TestClass]
[DoNotParallelize]
public class EditorDisableIfTests
{
	class Sample
	{
		public bool Gate { get; set; } = true;

		[DisableIf( nameof( Gate ), true )]
		public bool Value { get; set; }
	}

	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();

		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;

		GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( typeof( EditorBackend ).Assembly, true );
		GlobalToolsNamespace.EditorTypeLibrary.AddAssembly( typeof( EditorDisableIfTests ).Assembly, true );
	}

	[TestMethod]
	public void ShouldEnableIsFalseWhenDisableIfMatches()
	{
		var target = new Sample { Gate = true };
		var so = target.GetSerialized();
		var value = so.GetProperty( nameof( Sample.Value ) );

		Assert.IsFalse( value.ShouldEnable() );

		target.Gate = false;
		Assert.IsTrue( value.ShouldEnable() );
	}

	[TestMethod]
	public void ABoolControlStartsReadOnlyWhenDisableIfMatches()
	{
		var target = new Sample { Gate = true };
		var so = target.GetSerialized();
		var value = so.GetProperty( nameof( Sample.Value ) );

		var control = new BoolControlWidget( value );

		Assert.IsTrue( control.ReadOnly );

		target.Gate = false;
		control.ReadOnly = !value.ShouldEnable();

		Assert.IsFalse( control.ReadOnly );

		control.Destroy();
	}
}
