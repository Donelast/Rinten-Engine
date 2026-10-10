using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Move / rotate / scale switch in the corner of the model viewport. It lives on the overlay layer over the
//viewport, so where it sits is the one thing it is told - how far up from the bottom edge, clear of the transport.
[StyleSheet( "PlacementToolBar.cs.scss" )]
internal sealed class PlacementToolBar : Panel
{
	private readonly Preview _preview;

	public PlacementToolBar( Preview preview )
	{
		_preview = preview;

		AddClass( "placement-tools" );

		preview.PlacementToolChanged = Read;

		AddTool( PlacementTool.Move, "control_camera", "Move  ·  W" );
		AddTool( PlacementTool.Rotate, "3d_rotation", "Rotate  ·  E" );
		AddTool( PlacementTool.Scale, "straighten", "Scale  ·  R" );

		//The three the scene viewport keeps beside its own transform tools - which space the handles work in, and whether a drag lands on the grid or on the
		//angle step. The sizes those two use are in the viewport's grid menu; these are only the switches, because they are what gets turned on and off mid-drag.
		AddChild( new OptionButton( this,
			preview => preview.PlacementGlobalSpace,
			( preview, on ) => preview.SetPlacementGlobalSpace( on ),
			on => on ? "public" : "transform",
			on => on ? "World space  ·  X" : "Local space  ·  X" ) );

		AddChild( new OptionButton( this,
			_ => PreviewGizmoSettings.Current.SnapToGrid,
			( preview, on ) => preview.SetGridSnap( on ),
			_ => "grid_on",
			on => on ? "Grid snap on  ·  G" : "Grid snap off  ·  G" ) );

		AddChild( new OptionButton( this,
			_ => PreviewGizmoSettings.Current.SnapToAngles,
			( preview, on ) => preview.SetAngleSnap( on ),
			_ => "rotate_90_degrees_cw",
			on => on ? "Angle snap on" : "Angle snap off" ) );
	}

	private void AddTool( PlacementTool tool, string icon, string tip )
	{
		var button = AddChild( new ToolButton( this, tool, icon ) );
		button.Tooltip = tip;
	}

	//How far off the bottom of the viewport it sits, and whether it is up at all.
	internal void PlaceIn()
	{
		var wanted = _preview.IsValid() && _preview.Placement is not null;

		SetClass( "hidden", !wanted );
		if ( !wanted ) return;

		//Clear of whatever the viewport already has along its bottom - the transport and its clip
		//picker - rather than twelve pixels off the frame, which put these straight on top of them.
		Style.Bottom = 12.0f + _preview.ViewportBottomInset;
	}

	//Says on every button whether it is the one being used.
	private void Read()
	{
		foreach ( var child in Children )
		{
			if ( child is Button button ) button.Read();
		}
	}

	/// <summary>One square of the bar. What it means is its subclass's business.</summary>
	private abstract class Button : Panel
	{
		protected readonly PlacementToolBar Bar;
		protected readonly UILabel Icon;

		protected Button( PlacementToolBar bar )
		{
			Bar = bar;

			AddClass( "tool material-icon" );

			Icon = AddChild<UILabel>();

			AddEventListener( "onclick", () =>
			{
				Press();
				Bar.Read();
			} );
		}

		protected Preview Preview => Bar._preview.IsValid() ? Bar._preview : null;

		//What clicking it does.
		protected abstract void Press();

		//Reads the preview and says what it now shows.
		internal abstract void Read();
	}

	/// <summary>A button that is on while its tool is the one being used.</summary>
	private sealed class ToolButton : Button
	{
		private readonly PlacementTool _tool;

		public ToolButton( PlacementToolBar bar, PlacementTool tool, string icon ) : base( bar )
		{
			_tool = tool;
			Icon.Text = icon;

			Read();
		}

		private bool IsCurrent => Preview?.PlacementTool == _tool;

		protected override void Press()
			=> Preview?.SetPlacementTool( IsCurrent ? PlacementTool.None : _tool );

		internal override void Read() => SetClass( "on", IsCurrent );
	}

	/// <summary>A switch in the bar, lit while it is on. The same square as the tool buttons above, because it sits in the same row - what it stands for is the four closures it is made with.</summary>
	private sealed class OptionButton : Button
	{
		private readonly Func<Preview, bool> _isOn;
		private readonly Action<Preview, bool> _set;
		private readonly Func<bool, string> _icon;
		private readonly Func<bool, string> _tip;

		public OptionButton( PlacementToolBar bar, Func<Preview, bool> isOn, Action<Preview, bool> set,
			Func<bool, string> icon, Func<bool, string> tip ) : base( bar )
		{
			_isOn = isOn;
			_set = set;
			_icon = icon;
			_tip = tip;

			Read();
		}

		private bool IsOn => Preview is { } preview && _isOn( preview );

		protected override void Press()
		{
			if ( Preview is { } preview ) _set( preview, !IsOn );
		}

		internal override void Read()
		{
			var on = IsOn;

			SetClass( "on", on );

			Icon.Text = _icon( on );
			Tooltip = _tip( on );
		}
	}
}
