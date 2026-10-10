using Rinten.Engine;
using System;

namespace Editor;

//The document level of a workspace: one tab per open asset, each backed by a complete editor instance of its own.
//The workspace bar answers "which editor am I in", this bar answers "which of its documents am I looking at".
internal sealed class DocumentTabWidget : Widget
{
	/// <summary>Fired when the last document closes - the workspace has nothing left to show.</summary>
	public Action OnEmpty;

	readonly Widget _tabBar;
	readonly ContentHost _content;
	readonly List<Document> _documents = new();

	/// <summary>How many documents are open - the number on the tool strip's icon.</summary>
	internal int Count => _documents.Count;

	Document _current;

	internal sealed class Document
	{
		public string Key;
		public string Title;

		/// <summary>The editor instance - a hidden window husk once its parts are embedded.</summary>
		public Widget Editor;

		/// <summary>What actually fills the workspace: the editor's parts in a column.</summary>
		public Widget Content;

		public DocumentTab Tab;
	}

	public DocumentTabWidget( Widget parent ) : base( parent )
	{
		Name = "Documents";
		Layout = Layout.Column();

		_tabBar = Layout.Add( new TabBar( this ) );
		_content = Layout.Add( new ContentHost( this ), 1 );
	}

	/// <summary>Show the document for this asset, creating it through <paramref name="factory"/> if it isn't open. The factory returns the editor instance and its embedded content; returning default means the editor couldn't be created.</summary>
	public IAssetEditor Open( Asset asset, Func<(Widget Editor, Widget Content)> factory )
	{
		var key = asset.Path?.ToLowerInvariant() ?? asset.Name;

		if ( _documents.FirstOrDefault( x => x.Key == key ) is { } existing )
		{
			MakeCurrent( existing );
			return existing.Editor as IAssetEditor;
		}

		var (editor, content) = factory();

		if ( !editor.IsValid() || !content.IsValid() )
			return null;

		var document = new Document
		{
			Key = key,
			Title = asset.Name,
			Editor = editor,
			Content = content
		};

		document.Tab = new DocumentTab( _tabBar, this, document );

		content.Parent = _content;
		content.Visible = false;

		_documents.Add( document );
		RebuildTabBar();
		WorkspaceArea.Current?.TabBar.UpdateTabs();
		MakeCurrent( document );

		return editor as IAssetEditor;
	}

	public void Close( Document document )
	{
		var index = _documents.IndexOf( document );
		if ( index < 0 ) return;

		_documents.Remove( document );
		WorkspaceArea.Current?.TabBar.UpdateTabs();

		document.Content?.Destroy();
		document.Editor?.Destroy();
		document.Tab?.Destroy();

		if ( _documents.Count == 0 )
		{
			_current = null;
			OnEmpty?.Invoke();
			return;
		}

		if ( _current == document )
		{
			_current = null;
			MakeCurrent( _documents[Math.Min( index, _documents.Count - 1 )] );
		}

		RebuildTabBar();
	}

	public override void OnDestroyed()
	{
		base.OnDestroyed();

		//The husk windows aren't our children, so destroying the widget tree wouldn't reach them. One that
		//throws on its way out is its own problem: it must not keep the tabs after it from being torn down too.
		foreach ( var document in _documents )
		{
			try
			{
				document.Editor?.Destroy();
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"'{document.Title}' threw on the way out" );
			}
		}

		_documents.Clear();
	}

	internal bool IsCurrent( Document document ) => _current == document;

	void MakeCurrent( Document document )
	{
		if ( _current == document ) return;

		_current = document;
		_content.Show( document.Content );
		_tabBar.Update();
	}

	void RebuildTabBar()
	{
		_tabBar.Layout.Clear( false );

		foreach ( var document in _documents )
		{
			_tabBar.Layout.Add( document.Tab );
		}

		_tabBar.Layout.AddStretchCell();
		_tabBar.Update();
	}

	sealed class TabBar : Widget
	{
		public TabBar( Widget parent ) : base( parent )
		{
			Layout = Layout.Row();
			FixedHeight = 24;
		}

		protected override void OnPaint()
		{
			//The strip itself is nothing: a run of tabs standing on the window, not a bar of its own
			//colour behind them. The one that is open is what carries a surface - see DocumentTab.
		}
	}

	internal sealed class DocumentTab : Widget
	{
		readonly DocumentTabWidget _owner;
		readonly Document _document;
		readonly IconButton _close;

		bool IsCurrent => _owner.IsCurrent( _document );

		//The asset's name, and a star while its editor holds edits not yet saved.
		string Shown => _document.Editor is IAssetEditor { HasUnsavedChanges: true } ? $"{_document.Title} *" : _document.Title;

		string _painted;

		public override bool UsesCanvasDraw => true;

		public DocumentTab( Widget parent, DocumentTabWidget owner, Document document ) : base( parent )
		{
			_owner = owner;
			_document = document;
			Cursor = CursorShape.Finger;

			_close = new IconButton( "close", () => _owner.Close( _document ), this )
			{
				ToolTip = "Close",
				Background = Color.Transparent,
				Foreground = Theme.TextLight,
				IconSize = 8,
				FixedWidth = 14,
				FixedHeight = 14
			};
		}

		protected override Vector2 SizeHint()
		{
			Paint.SetDefaultFont();
			return new Vector2( Paint.MeasureText( Shown ).x + 44, 24 );
		}

		protected override void OnPaint()
		{
			var background = IsCurrent ? Theme.TabBackground
				: Paint.HasMouseOver ? Theme.Slab
				: Color.Transparent;

			var round = Theme.TabRadius;

			//Round along the top, square where it meets the document under it.
			Paint.ClearPen();
			Paint.SetBrush( background );
			Paint.DrawRect( LocalRect, round, round, 0.0f, 0.0f );

			Paint.SetPen( IsCurrent ? Theme.Text : Theme.TextLight );
			Paint.SetDefaultFont();
			_painted = Shown;
			Paint.DrawText( LocalRect.Shrink( 8, 0, 24, 0 ), _painted, TextFlag.LeftCenter );
		}

		//Drawn again, and measured again, when the star comes or goes.
		[EditorEvent.Frame]
		void WatchSaved()
		{
			if ( _painted is null || _painted == Shown ) return;

			UpdateGeometry();
			Update();
		}

		protected override void OnResize()
		{
			base.OnResize();
			_close.Position = new Vector2( Width - 18, (Height - _close.Height) * 0.5f );
		}

		protected override void OnMousePress( MouseEvent e )
		{
			base.OnMousePress( e );

			if ( e.LeftMouseButton )
				_owner.MakeCurrent( _document );

			if ( e.MiddleMouseButton )
				_owner.Close( _document );
		}
	}

	//Same stacked hosting the workspace and scene tabs use - switching documents swaps visibility, never relayouts.
	sealed class ContentHost : Widget
	{
		Widget _current;

		public ContentHost( Widget parent ) : base( parent )
		{
		}

		public void Show( Widget widget )
		{
			if ( _current == widget ) return;

			var outgoing = _current;
			_current = widget;

			widget.Position = 0;
			widget.Size = Size;
			widget.Visible = true;

			if ( !outgoing.IsValid() || !outgoing.Visible )
				return;

			widget.Lower();

			EngineLoop.DisposeAtFrameEnd( new Rinten.Utility.DisposeAction( () =>
			{
				if ( outgoing.IsValid() && outgoing != _current )
					outgoing.Visible = false;

				if ( _current.IsValid() )
					_current.Raise();
			} ) );
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.TabBackground );
			Paint.DrawRect( LocalRect );
		}

		protected override void OnResize()
		{
			foreach ( var child in Children )
			{
				if ( !child.Visible ) continue;

				child.Position = 0;
				child.Size = Size;
			}
		}
	}
}
