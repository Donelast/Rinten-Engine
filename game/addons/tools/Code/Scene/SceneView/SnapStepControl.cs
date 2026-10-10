using Rinten.UI;

namespace Editor;

/// <summary>Compact snap step field for the viewport toolbar.</summary>
[StyleSheet( "SnapStepControl.cs.scss" )]
public sealed class SnapStepControl : Panel
{
	public SnapStepKind Kind { get; set; }

	public string ToolTip { get; set; }

	static readonly float[] AngleValues =
	{
		0.25f,
		0.5f,
		1f,
		5f,
		15f,
		30f,
		45f,
		90f,
		180f
	};

	TextEntry entry;
	SerializedProperty property;
	Gizmo.SceneSettings propertyOf;
	float displayScale = 1f;
	bool built;

	public SnapStepControl()
	{
		AddClass( "snap-step" );
	}

	protected override void OnParametersSet()
	{
		base.OnParametersSet();
		EnsureBuilt();
	}

	void EnsureBuilt()
	{
		if ( built || entry is { IsValid: true } ) return;

		if ( Property is null ) return;

		built = true;
		displayScale = Kind switch
		{
			SnapStepKind.Angle => 1f,
			SnapStepKind.Grid => 100f,
			_ => 1f
		};

		var suffix = Kind switch
		{
			SnapStepKind.Angle => "º",
			SnapStepKind.Grid => "cm",
			_ => null
		};

		if ( !string.IsNullOrEmpty( ToolTip ) )
			Tooltip = ToolTip;

		var field = AddChild<Panel>( "field" );
		entry = field.AddChild<TextEntry>( "textentry control no-background" );
		entry.Numeric = true;
		entry.OnTextEdited = OnEdited;

		if ( !string.IsNullOrEmpty( suffix ) )
			AddChild<Rinten.UI.Label>( "snap-suffix" ).Text = suffix;

		var buttons = AddChild<Panel>( "step-buttons" );
		AddStepButton( buttons, "keyboard_arrow_up", Increase );
		AddStepButton( buttons, "keyboard_arrow_down", Decrease );

		SyncEntry();
	}

	//What the box was built for, the number behind it, and whether it has focus - it is left alone
	//while somebody is typing. Asked every frame; the box is written only when one of them moves.
	protected override int BuildHash() => built
		? HashCode.Combine( Kind, ToolTip, Current, entry?.HasFocus ?? false )
		: HashCode.Combine( Kind, ToolTip );

	protected override void OnBuildHashChanged() => SyncEntry();

	//The build waits on the setting it edits being there to find. It is tried when
	//the parameters are set and on the turns it asks for until it has been built.
	protected override bool TickWhenAsked => true;

	public override void Tick()
	{
		base.Tick();

		if ( built ) return;

		EnsureBuilt();

		if ( built ) MarkBuildHashDrawn();
		else RequestTick();
	}

	//The setting as a serialized property; re-found when the editor swaps its settings object
	SerializedProperty Property
	{
		get
		{
			var settings = EditorScene.GizmoSettings;
			if ( property is not null && ReferenceEquals( settings, propertyOf ) ) return property;

			propertyOf = settings;

			var so = settings.GetSerialized();
			property = Kind switch
			{
				SnapStepKind.Angle => so.GetProperty( nameof( Gizmo.SceneSettings.AngleSpacing ) ),
				SnapStepKind.Grid => so.GetProperty( nameof( Gizmo.SceneSettings.GridSpacing ) ),
				_ => null
			};

			return property;
		}
	}

	//The number behind the box, read off the settings as the float it is. Not through the serialized property, which
	//reads by reflection and boxes the float - and the hash asks for it every frame, for each of these in the toolbar.
	float Current => Kind switch
	{
		SnapStepKind.Angle => EditorScene.GizmoSettings.AngleSpacing,
		SnapStepKind.Grid => EditorScene.GizmoSettings.GridSpacing,
		_ => 0f
	};

	void OnEdited( string text )
	{
		if ( Property is not { } setting ) return;

		if ( float.TryParse( text, out var shown ) )
			setting.SetValue( shown / displayScale );
		else
			setting.SetValue<object>( text );
	}

	//The number last put in the box, so the same one is not formatted and written again.
	float? synced;

	void SyncEntry()
	{
		if ( entry is null || !entry.IsValid() ) return;
		if ( entry.HasFocus ) return;

		//Compare the number before formatting, so the text entry isn't rewritten every frame
		var value = Current;

		if ( synced == value ) return;

		synced = value;

		entry.Value = Shown( value );
	}

	void Increase() => Step( 1 );

	void Decrease() => Step( -1 );

	void Step( int direction )
	{
		if ( Property is not { } setting ) return;

		entry?.Blur();

		var value = Current;

		setting.SetValue( Kind switch
		{
			SnapStepKind.Angle => StepAngle( value, direction ),
			SnapStepKind.Grid => GridSteps.Step( value, direction ),
			_ => value
		} );
	}

	static float StepAngle( float value, int direction )
	{
		var index = Array.IndexOf( AngleValues, AngleValues.OrderBy( a => MathF.Abs( value - a ) ).First() );
		index = Math.Clamp( index + direction, 0, AngleValues.Length - 1 );
		return AngleValues[index];
	}

	string Shown( float value ) => (value * displayScale).ToString( "0.####" );


	static void AddStepButton( Panel parent, string icon, Action action )
	{
		var button = parent.AddChild<Panel>();
		button.AddClass( "toolbarbutton" );
		button.AddChild<IconPanel>().Text = icon;
		button.AddEventListener( "onmousedown", _ => action() );
	}
}

public enum SnapStepKind
{
	Angle,
	Grid
}

//How the grid step moves: halved, or doubled.
internal static class GridSteps
{
	public const float Finest = 0.0025f;
	public const float Coarsest = 40.96f;

	public static float Step( float metres, int direction )
	{
		if ( metres <= 0.0f ) return Finest;

		return Math.Clamp( direction < 0 ? metres * 0.5f : metres * 2.0f, Finest, Coarsest );
	}
}
