using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class EditorSuspendUpdatesTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;
	}

	[TestMethod]
	public void SuspendUpdatesSettlesFlexSubtree()
	{
		var holder = new Widget( null );
		holder.Size = new Vector2( 320, 200 );
		holder.Layout = Layout.Column();

		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 320, 200 );
		root.AddChild( holder.Surface );

		using ( SuspendUpdates.For( holder ) )
		{
			for ( int i = 0; i < 8; i++ )
			{
				var row = new Widget( holder )
				{
					MinimumHeight = Theme.RowHeight,
					HorizontalSizeMode = SizeMode.Flexible
				};
				holder.Layout.Add( row );
			}
		}

		var (passes, stable) = holder.SettleFlexSubtree();

		Assert.IsTrue( stable, "flex subtree should converge after suspend rebuild" );
		Assert.IsTrue( passes <= Widget.MaxArrangePasses, "settle should not hit arrange limit" );
	}

	[TestMethod]
	public void SettleFlexSubtreeConvergesForNestedColumn()
	{
		var holder = new Widget( null );
		holder.Size = new Vector2( 280, 160 );

		var column = new Widget( holder );
		column.Layout = Layout.Column();
		column.Layout.Spacing = 2;
		column.HorizontalSizeMode = SizeMode.Flexible;
		holder.Layout = Layout.Column();
		holder.Layout.Add( column, 1 );

		for ( int i = 0; i < 5; i++ )
			column.Layout.Add( new Widget( column ) { MinimumHeight = Theme.RowHeight } );

		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, 280, 160 );
		root.AddChild( holder.Surface );

		var (passes, stable) = holder.SettleFlexSubtree();

		Assert.IsTrue( stable );
		Assert.IsTrue( passes >= 1 );
	}
}
