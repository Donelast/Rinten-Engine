using Rinten.UI;

namespace Editor.NodeEditor;

public enum HandleShape
{
	Square,
	Circle,
	Arrow,
}

public record struct HandleConfig( string Name, Color Color, HandleShape Shape = HandleShape.Square );

/// <summary>One socket on a node; laid out by flexbox in the node, and wires read where it ended up</summary>
public abstract class Plug : Panel
{
	public NodeUI Node { get; }

	public IPlug Inner { get; }

	public GraphView Graph => Node.Graph;

	public HandleConfig HandleConfig => Graph.GetHandleConfig( PropertyType );

	protected Plug DropTarget => Graph.DropTarget;

	public string Title => Inner.DisplayInfo.Name;

	private Type _overridePropertyType;

	public Type PropertyType
	{
		get => _overridePropertyType ?? Inner.Type;
		set
		{
			_overridePropertyType = value;
			Update();
		}
	}

	/// <summary>Whether the handle is drawn at all - a reroute hides its own.</summary>
	public bool Visible
	{
		get;
		set
		{
			field = value;
			_handle.SetClass( "hidden", !value );
		}
	} = true;

	public abstract Vector2 ConnectionPosition { get; }

	public abstract bool IsConnected { get; }

	/// <summary>Whatever edits the value while nothing is plugged in here.</summary>
	public ValueEditor Editor { get; }

	/// <summary>Where this plug sits in the graph, which is wherever the node put it.</summary>
	public Rect SceneRect => Graph.SceneRectOf( this );

	//Where the handle sits in the graph.
	protected Rect HandleSceneRect => Graph.SceneRectOf( _handle );

	private readonly PlugHandle _handle;
	private readonly Panel _labelRow;
	private readonly Rinten.UI.Label _title;
	private readonly Rinten.UI.Label _group;

	protected Plug( NodeUI node, IPlug plug )
	{
		Node = node;
		Inner = plug;

		AddClass( "plug" );

		_handle = new PlugHandle();
		_labelRow = new Panel();
		_labelRow.AddClass( "label" );

		_group = _labelRow.AddChild<Rinten.UI.Label>( "group" );
		_group.Tokenize = false;

		_title = _labelRow.AddChild<Rinten.UI.Label>( "title" );
		_title.Tokenize = false;

		Build( _handle, _labelRow );

		Editor = plug.CreateEditor( node, this );

		if ( Editor.IsValid() ) AddChild( Editor );
	}

	//Puts the handle and the label in the order this end of a node wants them.
	protected abstract void Build( PlugHandle handle, Panel label );

	/// <summary>Left as it is by default; a node with rows of its own overrides it.</summary>
	public virtual void Layout()
	{
	}

	//Whether what the plug shows is out of date with what it says.
	private bool _dirty = true;

	//Deferred for the same reason a node's is - and because a wire dragged across a graph tells every plug it passes, several times a frame.
	/// <summary>Says the plug needs re-reading; the reading happens on the next tick.</summary>
	public void Update() => _dirty = true;

	public override void Tick()
	{
		base.Tick();

		if ( !_dirty ) return;

		_dirty = false;

		OnUpdate();
	}

	//Re-reads everything this shows.
	protected virtual void OnUpdate()
	{
		var config = HandleConfig;

		var isTarget = ReferenceEquals( DropTarget, this ) || ReferenceEquals( Graph.AttachSource, this );
		var highlight = HasHovered && !DropTarget.IsValid() || isTarget;
		var unreachable = !highlight && !Node.Selected && !Inner.IsReachable;

		var colour = OnGetHandleColor( ref config, isTarget );

		if ( !highlight ) colour = colour.Desaturate( 0.2f ).Darken( 0.3f );
		if ( unreachable ) colour = colour.Desaturate( 0.5f ).Darken( 0.25f );

		if ( !string.IsNullOrEmpty( Inner.ErrorMessage ) ) colour = Theme.Red;

		_handle.Apply( config.Shape, colour );

		SetClass( "highlight", highlight );
		SetClass( "unreachable", unreachable );
		SetClass( "connected", IsConnected );
		SetClass( "error", !string.IsNullOrEmpty( Inner.ErrorMessage ) );

		UpdateLabel( unreachable );

		Tooltip = NodeUI.FormatToolTip( Inner.DisplayInfo.Name, Inner.DisplayInfo.Description, PropertyType, null );
	}

	//What colour the handle is, which either end of a wire may have a say in.
	protected virtual Color OnGetHandleColor( ref HandleConfig config, bool isTarget ) => config.Color;

	protected override void OnMouseDown( MousePanelEvent e )
	{
		if ( e.MouseButton != MouseButtons.Left ) return;

		e.StopPropagation();

		Graph.Begin( new WireInteraction( Graph, this, Graph.CurrentPointer ), e );
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		if ( Inner.CreateContextMenu( Node, this ) is not { } menu ) return;

		e.StopPropagation();

		menu.OpenAt( Application.CursorPosition );
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		Inner.OnDoubleClick( Node, this );

		e.StopPropagation();
	}

	protected override void OnMouseOver( MousePanelEvent e ) => Update();

	protected override void OnMouseOut( MousePanelEvent e ) => Update();

	private void UpdateLabel( bool unreachable )
	{
		var show = Inner.ShowLabel && !Inner.InTitleBar && Editor is not { Enabled: true, HideLabel: true };

		_labelRow.SetClass( "hidden", !show );

		if ( !show ) return;

		_title.Text = Title;

		var group = Inner.DisplayInfo.Group;

		_group.SetClass( "hidden", group is null );
		_group.Text = group is null ? string.Empty : $"{group} > ";
	}
}
