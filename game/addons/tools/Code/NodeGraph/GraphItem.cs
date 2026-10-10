using Rinten.UI;

namespace Editor.NodeEditor;

/// <summary>Anything placed in a graph; position and size are in graph units, the world does pan/zoom</summary>
public abstract class GraphItem : Panel, IGraphSelectable
{
	/// <summary>The view this belongs to.</summary>
	public GraphView Graph { get; }

	private Vector2 _position;
	private Vector2 _size;
	private float _zIndex;
	private bool _selected;

	/// <summary>Where this is in the graph.</summary>
	public Vector2 Position
	{
		get => _position;
		set
		{
			if ( _position == value ) return;

			_position = value;

			ApplyGeometry();
			OnPositionChanged();
		}
	}

	/// <summary>How big this is, in the graph's units.</summary>
	public Vector2 Size
	{
		get => _size;
		set
		{
			if ( _size == value ) return;

			_size = value;

			ApplyGeometry();
			OnSizeChanged();
		}
	}

	/// <summary>Where this is and how much of the graph it covers.</summary>
	public Rect SceneRect => new( _position, _size );

	/// <summary>What a rubber band has to touch to catch this.</summary>
	public virtual Rect SelectionRect => SceneRect;

	/// <summary>Whether a rubber band or a click may select it.</summary>
	public bool Selectable { get; set; }

	/// <summary>Whether dragging it moves it.</summary>
	public bool Movable { get; set; }

	/// <summary>Whether it is selected, which is a class as far as the styling is concerned.</summary>
	public bool Selected
	{
		get => _selected;
		set
		{
			if ( _selected == value ) return;

			_selected = value;

			SetClass( "selected", value );
			OnSelectionChanged();
		}
	}

	/// <summary>What is drawn over what.</summary>
	public float ZIndex
	{
		get => _zIndex;
		set
		{
			if ( _zIndex.AlmostEqual( value ) ) return;

			_zIndex = value;

			//The style takes whole numbers; the graph counts comment layers in them anyway, and half a layer would mean nothing.
			Style.ZIndex = value.FloorToInt();
		}
	}

	protected GraphItem( GraphView graph )
	{
		Graph = graph;

		AddClass( "graph-item" );

		ApplyGeometry();
	}

	//Whether this item's size is something it is told, rather than something it works out from what is in it.
	protected virtual bool WritesSize => true;

	//Writes the item's place in the graph onto its box.
	private void ApplyGeometry()
	{
		Style.Left = Length.Pixels( _position.x );
		Style.Top = Length.Pixels( _position.y );

		if ( !WritesSize ) return;

		if ( _size.x > 0.0f ) Style.Width = Length.Pixels( _size.x );
		if ( _size.y > 0.0f ) Style.Height = Length.Pixels( _size.y );
	}

	//Puts the geometry back on the box after something else changed it.
	protected void RefreshGeometry() => ApplyGeometry();

	//Takes the size the box came out at, for an item that is sized by what is in it rather than by being told.
	protected bool ReadSizeFromBox()
	{
		var size = Box.Rect.Size;

		if ( _size.AlmostEqual( size, 0.01f ) ) return false;

		_size = size;

		OnSizeChanged();

		return true;
	}

	protected virtual void OnPositionChanged() { }
	protected virtual void OnSizeChanged() { }
	protected virtual void OnSelectionChanged() { }

	/// <summary>Takes this out of the graph for good.</summary>
	public virtual void Destroy()
	{
		Graph?.ForgetItem( this );

		Delete();
	}
}
