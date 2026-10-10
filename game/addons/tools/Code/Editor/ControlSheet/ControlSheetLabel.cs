using Rinten;
using Rinten.UI;

namespace Editor;

//A draggable label.
class ControlSheetLabel : Widget
{
	public SerializedProperty Property { get; private set; }
	private readonly Label title;
	private Drag _drag;
	Panel dragVeil;

	public ControlSheetLabel( SerializedProperty property )
	{
		Property = property;

		Layout = Layout.Row();
		title = Layout.Add( new Label( property.DisplayName ) );
		title.Alignment = TextFlag.LeftCenter;
		title.Color = Theme.InkDim;

		var wanted = title.WantedSize();
		MinimumWidth = MathF.Max( wanted.x, 8 );
		MinimumHeight = Theme.RowHeight;
		HorizontalSizeMode = SizeMode.Default;

		IsDraggable = IsDraggableProperty( property );
	}

	public override void SyncSurfaceContent()
	{
		if ( ContentPanel is null ) return;

		if ( dragVeil is null )
		{
			dragVeil = new Panel();
			dragVeil.AddClass( "drag-veil" );
			dragVeil.AddClass( "hidden" );
			ContentPanel.AddChild( dragVeil );
		}

		dragVeil.SetClass( "hidden", !_drag.IsValid() );
	}

	protected override Vector2 SizeHint()
	{
		var inner = title.WantedSize();
		var edges = ContentMargins.EdgeSize;

		return new Vector2( inner.x + edges.x, MathF.Max( inner.y + edges.y, Theme.RowHeight ) );
	}

	private static bool IsDraggableProperty( SerializedProperty property )
	{
		return property.FindPathInScene() is not null;
	}

	protected override void OnMouseEnter()
	{
		base.OnMouseEnter();
		title.Color = Theme.Ink;
	}

	protected override void OnMouseLeave()
	{
		base.OnMouseLeave();
		title.Color = Theme.InkDim;
	}

	protected override void OnDragStart()
	{
		base.OnDragStart();

		_drag = new Drag( this )
		{
			Data = { Object = Property, Text = Property.As.String }
		};

		_drag.Execute();
	}
}
