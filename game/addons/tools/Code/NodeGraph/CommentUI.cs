using Rinten.UI;
using UILabel = Rinten.UI.Label;

namespace Editor.NodeEditor;

public enum CommentColor
{
	White,
	Red,
	Green,
	Blue,
	Yellow,
	Purple,
	Orange
}

public interface ICommentNode : INode
{
	int Layer { get; set; }
	Vector2 Size { get; set; }
	CommentColor Color { get; set; }
	string Title { get; set; }
	string Description { get; set; }
}

//A node like any other as far as the graph is concerned - it is saved, copied and deleted with them - but it has no sockets, it is sized by hand, and it takes the nodes it encloses with it when it is dragged.
/// <summary>A box drawn round part of a graph, with a name on it.</summary>
public class CommentUI : NodeUI
{
	//How wide the band round the edge is that resizes instead of moving.
	private const float GripSize = 16.0f;

	private static readonly Vector2 MinSize = new( 64.0f, 64.0f );
	private static readonly Vector2 MaxSize = new( 10000.0f, 10000.0f );

	private ICommentNode Comment => Node as ICommentNode;

	//Sized by hand rather than by what is in it.
	protected override bool WritesSize => true;

	private Panel _titleRow;
	private IconPanel _icon;
	private UILabel _title;
	private UILabel _description;

	public CommentUI( GraphView graph, ICommentNode node ) : base( graph, node )
	{
		AddClass( "comment" );

		Selectable = true;
		Movable = true;

		ZIndex = -node.Layer;
		Size = node.Size.Clamp( MinSize, MaxSize );
	}

	protected override void BuildContent()
	{
		_titleRow = AddChild<Panel>( "comment-title" );

		_icon = _titleRow.AddChild<IconPanel>( "icon" );

		_title = _titleRow.AddChild<UILabel>( "name" );
		_title.Tokenize = false;

		_description = AddChild<UILabel>( "comment-description" );
		_description.Tokenize = false;
	}

	protected override void OnRebuild()
	{
		base.OnRebuild();

		ZIndex = -Comment.Layer;
		Size = Comment.Size.Clamp( MinSize, MaxSize );
	}

	protected override void Layout()
	{
		Size = Comment.Size.Clamp( MinSize, MaxSize );
	}

	private static Color GetColor( CommentColor color ) => color switch
	{
		CommentColor.Red => "#d60000",
		CommentColor.Green => "#33b679",
		CommentColor.Blue => "#039be5",
		CommentColor.Yellow => "#f6c026",
		CommentColor.Purple => "#8e24aa",
		CommentColor.Orange => "#f5511d",
		_ => "#C2B5B5"
	};

	protected override void OnUpdate()
	{
		var comment = Comment;
		var colour = GetColor( comment.Color );

		PrimaryColor = colour.Darken( 0.1f ).Desaturate( Selected ? 0.3f : HasHovered ? 0.35f : 0.4f );

		Style.BackgroundColor = PrimaryColor.Darken( 0.7f ).WithAlpha( 0.48f );
		Style.BorderColor = Selected || HasHovered
			? colour.Lighten( 0.1f ).Desaturate( 0.3f ).WithAlpha( Selected ? 0.6f : 0.24f )
			: PrimaryColor.WithAlpha( 0.3f );

		_titleRow.Style.BackgroundColor = PrimaryColor.WithAlpha( 0.12f );

		_icon.SetClass( "hidden", DisplayInfo.Icon is null );
		_icon.Text = DisplayInfo.Icon ?? string.Empty;
		_icon.Style.FontColor = PrimaryColor.WithAlpha( 0.7f );

		_title.Text = DisplayInfo.Name ?? string.Empty;
		_title.Style.FontColor = PrimaryColor;

		var description = comment.Description ?? string.Empty;

		_description.SetClass( "hidden", string.IsNullOrEmpty( description ) );
		_description.Text = description[..Math.Min( description.Length, 300 )];
		_description.Style.FontColor = PrimaryColor.WithAlpha( 0.7f );

		Tooltip = $"<span style=\"font-size: 16px;font-weight: 900;\">{comment.Title}</span><br>{comment.Description}";
	}

	//Which edges of the box a point is near, or none of them for the middle.
	private ResizeEdges EdgesAt( Vector2 local )
	{
		var edges = ResizeEdges.None;

		if ( local.x <= GripSize ) edges |= ResizeEdges.Left;
		else if ( local.x >= Size.x - GripSize ) edges |= ResizeEdges.Right;

		if ( local.y <= GripSize ) edges |= ResizeEdges.Top;
		else if ( local.y >= Size.y - GripSize ) edges |= ResizeEdges.Bottom;

		return edges;
	}

	private static string CursorFor( ResizeEdges edges ) => edges switch
	{
		ResizeEdges.None => "move",
		ResizeEdges.Left or ResizeEdges.Right => "ew-resize",
		ResizeEdges.Top or ResizeEdges.Bottom => "ns-resize",
		ResizeEdges.Top | ResizeEdges.Left or ResizeEdges.Bottom | ResizeEdges.Right => "nwse-resize",
		_ => "nesw-resize"
	};

	protected override void OnMouseMove( MousePanelEvent e )
	{
		Style.Cursor = CursorFor( EdgesAt( MousePosition ) );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left ) return;

		var edges = EdgesAt( MousePosition );

		if ( edges != ResizeEdges.None )
		{
			e.StopPropagation();

			Graph.Select( this, alone: !GraphView.HasCtrl );
			Graph.Begin( new ResizeCommentInteraction( Graph, this, edges, Graph.CurrentPointer ), e );
			return;
		}

		//Taking hold of a comment takes hold of everything it encloses, which is the whole point of drawing a box round them.
		if ( !Selected ) TakeContents();

		base.OnMouseDown( e );
	}

	//Selects every node the box is drawn all the way round.
	private void TakeContents()
	{
		foreach ( var node in Graph.Items.OfType<NodeUI>() )
		{
			if ( ReferenceEquals( node, this ) ) continue;

			if ( SceneRect.IsInside( node.SceneRect, true ) ) Graph.Select( node, alone: false );
			else if ( !GraphView.HasCtrl ) Graph.Deselect( node );
		}
	}

	//Writes the size back to the node it belongs to.
	internal void CommitSize()
	{
		Comment.Size = Size;
		Comment.Position = Position;

		Update();
	}

	/// <summary>Puts the smaller boxes in front of the bigger ones they sit in.</summary>
	public void UpdateLayer()
	{
		var lowest = Comment.Layer;
		var changed = false;

		foreach ( var other in Graph.Items.OfType<CommentUI>() )
		{
			if ( other == this ) continue;

			if ( SceneRect.IsInside( other.SceneRect, true ) )
			{
				other.UpdateLayer();
				continue;
			}

			if ( !other.SceneRect.IsInside( SceneRect ) ) continue;
			if ( other.Comment.Layer > lowest ) continue;

			changed = true;
			lowest = other.Comment.Layer;
		}

		if ( !changed ) return;

		Comment.Layer = lowest - 1;
		ZIndex = -Comment.Layer;
	}

	/// <summary>Closes the gaps the layers pick up as boxes are moved about.</summary>
	public void NormalizeLayers()
	{
		var layer = 1;

		foreach ( var group in Graph.Items.OfType<CommentUI>().OrderBy( x => x.Comment.Layer ).GroupBy( x => x.Comment.Layer ) )
		{
			foreach ( var comment in group )
			{
				comment.Comment.Layer = layer;
				comment.ZIndex = -layer;
			}

			layer++;
		}
	}

	protected override void OnPositionChanged()
	{
		base.OnPositionChanged();

		if ( Comment is null ) return;

		UpdateLayer();
		NormalizeLayers();
	}

	//Grows or shrinks the box towards the pointer, on the edges being held.
	internal void Resize( ResizeEdges edges, Vector2 scenePosition )
	{
		var wanted = scenePosition.SnapToGrid( Graph.GridSize );
		var rect = SceneRect;

		if ( edges.HasFlag( ResizeEdges.Left ) ) rect.Left = MathF.Min( wanted.x, rect.Right - MinSize.x );
		else if ( edges.HasFlag( ResizeEdges.Right ) ) rect.Right = MathF.Max( wanted.x, rect.Left + MinSize.x );

		if ( edges.HasFlag( ResizeEdges.Top ) ) rect.Top = MathF.Min( wanted.y, rect.Bottom - MinSize.y );
		else if ( edges.HasFlag( ResizeEdges.Bottom ) ) rect.Bottom = MathF.Max( wanted.y, rect.Top + MinSize.y );

		Position = rect.Position;
		Size = rect.Size.Clamp( MinSize, MaxSize );
	}
}

/// <summary>Which sides of a box are being pulled.</summary>
[Flags]
public enum ResizeEdges
{
	None = 0,
	Top = 1 << 0,
	Bottom = 1 << 1,
	Left = 1 << 2,
	Right = 1 << 3
}
