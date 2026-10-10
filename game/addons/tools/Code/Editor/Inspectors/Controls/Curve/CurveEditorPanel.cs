using UILabel = Rinten.UI.Label;

namespace Editor.Inspectors;

/// <summary>The curve editor: the chart, the four numbers that bound it, the view toggles and the saved curves.</summary>
[StyleSheet( "CurveEditorPanel.cs.scss" )]
public sealed class CurveEditorPanel : Panel
{
	/// <summary>The property being edited, where there is one - only used to say when it holds several values.</summary>
	public SerializedProperty Property { get; set; }

	readonly UILabel multipleLabel;
	readonly Panel chartArea;
	readonly CurveChartPanel chart;

	readonly NumberControl timeMinControl;
	readonly NumberControl timeMaxControl;
	readonly NumberControl valueMinControl;
	readonly NumberControl valueMaxControl;

	readonly ToolbarButton panToggle;
	readonly ToolbarButton retainToggle;

	readonly SerializedObject serialized;

	CurvePresetsPanel presets;

	bool canEditTimeRange = true;
	bool canEditValueRange = true;

	public CurveEditorPanel()
	{

		multipleLabel = AddChild<UILabel>( "multiple" );
		multipleLabel.AddClass( "multiple" );
		multipleLabel.Text = "Multiple Values Selected. Making changes will modify all.";
		multipleLabel.SetClass( "hidden", true );

		chartArea = AddChild<Panel>( "chart-area" );

		chart = chartArea.AddChild<CurveChartPanel>();
		chart.OnEdited = SyncFields;
		chart.OnViewChanged = SyncFields;

		valueMaxControl = AddField( "value-max", "vertical_align_top", null );
		valueMinControl = AddField( "value-min", "vertical_align_bottom", null );
		timeMinControl = AddField( "time-min", "first_page", Theme.Red );
		timeMaxControl = AddField( "time-max", "last_page", Theme.Red );

		var toolbar = chartArea.AddChild<Panel>( "toolbar" );

		panToggle = toolbar.AddChild<ToolbarButton>();
		panToggle.Icon = "pan_tool";
		panToggle.ToolTip = "Change Range when Panning/Zooming";
		panToggle.OnPressed = () =>
		{
			chart.MoveRangeWhenPanning = !chart.MoveRangeWhenPanning;
			panToggle.Active = chart.MoveRangeWhenPanning;
		};

		retainToggle = toolbar.AddChild<ToolbarButton>();
		retainToggle.Icon = "height";
		retainToggle.ToolTip = "Retain Values when changing Min/Max";
		retainToggle.OnPressed = () =>
		{
			chart.MoveRangeWhenSettingMinMax = !chart.MoveRangeWhenSettingMinMax;
			retainToggle.Active = chart.MoveRangeWhenSettingMinMax;
		};

		var fit = toolbar.AddChild<ToolbarButton>();
		fit.Icon = "settings_overscan";
		fit.ToolTip = "Fit to Screen";
		fit.OnPressed = () => chart.FitToScreen();

		serialized = EditorTypeLibrary.GetSerializedObject( this );

		timeMinControl.Property = serialized.GetProperty( nameof( TimeMin ) );
		timeMaxControl.Property = serialized.GetProperty( nameof( TimeMax ) );
		valueMinControl.Property = serialized.GetProperty( nameof( ValueMin ) );
		valueMaxControl.Property = serialized.GetProperty( nameof( ValueMax ) );
	}

	NumberControl AddField( string className, string icon, Color? color )
	{
		var control = chartArea.AddChild<NumberControl>( className );

		control.Icon = icon;
		control.Color = color;
		control.ShowHandle = true;

		return control;
	}

	//The time range of the curves, which is what the min and max across say.
	Vector2 AxisX
	{
		get
		{
			foreach ( var track in chart.Tracks )
			{
				if ( track.TimeRange != Vector2.Zero ) return track.TimeRange;
			}

			return chart.RangeX;
		}
	}

	Vector2 AxisY
	{
		get
		{
			foreach ( var track in chart.Tracks )
			{
				if ( track.ValueRange != Vector2.Zero ) return track.ValueRange;
			}

			return chart.RangeY;
		}
	}

	[Title( "Time Min" )]
	public float TimeMin
	{
		get => AxisX.x;
		set
		{
			var axis = AxisX;

			if ( value >= axis.y ) return;

			var diff = axis.x - value;

			foreach ( var track in chart.Tracks )
			{
				var range = track.TimeRange;
				var viewport = track.ViewportRangeX;

				track.UpdateTimeRange( new Vector2( range.x - diff, range.y ), chart.MoveRangeWhenSettingMinMax );
				track.UpdateZoomRangeX( new Vector2( viewport.x - diff, viewport.y ) );
				track.UpdateHandlePositions();
			}

			chart.MarkRenderDirty();
		}
	}

	[Title( "Time Max" )]
	public float TimeMax
	{
		get => AxisX.y;
		set
		{
			var axis = AxisX;

			if ( value <= axis.x ) return;

			var diff = value - axis.y;

			foreach ( var track in chart.Tracks )
			{
				var range = track.TimeRange;
				var viewport = track.ViewportRangeX;

				track.UpdateTimeRange( new Vector2( range.x, range.y + diff ), chart.MoveRangeWhenSettingMinMax );
				track.UpdateZoomRangeX( new Vector2( viewport.x, viewport.y + diff ) );
				track.UpdateHandlePositions();
			}

			chart.MarkRenderDirty();
		}
	}

	[Title( "Value Min" )]
	public float ValueMin
	{
		get => AxisY.x;
		set
		{
			var axis = AxisY;

			if ( value >= axis.y ) return;

			var diff = axis.x - value;

			foreach ( var track in chart.Tracks )
			{
				var range = track.ValueRange;
				var viewport = track.ViewportRangeY;

				track.UpdateValueRange( new Vector2( range.x - diff, range.y ), chart.MoveRangeWhenSettingMinMax );
				track.UpdateZoomRangeY( new Vector2( viewport.x - diff, viewport.y ) );
				track.UpdateHandlePositions();
			}

			chart.MarkRenderDirty();
		}
	}

	[Title( "Value Max" )]
	public float ValueMax
	{
		get => AxisY.y;
		set
		{
			var axis = AxisY;

			if ( value <= axis.x ) return;

			var diff = value - axis.y;

			foreach ( var track in chart.Tracks )
			{
				var range = track.ValueRange;
				var viewport = track.ViewportRangeY;

				track.UpdateValueRange( new Vector2( range.x, range.y + diff ), chart.MoveRangeWhenSettingMinMax );
				track.UpdateZoomRangeY( new Vector2( viewport.x, viewport.y + diff ) );
				track.UpdateHandlePositions();
			}

			chart.MarkRenderDirty();
		}
	}

	/// <summary>Puts the curve of a property on the chart, and writes back to it as it is edited.</summary>
	public void AddCurve( SerializedProperty property, Action onChanged = null )
	{
		Property ??= property;

		ApplyRangeAttributes( property );

		AddCurve( () => property.GetValue<Curve>(), value =>
		{
			property.SetValue( value );
			onChanged?.Invoke();
		} );
	}

	/// <summary>Puts a curve on the chart, bound to wherever it is read from and written to.</summary>
	public void AddCurve( Func<Curve> read, Action<Curve> write )
	{
		var track = chart.AddCurve( read, write );

		track.CanEditTimeRange = canEditTimeRange;
		track.CanEditValueRange = canEditValueRange;
		track.FitViewportToCurve();
		track.UpdateHandlePositions();

		SyncFields();
	}

	/// <summary>Adds the strip of saved curves, which only makes sense for a single curve.</summary>
	public void AddPresets( SerializedProperty property )
	{
		if ( property.PropertyType != typeof( Curve ) ) return;
		if ( presets is not null ) return;

		presets = AddChild<CurvePresetsPanel>();

		presets.GetCurrentValue = () => property.GetValue<Curve>();
		presets.OnPresetSelected = picked =>
		{
			var existing = property.GetValue<Curve>();

			//A preset is a shape, not a unit - keep ranges the property owns.
			if ( !canEditTimeRange ) picked.UpdateTimeRange( existing.TimeRange, false );
			if ( !canEditValueRange ) picked.UpdateValueRange( existing.ValueRange, false );

			property.SetValue( picked );

			ReloadCurves();
		};
	}

	/// <summary>Makes this a range editor: two curves, and the band between them.</summary>
	public void SetIsRange() => chart.SetIsRange();

	public void SetCanEditTimeRange( bool canEdit )
	{
		canEditTimeRange = canEdit;

		chart.SetCanEditTimeRange( canEdit );

		timeMinControl.ReadOnly = !canEdit;
		timeMaxControl.ReadOnly = !canEdit;
	}

	public void SetCanEditValueRange( bool canEdit )
	{
		canEditValueRange = canEdit;

		chart.SetCanEditValueRange( canEdit );

		valueMinControl.ReadOnly = !canEdit;
		valueMaxControl.ReadOnly = !canEdit;
	}

	/// <summary>Reads every curve back off whatever it is bound to.</summary>
	public void ReloadCurves()
	{
		foreach ( var track in chart.Tracks )
		{
			track.Reload();
		}

		chart.MarkRenderDirty();

		SyncFields();
	}

	void SyncFields()
	{
		timeMinControl?.StateHasChanged();
		timeMaxControl?.StateHasChanged();
		valueMinControl?.StateHasChanged();
		valueMaxControl?.StateHasChanged();
	}

	public override void Tick()
	{
		base.Tick();

		multipleLabel.SetClass( "hidden", !(Property?.IsMultipleDifferentValues ?? false) );
	}

	//Applies the ranges the property asked for, and whether the user may change them.
	void ApplyRangeAttributes( SerializedProperty property )
	{
		var curve = property.GetValue<Curve>();
		var untouched = curve.Equals( default( Curve ) ) || curve.Equals( new Curve() );

		if ( property.TryGetAttribute<TimeRangeAttribute>( out var timeRange ) )
		{
			SetCanEditTimeRange( timeRange.CanModify );

			if ( untouched )
			{
				curve.UpdateTimeRange( new Vector2( timeRange.Min, timeRange.Max ), false );
			}
		}

		if ( property.TryGetAttribute<ValueRangeAttribute>( out var valueRange ) )
		{
			SetCanEditValueRange( valueRange.CanModify );

			if ( untouched )
			{
				curve.UpdateValueRange( new Vector2( valueRange.Min, valueRange.Max ), false );
			}
		}

		property.SetValue( curve );
	}
}
