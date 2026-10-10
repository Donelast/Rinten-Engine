using Rinten.UI;
using System;
using System.Collections.Generic;

namespace Editor.MovieMaker;

#nullable enable

//Rows are pooled and placed by hand rather than laid out one per item: the history is as long as the session is old, and only the dozen rows the scroll is over are worth building.
/// <summary>The list of changes, newest at the top, which you can click to go back to.</summary>
[StyleSheet( "HistoryListPanel.cs.scss" )]
public sealed class HistoryListPanel : Panel
{
	private const float RowHeight = 32f;
	private const int Overscan = 2;

	private readonly Session _session;
	private readonly Panel _content;
	private readonly List<HistoryRowPanel> _rows = new();

	private int _stateHash;
	private float _contentHeight = -1f;

	public HistoryListPanel( Session session )
	{
		_session = session;

		AddClass( "history-list" );

		_content = AddChild<Panel>( "content" );
	}

	public override void Tick()
	{
		base.Tick();

		var hash = HashCode.Combine( _session.History.Count, _session.History.Index );

		if ( hash != _stateHash )
		{
			_stateHash = hash;

			SetContentHeight( _session.History.Count * RowHeight );
		}

		UpdateVisibleRows();
	}

	private void SetContentHeight( float height )
	{
		if ( Math.Abs( _contentHeight - height ) < 0.5f ) return;

		_contentHeight = height;

		_content.Style.Height = Length.Pixels( height );
	}

	private void UpdateVisibleRows()
	{
		var history = _session.History;
		var count = history.Count;

		if ( count == 0 )
		{
			foreach ( var row in _rows ) row.Park();
			return;
		}

		var scale = ScaleFromScreen;
		var viewTop = ScrollOffset.y * scale;
		var viewHeight = Box.Rect.Height * scale;

		var first = Math.Max( 0, (int)(viewTop / RowHeight) - Overscan );
		var last = Math.Min( count - 1, (int)((viewTop + viewHeight) / RowHeight) + Overscan );

		EnsurePool( last - first + 1 );

		var slot = 0;

		for ( var index = first; index <= last; index++, slot++ )
		{
			//Newest first: row 0 is the last thing that happened.
			var item = history[count - 1 - index];

			_rows[slot].Show( item, index * RowHeight, history.Index );
		}

		for ( ; slot < _rows.Count; slot++ )
		{
			_rows[slot].Park();
		}
	}

	private void EnsurePool( int wanted )
	{
		while ( _rows.Count < wanted )
		{
			var row = new HistoryRowPanel { Parent = _content };

			_rows.Add( row );
		}
	}
}

/// <summary>One change in the history list.</summary>
public sealed class HistoryRowPanel : Panel
{
	private readonly IconPanel _icon;
	private readonly Rinten.UI.Label _title;
	private readonly Rinten.UI.Label _time;

	private IHistoryItem? _item;

	public HistoryRowPanel()
	{
		AddClass( "history-row" );
		SetClass( "hidden", true );

		_icon = AddChild<IconPanel>( "icon" );
		_title = AddChild<Rinten.UI.Label>( "title" );
		_time = AddChild<Rinten.UI.Label>( "time" );
	}

	/// <summary>Puts this row over one item, at a height of its own.</summary>
	public void Show( IHistoryItem item, float top, int currentIndex )
	{
		_item = item;

		SetClass( "hidden", false );
		Style.Top = Length.Pixels( top );

		_icon.Text = item.Icon;
		_title.Text = item.Title;
		_time.Text = item.Time.ToLocalTime().ToString( "HH:mm:ss" );

		SetClass( "current", item.Index == currentIndex );
		SetClass( "undone", item.Index > currentIndex );
	}

	/// <summary>Takes this row out of the list without destroying it.</summary>
	public void Park()
	{
		_item = null;

		SetClass( "hidden", true );
	}

	protected override void OnClick( MousePanelEvent e )
	{
		base.OnClick( e );

		_item?.Apply();
	}
}
