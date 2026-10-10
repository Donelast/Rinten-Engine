using Editor;
using Rinten;
using Rinten.UI;

namespace EngineTests;

//Razor @ref panels must defer child builds until the tree exists.
[TestClass]
[DoNotParallelize]
public class EditorInspectorDeferredTests
{
	[TestInitialize]
	public void OnPanels()
	{
		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;
		if ( Theme.RowHeight <= 0.0f ) Theme.RowHeight = 28.0f;
	}

	static void Arrange( Panel panel, float width = 320, float height = 400 )
	{
		var root = new RootPanel();
		root.PanelBounds = new Rect( 0, 0, width, height );
		root.AddChild( panel );
		root.SettleLayout();
		root.TickInternal();
	}

	[TestMethod]
	public void DeferredContentSlotGetsChildAfterTreeRender()
	{
		var host = new DeferredContentHost();
		Arrange( host );

		var marker = new Panel();
		marker.AddClass( "marker" );
		host.SetContent( marker );

		Assert.IsTrue( host.ContentSlot.HasChildren, "content should be attached after first render" );
		Assert.IsTrue( host.ContentSlot.Children.Any( c => c.HasClass( "marker" ) ) );
	}

	[TestMethod]
	public void DeferredContentBeforeTreeRenderStillAttachesOnRender()
	{
		var host = new DeferredContentHost();
		var marker = new Panel();
		marker.AddClass( "marker" );
		host.SetContent( marker );

		Arrange( host );

		Assert.IsTrue( host.ContentSlot.HasChildren );
	}

	[TestMethod]
	public void DeferredRowHostBuildsControlAfterTreeRender()
	{
		var row = new DeferredRowHost();
		var host = new Panel();
		host.AddChild( row );
		row.Initialize( "test" );
		Arrange( host );

		Assert.IsTrue( row.ControlSlot.HasChildren );
	}

	[TestMethod]
	public void DeferredScrollBodyGetsChildrenAfterTreeRender()
	{
		var panel = new DeferredScrollPanel();
		Arrange( panel );

		Assert.IsTrue( panel.ScrollSlot.HasChildren, "scroll body should be populated after render" );
		Assert.IsTrue( panel.ScrollSlot.Children.Count() >= 2 );
	}

	sealed class DeferredContentHost : Panel
	{
		public Panel ContentSlot { get; private set; }
		Panel _pending;

		public DeferredContentHost()
		{
			ContentSlot = AddChild<Panel>( "content" );
		}

		public void SetContent( Panel panel )
		{
			_pending = panel;
			if ( ContentSlot is not null )
			{
				ContentSlot.DeleteChildren();
				if ( panel is not null )
					ContentSlot.AddChild( panel );
				_pending = null;
			}
		}

		protected override void OnAfterTreeRender( bool firstTime )
		{
			base.OnAfterTreeRender( firstTime );

			if ( _pending is not null && ContentSlot is not null )
			{
				ContentSlot.DeleteChildren();
				ContentSlot.AddChild( _pending );
				_pending = null;
			}
		}
	}

	sealed class DeferredRowHost : Panel
	{
		public Panel ControlSlot { get; private set; }
		string _value;
		bool _needsControl;

		public DeferredRowHost()
		{
			ControlSlot = AddChild<Panel>( "right" );
		}

		public void Initialize( string value )
		{
			_value = value;
			BuildControl();
		}

		void BuildControl()
		{
			if ( ControlSlot is null )
			{
				_needsControl = true;
				return;
			}

			_needsControl = false;
			ControlSlot.DeleteChildren();
			var label = ControlSlot.AddChild<Rinten.UI.Label>();
			label.Text = _value;
		}

		protected override void OnAfterTreeRender( bool firstTime )
		{
			base.OnAfterTreeRender( firstTime );
			if ( firstTime || _needsControl )
				BuildControl();
		}

		public override void Tick()
		{
			base.Tick();
			if ( _needsControl )
				BuildControl();
		}
	}

	sealed class DeferredScrollPanel : Panel
	{
		public Panel ScrollSlot { get; private set; }
		bool _needsRebuild;

		public DeferredScrollPanel()
		{
			ScrollSlot = AddChild<Panel>( "scroll" );
		}

		void Rebuild()
		{
			if ( ScrollSlot is null )
			{
				_needsRebuild = true;
				return;
			}

			_needsRebuild = false;
			ScrollSlot.DeleteChildren();
			ScrollSlot.AddChild<Panel>( "header" );
			ScrollSlot.AddChild<Panel>( "body" );
		}

		protected override void OnAfterTreeRender( bool firstTime )
		{
			base.OnAfterTreeRender( firstTime );
			if ( firstTime ) Rebuild();
		}

		public override void Tick()
		{
			base.Tick();
			if ( _needsRebuild ) Rebuild();
		}
	}
}
