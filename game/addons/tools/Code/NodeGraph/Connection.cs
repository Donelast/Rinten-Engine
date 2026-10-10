namespace Editor.NodeEditor;

//Not a panel. A panel is a box, and a wire is a curve across a box that is mostly empty air - so wires are drawn together on one layer beneath the nodes, and each one keeps the line it follows and answers whether the pointer is on it.
/// <summary>One wire, from an output to an input.</summary>
public class Connection : IGraphSelectable
{
	public static Color SelectedColor { get; } = Color.Parse( "#ff99c8" )!.Value;

	/// <summary>How near the wire counts as being on it, in graph units.</summary>
	public const float HitWidth = 4.0f;

	public GraphView Graph { get; }

	public PlugOut Output { get; protected set; }
	public PlugIn Input { get; protected set; }

	public float WidthScale { get; set; } = 1f;
	public Color ColorTint { get; set; }

	internal bool IsInvalidPreview { get; set; }

	private bool _destroyed;
	private bool _selected;
	private bool _hovered;

	public bool IsValid => !_destroyed;

	public HandleConfig Config => Output.IsValid() ? Output.HandleConfig : Input.HandleConfig;

	public ConnectionStyle ConnectionStyle => Graph?.ConnectionStyle ?? ConnectionStyle.Default;

	/// <summary>The line this wire follows, in graph units.</summary>
	public GraphPath Path { get; } = new();

	public Vector2 OutputPosition { get; private set; }
	public Vector2 InputPosition { get; private set; }

	/// <summary>Everything the wire covers, with room for how thick it is drawn.</summary>
	public Rect SceneBounds { get; private set; }

	public Rect SelectionRect => SceneBounds;

	public bool Selectable => true;

	/// <summary>Per-<see cref="NodeEditor.ConnectionStyle"/> data.</summary>
	public object StyleData { get; set; }

	private readonly Dictionary<string, ConnectionHandleConfig> _handleConfigs = new();
	private readonly Dictionary<string, ConnectionHandle> _handles = new();

	public bool Selected
	{
		get => _selected;
		set
		{
			if ( _selected == value ) return;

			_selected = value;

			SetHandlesVisible( _selected || _hovered );
			Graph?.RedrawConnections();
		}
	}

	public bool Hovered
	{
		get => _hovered;
		internal set
		{
			if ( _hovered == value ) return;

			_hovered = value;

			SetHandlesVisible( _selected || _hovered );
			Graph?.RedrawConnections();
		}
	}

	public Connection( GraphView graph, PlugOut output, PlugIn input )
	{
		Graph = graph;
		Output = output;
		Input = input;

		input.SetConnectionInternal( this );
		output.AddConnectionInternal( this );
	}

	/// <summary>A wire with one end in the air, drawn while a plug is being dragged.</summary>
	public Connection( GraphView graph, Plug source )
	{
		Graph = graph;
		Input = source as PlugIn;
		Output = source as PlugOut;
	}

	public void Clear() => Path.Clear();
	public void MoveTo( Vector2 point ) => Path.MoveTo( point );
	public void LineTo( Vector2 point ) => Path.LineTo( point );
	public void CubicLineTo( Vector2 c1, Vector2 c2, Vector2 point ) => Path.CubicLineTo( c1, c2, point );

	internal void UpdateSceneBounds( Rect sceneRect ) => SceneBounds = sceneRect.Grow( 8.0f );

	/// <summary>Whether the pointer is on the wire itself.</summary>
	public bool IsNear( Vector2 scenePosition, float reach ) => Path.IsNear( scenePosition, reach );

	/// <summary>Says the wire looks different now, without moving it.</summary>
	public void Update() => Graph?.RedrawConnections();

	public void Layout()
	{
		if ( !Output.IsValid() || !Input.IsValid() ) return;

		OutputPosition = Output.ConnectionPosition;
		InputPosition = Input.ConnectionPosition;

		ConnectionStyle.Layout( this, OutputPosition, InputPosition );

		Graph?.RedrawConnections();
	}

	internal void LayoutForPreview( Plug plug, Vector2 scenePosition, Plug dropTarget )
	{
		Output = plug as PlugOut ?? dropTarget as PlugOut;
		Input = plug as PlugIn ?? dropTarget as PlugIn;

		if ( Output.IsValid() && Input.IsValid() && Output.Node != Input.Node )
		{
			Layout();
			return;
		}

		OutputPosition = Output?.ConnectionPosition ?? scenePosition;
		InputPosition = Input?.ConnectionPosition ?? scenePosition;

		ConnectionStyle.Layout( this, OutputPosition, InputPosition );

		Graph?.RedrawConnections();
	}

	internal bool IsAttachedTo( NodeUI node )
	{
		if ( node == Output?.Node ) return true;
		if ( node == Input?.Node ) return true;

		return false;
	}

	/// <summary>What the wire is drawn in, given who is looking at it.</summary>
	public Color DrawColor
	{
		get
		{
			if ( IsInvalidPreview ) return Theme.Red;
			if ( _hovered ) return SelectedColor;
			if ( _selected ) return SelectedColor.Darken( 0.2f );

			var color = Color.Lerp( Config.Color, ColorTint.WithAlpha( 1f ), ColorTint.a );

			if ( !Input.IsValid() || !Output.IsValid() ) return color.WithAlpha( 0.4f );

			if ( Output.Node?.Node.IsReachable is false || Input.Node?.Node.IsReachable is false )
			{
				return color.Desaturate( 0.5f ).Darken( 0.25f );
			}

			return color;
		}
	}

	/// <summary>How thick, in graph units.</summary>
	public float DrawWidth => (_hovered ? 6.0f : 4.0f) * WidthScale;

	/// <summary>What the wire says about itself when the pointer is on it.</summary>
	public string ToolTip
	{
		get
		{
			if ( !Input.IsValid() || !Output.IsValid() ) return null;

			return $"<span style=\"white-space: nowrap;\">{Output.Inner.Type.ToRichText()}<br/>" +
				$"<b>From</b>: {Output.Node.Node.DisplayInfo.Name} → {Output.Inner.DisplayInfo.Name.WithColor( "#9CDCFE" )}<br/>" +
				$"<b>To</b>: {Input.Node.Node.DisplayInfo.Name} → {Input.Inner.DisplayInfo.Name.WithColor( "#9CDCFE" )}</span>";
		}
	}

	public void Disconnect()
	{
		if ( Input.IsValid() ) Input.SetConnectionInternal( null );
		if ( Output.IsValid() ) Output.RemoveConnectionInternal( this );

		Graph?.RemoveConnection( this );
	}

	public void Destroy()
	{
		if ( _destroyed ) return;

		_destroyed = true;

		SetHandlesVisible( false );

		Graph?.ForgetConnection( this );
		Graph?.RedrawConnections();
	}

	internal void SetHandles( IReadOnlyList<ConnectionHandleConfig> configs )
	{
		_handleConfigs.Clear();

		foreach ( var config in configs )
		{
			_handleConfigs[config.Name] = config;
		}

		foreach ( var (name, handle) in _handles.ToArray() )
		{
			if ( _handleConfigs.TryGetValue( name, out var config ) )
			{
				handle.Config = config;
				continue;
			}

			_handles.Remove( name );
			handle.Destroy();
		}
	}

	//The grips a grid-drawn wire is bent by, which are only there while it is the wire being looked at.
	private void SetHandlesVisible( bool visible )
	{
		if ( visible && Input.IsValid() && Output.IsValid() )
		{
			foreach ( var config in _handleConfigs.Values )
			{
				if ( !_handles.TryGetValue( config.Name, out var handle ) )
				{
					_handles[config.Name] = handle = Graph.AddHandle( this );
				}

				handle.Config = config;
			}

			return;
		}

		foreach ( var handle in _handles.Values )
		{
			handle.Destroy();
		}

		_handles.Clear();
	}
}

public enum DragDirection
{
	Horizontal,
	Vertical
}

public enum ConnectionPlug
{
	Output,
	Input
}

public record struct ConnectionHandleConfig(
	string Name,
	DragDirection Direction,
	ConnectionPlug RelativePlug,
	Vector2 SceneOrigin,
	float Default,
	float? Min = null,
	float? Max = null )
{
	public Vector2 GetScenePosition( Connection connection )
	{
		var value = GetValue( connection );
		var axis = Direction == DragDirection.Horizontal ? new Vector2( 1f, 0f ) : new Vector2( 0f, 1f );

		return SceneOrigin + axis * value;
	}

	public float GetValue( Connection connection )
	{
		var value = connection.Input?.Inner.GetHandleOffset( Name ) ?? Default;

		return Math.Clamp( value, Min ?? float.NegativeInfinity, Max ?? float.PositiveInfinity );
	}
}
