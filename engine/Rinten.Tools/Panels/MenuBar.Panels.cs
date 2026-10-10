using Rinten;
using Rinten.UI;
using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>The strip of menu names along the top of a window, drawn by us.</summary>
[FramesOnDemand]
public partial class MenuBar
{
	private readonly List<Rect> titleRects = new();
	private readonly List<MenuBarTitleRow> titleRows = new();

	private int hovered = -1;
	private int open = -1;

	//Where the names end moved - which is where the window's drag strip starts. See TitleBar.
	internal Action TitlesMoved;

	//Which name has its menu down, or minus one.
	internal int OpenIndex => open;

	//The room either side of a name - MenuBar::item padding: 4px 8px.
	private const float Padding = 8.0f;

	//What the names are drawn at - MenuBar font-weight: 500.
	private const int Weight = 500;

	private Panel titlesPanel;

	private bool titlesHidden;

	//Whether the menu names are left out of the strip - the editor's own bar, while it has no project for any of
	//them to act on. The strip is still there: its buttons and the window's drag area are the bar's, not the menus'.
	internal bool TitlesHidden
	{
		get => titlesHidden;
		set
		{
			if ( titlesHidden == value ) return;

			titlesHidden = value;

			if ( value && open >= 0 )
			{
				Menu.CloseAllPanels();
				open = -1;
			}

			ApplyTitlesHidden();
			MeasureTitleRects();
			TitlesMoved?.Invoke();
			Update();
		}
	}

	private void ApplyTitlesHidden()
	{
		if ( titlesPanel is null || !titlesPanel.IsValid() ) return;

		titlesPanel.Style.Display = titlesHidden ? DisplayMode.None : null;
	}

	protected override Vector2 SizeHint()
	{
		if ( !OnPanels ) return base.SizeHint();

		return new Vector2( Size.x, Theme.RowHeight + 8 );
	}

	private Widget trailing;

	//Something of the window's own, sat in the right half of the strip.
	internal Widget TrailingWidget
	{
		get => trailing;
		set
		{
			if ( trailing == value ) return;

			trailing = value;
			trailing?.SetPanelParent( this );

			MarkNeedsArrange();
		}
	}

	private protected override void OnSurfaceCreated()
	{
		base.OnSurfaceCreated();

		if ( !OnPanels || Surface is null ) return;

		titlesPanel = Surface.AddChild<Panel>( "menubar-titles" );
		titlesPanel.AddClass( "menubar-titles" );

		ApplyTitlesHidden();

		SyncTitlePanels();
	}

	public override void SyncSurfaceContent()
	{
		if ( !OnPanels ) return;

		var endWas = titleRects.Count > 0 ? titleRects[^1].Right : -1.0f;

		SyncTitlePanels();
		UpdateTitleStyles();
		MeasureTitleRects();

		//A menu added changes nothing about how the bar is arranged, only where the names end - so it is said rather than waited for.
		var end = titleRects.Count > 0 ? titleRects[^1].Right : -1.0f;

		if ( end != endWas ) TitlesMoved?.Invoke();
	}

	private protected override void OnArrange()
	{
		base.OnArrange();

		if ( !OnPanels ) return;

		SyncTitlePanels();

		if ( trailing.IsValid() )
		{
			var left = MathF.Round( Size.x * 0.5f );

			trailing.Position = new Vector2( left, 0 );
			trailing.Size = new Vector2( MathF.Max( Size.x - left, 0.0f ), Size.y );
		}

		MeasureTitleRects();
	}

	//Where each name sits along the strip, measured if it has not been drawn yet.
	internal IReadOnlyList<Rect> TitleRects
	{
		get
		{
			if ( titleRects.Count != Menus.Count ) MeasureTitleRects();

			return titleRects;
		}
	}

	//How far along the strip the last menu name reaches.
	internal float TitlesEnd => !titlesHidden && TitleRects.Count > 0 ? TitleRects[^1].Right : 4.0f;

	void SyncTitlePanels()
	{
		if ( titlesPanel is null || !titlesPanel.IsValid() ) return;
		if ( titleRows.Count == Menus.Count ) return;

		titlesPanel.DeleteChildren();
		titleRows.Clear();
		titleRects.Clear();

		for ( var i = 0; i < Menus.Count; i++ )
		{
			var row = titlesPanel.AddChild<MenuBarTitleRow>( "menubar-title" );
			row.Init( this, i, Menus[i].Title );
			titleRows.Add( row );
		}
	}

	void MeasureTitleRects()
	{
		titleRects.Clear();

		if ( Surface is null || !Surface.IsValid() ) return;

		var origin = Surface.Box.Rect.Position;
		var height = MathF.Max( Size.y, Theme.RowHeight );
		var at = 4.0f;

		for ( var i = 0; i < titleRows.Count && i < Menus.Count; i++ )
		{
			var row = titleRows[i];
			if ( !row.IsValid() )
			{
				var fallback = EditorText.Measure( Menus[i].Title, EditorText.Size, null, Weight ).x + (Padding * 2);
				titleRects.Add( new Rect( at, 0, fallback, height ) );
				at += fallback;
				continue;
			}

			var box = row.Box.Rect;
			var local = box.Position - origin;
			var rowWidth = box.Width > 0
				? box.Width
				: EditorText.Measure( Menus[i].Title, EditorText.Size, null, Weight ).x + (Padding * 2);

			titleRects.Add( new Rect( local.x > 0 ? local.x : at, 0, rowWidth, height ) );
			at = local.x > 0 ? local.x + rowWidth : at + rowWidth;
		}
	}

	void UpdateTitleStyles()
	{
		for ( var i = 0; i < titleRows.Count; i++ )
		{
			var row = titleRows[i];
			if ( !row.IsValid() ) continue;

			row.SetClass( "open", i == open );
			row.SetClass( "hover", i == hovered );
		}
	}

	internal void PressTitle( int index )
	{
		if ( !OnPanels ) return;

		if ( titlesHidden ) return;

		//A name in the bar is a panel of its own and hears the press before the widget does, so it asks for itself whether a modal is holding the window.
		if ( Editor.Window.BlocksWidgetInput( this ) ) return;

		if ( index < 0 || index >= Menus.Count ) return;

		if ( index == open )
		{
			Menu.CloseAllPanels();
			open = -1;
			UpdateTitleStyles();
			Update();
			return;
		}

		Open( index );
	}

	internal void SetTitleHovered( int index )
	{
		if ( hovered == index ) return;

		hovered = index;
		UpdateTitleStyles();
		Update();
	}

	internal void ClearTitleHover( int index )
	{
		if ( hovered != index ) return;

		hovered = -1;
		UpdateTitleStyles();
		Update();
	}

	protected override void OnPaint()
	{
		if ( !OnPanels ) base.OnPaint();
	}

	//Which name is under a point, or minus one.
	private int TitleAt( Vector2 local )
	{
		for ( var i = 0; i < titleRects.Count; i++ )
		{
			if ( titleRects[i].IsInside( local ) ) return i;
		}

		return -1;
	}

	protected override void OnMouseMove( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMouseMove( e ); return; }

		var was = hovered;

		hovered = TitleAt( e.LocalPosition );

		if ( was != hovered ) UpdateTitleStyles();
	}

	protected override void OnMouseLeave()
	{
		if ( !OnPanels ) { base.OnMouseLeave(); return; }

		hovered = -1;
		UpdateTitleStyles();
		Update();
	}

	//Lets go of the name whose menu was closed some other way than through the bar - a click away, a
	//key. Asked for while a menu is down (see Open), which is the only time there is anything to notice.
	private protected override void OnWidgetFrame()
	{
		base.OnWidgetFrame();

		if ( !OnPanels ) return;
		if ( open < 0 ) return;

		if ( open < Menus.Count && Menus[open].IsOpen )
		{
			RequestFrame();
			return;
		}

		open = -1;

		UpdateTitleStyles();
		Update();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !OnPanels ) { base.OnMousePress( e ); return; }

		var at = TitleAt( e.LocalPosition );

		if ( at < 0 ) return;

		PressTitle( at );
	}

	//Opens one of the menus under its own name.
	private void Open( int index )
	{
		if ( index < 0 || index >= Menus.Count ) return;

		Menu.CloseAllPanels();

		open = index;
		RequestFrame();

		var menu = Menus[index];
		var rect = titleRects[index];

		menu.Owner = this;
		menu.OpenAt( ToWindow( new Vector2( rect.Left, Size.y ) ) );

		UpdateTitleStyles();
		Update();
	}

	//A point of this widget's, in the window's own pixels.
	private Vector2 ToWindow( Vector2 local )
	{
		for ( Widget widget = this; widget is not null; widget = widget.Parent )
			local += widget.Position;

		return local;
	}
}

//One menu name in the bar; opens its menu on press.
internal sealed class MenuBarTitleRow : Panel
{
	MenuBar bar;
	int index;

	public void Init( MenuBar bar, int index, string title )
	{
		this.bar = bar;
		this.index = index;
		AddClass( "menubar-title" );

		var label = AddChild<Rinten.UI.Label>();
		label.Text = title;
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );
		bar?.PressTitle( index );
		e.StopPropagation();
	}

	protected override void OnMouseOver( MousePanelEvent e )
	{
		base.OnMouseOver( e );
		bar?.SetTitleHovered( index );
	}

	protected override void OnMouseOut( MousePanelEvent e )
	{
		base.OnMouseOut( e );
		bar?.ClearTitleHover( index );
	}
}
