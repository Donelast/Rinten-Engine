using Editor.Inspectors;
using Rinten.MovieMaker;
using Rinten.MovieMaker.Properties;
using Rinten.UI;
using System.Linq;
using System.Reflection;

namespace Editor.MovieMaker;

#nullable enable

//A root row is placed at the height the timeline draws that track at; its children follow it in flow, indented, which comes out at the same heights the track list worked out for them.
/// <summary>One track in the list down the left: its name, and the buttons that configure it.</summary>
public sealed partial class TrackRowPanel : Panel
{
	public TrackListPanel TrackList { get; }
	public TrackRowPanel? ParentRow { get; }
	public TrackView View { get; }

	public Session Session { get; }

	public IEnumerable<TrackRowPanel> ChildRows => _children;

	public bool IsSelected => View.IsSelected;

	private readonly Panel _header;
	private readonly Panel _childHost;
	private readonly CollapseButtonPanel _collapse;
	private readonly LockButtonPanel _lock;
	private readonly IconPanel _autoCreated;
	private readonly Panel _flash;
	private readonly Rinten.UI.Label? _label;
	private readonly BaseControl? _control;

	private readonly SynchronizedSet<TrackView, TrackRowPanel> _children;

	private RealTimeSince _timeSinceInteraction = 1000f;

	public TrackRowPanel( TrackListPanel trackList, TrackRowPanel? parent, TrackView view )
	{
		TrackList = trackList;
		ParentRow = parent;
		Session = trackList.Session;
		View = view;

		AddClass( "track-row" );

		Tooltip = View.Description;

		_children = new SynchronizedSet<TrackView, TrackRowPanel>(
			AddChildTrack, RemoveChildTrack, UpdateChildTrack );

		_header = AddChild<Panel>( "header" );
		_childHost = AddChild<Panel>( "children" );

		_collapse = _header.AddChild( new CollapseButtonPanel( this ) );

		_control = AddReferenceControl();

		if ( _control is null )
		{
			_label = _header.AddChild<Rinten.UI.Label>( "name" );
			_label.Text = view.Target.Name;
		}

		_autoCreated = _header.AddChild<IconPanel>( "auto-created" );
		_autoCreated.Text = "person_add";

		_lock = _header.AddChild( new LockButtonPanel( this ) );

		_flash = AddChild<Panel>( "flash" );

		View.Changed += View_Changed;
		View.ValueChanged += View_ValueChanged;

		View_Changed( view );
	}

	public override void OnDeleted()
	{
		View.Changed -= View_Changed;
		View.ValueChanged -= View_ValueChanged;

		base.OnDeleted();
	}

	/// <summary>Puts a root row at the height the timeline draws its track at.</summary>
	public void PlaceAt( float top )
	{
		AddClass( "placed" );
		Style.Top = Length.Pixels( top );
	}

	private TrackRowPanel AddChildTrack( TrackView source ) => new( TrackList, this, source ) { Parent = _childHost };

	private void RemoveChildTrack( TrackRowPanel item ) => item.Delete();

	private bool UpdateChildTrack( TrackView source, TrackRowPanel item ) => item.UpdateLayout();

	public bool UpdateLayout()
	{
		_children.Update( View.IsExpanded ? View.Children : [] );

		for ( var i = 0; i < _children.Count; i++ )
		{
			_childHost.SetChildIndex( _children[i], i );
		}

		return true;
	}

	//The picker for retargeting a scene reference, where the track is one.
	private BaseControl? AddReferenceControl()
	{
		if ( View.Target is not ITrackReference reference ) return null;
		if ( View.IsBoneObject ) return null;

		SerializedProperty? property = null;

		if ( View.Track is ProjectSequenceTrack )
		{
			return null;
		}

		if ( reference is ITrackReference<GameObject> goReference )
		{
			property = EditorTypeLibrary.CreateProperty( reference.Name,
				() => goReference.Value, value =>
				{
					goReference.Bind( value );
					View.TrackList.Session.Player.UpdateTargets();
				} );
		}
		else
		{
			var helperType = typeof( ReferenceHelper<> ).MakeGenericType( reference.TargetType );
			var createMethod = helperType.GetMethod( nameof( ReferenceHelper<IValid>.CreateProperty ),
				BindingFlags.Static | BindingFlags.Public )!;

			property = (SerializedProperty?)createMethod.Invoke( null, [View.Track, reference] );
		}

		if ( property is null ) return null;

		var control = PanelControlFactory.CreateFor( property );

		if ( control is null ) return null;

		control.AddClass( "reference" );
		control.Parent = _header;

		return control;
	}

	private void View_Changed( TrackView view )
	{
		_collapse.SetClass( "hidden", view.Children.Count == 0 );

		_collapse.Sync();
		_lock.Sync();

		SetClass( "locked", View.IsLocked );
		SetClass( "selected", IsSelected );

		_autoCreated.SetClass( "hidden", View.Target is not ITrackReference { IsAutoCreatedTarget: true } );

		if ( _label is { } label )
		{
			label.Style.FontColor = !View.IsLocked
				? IsSelected ? Color.White : Color.White.Darken( 0.4f )
				: Color.White.Darken( 0.6f );
		}

		if ( _control is { } control )
		{
			control.SetClass( "readonly", View.IsLocked );
		}

		UpdateBackground();
	}

	private void View_ValueChanged( TrackView view )
	{
		_timeSinceInteraction = 0f;
	}

	private Color? _appliedBackground;

	private void UpdateBackground()
	{
		var color = View.BackgroundColor;

		if ( _appliedBackground == color ) return;

		_appliedBackground = color;

		_header.Style.BackgroundColor = color;
	}

	public override void Tick()
	{
		base.Tick();

		UpdateBackground();

		//A track that just took a value flashes, and then stops costing anything.
		var flash = _timeSinceInteraction < 2f ? _timeSinceInteraction.Relative.Remap( 2f, 0f, 0f, 1f ) : 0f;

		_flash.Style.Opacity = flash;
		_flash.SetClass( "hidden", flash <= 0f );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		var modifiers = Application.KeyboardModifiers;

		if ( (modifiers & KeyboardModifiers.Ctrl) != 0 )
		{
			View.ToggleSelect();
		}
		else if ( (modifiers & KeyboardModifiers.Shift) != 0 )
		{
			View.RangeSelect();
		}
		else
		{
			View.Select();
		}

		e.StopPropagation();
	}

	protected override void OnDoubleClick( MousePanelEvent e )
	{
		base.OnDoubleClick( e );

		View.InspectProperty();

		e.StopPropagation();
	}

	protected override void OnMouseOver( MousePanelEvent e )
	{
		base.OnMouseOver( e );

		View.IsHovered = true;
	}

	protected override void OnMouseOut( MousePanelEvent e )
	{
		base.OnMouseOut( e );

		View.IsHovered = false;
	}

	protected override void OnRightClick( MousePanelEvent e )
	{
		base.OnRightClick( e );

		if ( !View.IsSelected )
		{
			var modifiers = Application.KeyboardModifiers;

			if ( (modifiers & KeyboardModifiers.Ctrl) != 0 )
			{
				View.ToggleSelect();
			}
			else if ( (modifiers & KeyboardModifiers.Shift) != 0 )
			{
				View.RangeSelect();
			}
			else
			{
				View.Select();
			}
		}

		ShowContextMenu();

		e.StopPropagation();
	}

	//Reflection helper, because the reference type is only known at runtime.
	private sealed class ReferenceHelper<T>
		where T : class, IValid
	{
		public static SerializedProperty CreateProperty( IProjectReferenceTrack track, ITrackReference<T> target )
		{
			return EditorTypeLibrary.CreateProperty( target.Name,
				() => target.Value, value =>
				{
					var metadata = track.Metadata ?? new TrackMetadata();

					track.Metadata = metadata with
					{
						ReferenceId = value switch
						{
							Component cmp => cmp.Id,
							GameObject go => go.Id,
							_ => null
						}
					};

					target.Bind( value );
				} );
		}
	}
}

/// <summary>The button that opens and closes a track's sub-tracks.</summary>
public sealed class CollapseButtonPanel : Panel
{
	private readonly TrackRowPanel _row;
	private readonly IconPanel _icon;

	public CollapseButtonPanel( TrackRowPanel row )
	{
		_row = row;

		AddClass( "track-button" );

		Tooltip = "Toggle expanded";

		_icon = AddChild<IconPanel>();

		Sync();
	}

	public void Sync() => _icon.Text = _row.View.IsExpanded ? "remove" : "add";

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		_row.View.IsExpanded = !_row.View.IsExpanded;
		_row.View.TrackList.Update();

		Sync();

		e.StopPropagation();
	}
}

/// <summary>The button that stops a track being edited.</summary>
public sealed class LockButtonPanel : Panel
{
	private readonly TrackRowPanel _row;
	private readonly IconPanel _icon;

	public LockButtonPanel( TrackRowPanel row )
	{
		_row = row;

		AddClass( "track-button" );

		Tooltip = "Toggle lock";

		_icon = AddChild<IconPanel>();

		Sync();
	}

	public void Sync()
	{
		_icon.Text = _row.View.IsLockedSelf ? "lock" : "lock_open";

		SetClass( "active", _row.View.IsLockedSelf );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Left ) return;

		using ( _row.Session.History.Push( $"{(_row.View.IsLockedSelf ? "Unlocked" : "Locked")} Track" ) )
		{
			_row.View.IsLockedSelf = !_row.View.IsLockedSelf;
		}

		Sync();

		e.StopPropagation();
	}
}
