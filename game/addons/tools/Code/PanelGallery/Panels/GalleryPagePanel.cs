using Rinten.UI;

namespace Editor.Gallery;

using Label = Rinten.UI.Label;

/// <summary>The page in the middle: a heading and a card for every example on the section open.</summary>
[StyleSheet( "GalleryPagePanel.cs.scss" )]
public sealed class GalleryPagePanel : Panel
{
	readonly GallerySession session;
	readonly Label title;
	readonly Label subtitle;
	readonly Panel cards;
	readonly List<GalleryCardPanel> cardList = new();

	public GalleryPagePanel( GallerySession session )
	{
		this.session = session;

		AddClass( "gallery-page" );

		var header = AddChild<Panel>( "page-header" );
		title = header.AddChild<Label>( "title" );
		subtitle = header.AddChild<Label>( "description" );

		cards = AddChild<Panel>( "cards" );

		session.SectionChanged += Show;
		session.SelectionChanged += OnSelectionChanged;
		session.PageRebuildRequested += Rebuild;

		Show( session.CurrentSection );
	}

	public override void OnDeleted()
	{
		base.OnDeleted();

		session.SectionChanged -= Show;
		session.SelectionChanged -= OnSelectionChanged;
		session.PageRebuildRequested -= Rebuild;
	}

	void Show( GallerySection section )
	{
		cards.DeleteChildren( true );
		cards.ScrollOffset = Vector2.Zero;
		cardList.Clear();

		if ( section is null )
		{
			title.Text = "Nothing to show";
			subtitle.Text = "No method is marked [PanelGallery].";
			return;
		}

		var filter = GalleryCommands.CardFilter;
		var shown = string.IsNullOrEmpty( filter )
			? section.Entries
			: section.Entries.Where( x => x.Id.Contains( filter, StringComparison.OrdinalIgnoreCase ) ).ToList();

		title.Text = section.Title;
		subtitle.Text = shown.Count == 1 ? "1 example" : $"{shown.Count} examples";

		if ( shown.Count != section.Entries.Count )
			subtitle.Text += $" of {section.Entries.Count}, filtered by '{filter}' (panelgallery_filter)";

		//A few cards a frame rather than the whole page in one go: the page is on screen at once, the frame that opens it is not a
		//stall, and a page of a hundred property editors is not a hundred fresh trees laid out and drawn together on their first frame.
		pending.Clear();

		foreach ( var entry in shown )
			pending.Enqueue( entry );

		BuildSome();
	}

	//Cards still to build for the page open, in order.
	readonly Queue<GalleryEntry> pending = new();

	//How many cards one frame builds.
	const int CardsPerFrame = 4;

	public override void Tick()
	{
		base.Tick();

		if ( pending.Count > 0 )
			BuildSome();
	}

	void BuildSome()
	{
		for ( var i = 0; i < CardsPerFrame && pending.Count > 0; i++ )
		{
			var entry = pending.Dequeue();

			var card = new GalleryCardPanel( entry, session.Options )
			{
				Pressed = () => session.Select( entry ),
			};

			cards.AddChild( card );
			cardList.Add( card );
		}

		SyncSelection();
	}

	//Every card built again, on the same page - what the menu's Rebuild Page does.
	void Rebuild() => Show( session.CurrentSection );

	/// <summary>Scrolls the cards to a height from the top, for driving the page from the console.</summary>
	public void ScrollTo( float y ) => cards.ScrollOffset = new Vector2( 0, MathF.Max( 0, y ) );

	/// <summary>How far down the cards are, and how far they go.</summary>
	public (float Offset, float Size) Scroll => (cards.ScrollOffset.y, cards.ScrollSize.y);

	void OnSelectionChanged( GalleryEntry _ ) => SyncSelection();

	void SyncSelection()
	{
		GalleryCardPanel selected = null;

		foreach ( var card in cardList )
		{
			var isSelected = card.Entry == session.SelectedEntry;

			card.SetSelected( isSelected );

			if ( isSelected ) selected = card;
		}

		session.SetReadout( selected );
	}
}
