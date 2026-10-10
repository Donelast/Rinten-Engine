namespace Editor.Inspectors;
using UILabel = Rinten.UI.Label;
[StyleSheet( "GradientEditorPanel.cs.scss" )]
public sealed class GradientEditorPanel : Panel
{
	public Action<Gradient> ValueChanged;
	public SerializedProperty Property { get; set; }
	GradientStopBarPanel.Stop _selectedStop;
	bool _isColorSelection;
	bool _skipUpdatePoints;
	GradientStopBarPanel _alphaBar;
	GradientPreviewPanel _preview;
	GradientStopBarPanel _colorBar;
	Panel _gradientStack;
	GradientPresetsPanel _presets;
	UILabel _multipleLabel;
	Panel _controls;
	ToolbarButton _deleteButton;
	ToolbarButton _prevButton;
	ToolbarButton _nextButton;
	NumberControl _timeControl;
	ColorControl _colorControl;
	ColorTrack _alphaControl;
	SerializedObject _controlSo;
	Gradient _value = new( new Gradient.ColorFrame( 0, Color.White ), new Gradient.ColorFrame( 1, Color.Black ) );
	public Gradient Value
	{
		get => _value;
		set
		{
			_value = value;
			UpdatePoints();
			_preview?.SetValue( _value );
			ValueChanged?.Invoke( _value );
			StateHasChanged();
		}
	}
	public GradientEditorPanel()
	{
		_multipleLabel = AddChild<UILabel>( "multiple" );
		_multipleLabel.AddClass( "multiple" );
		_multipleLabel.Text = "Multiple Values Selected. Making changes will modify all.";
		_multipleLabel.SetClass( "hidden", true );
		_gradientStack = AddChild<Panel>( "gradient-stack" );
		_alphaBar = _gradientStack.AddChild( new GradientStopBarPanel { IsColorBar = false } );
		_alphaBar.OnAddPoint = time => AddStop( _alphaBar, time, false );
		_alphaBar.OnStopSelected = stop => UpdateSelection( stop, false );
		_alphaBar.OnPointsChanged = () => UpdateFromPoints( notifyExternal: true );
		_alphaBar.OnPointsLiveChanged = () => UpdateFromPoints( notifyExternal: false );
		_preview = _gradientStack.AddChild<GradientPreviewPanel>();
		_preview.OnPickColor = color => ColorValue = color;
		_colorBar = _gradientStack.AddChild<GradientStopBarPanel>();
		_colorBar.OnAddPoint = time => AddStop( _colorBar, time, true );
		_colorBar.OnStopSelected = stop => UpdateSelection( stop, true );
		_colorBar.OnPointsChanged = () => UpdateFromPoints( notifyExternal: true );
		_colorBar.OnPointsLiveChanged = () => UpdateFromPoints( notifyExternal: false );
		BuildControls();
		_presets = AddChild<GradientPresetsPanel>();
		_presets.GetCurrentValue = () => Value;
		_presets.OnPresetSelected = preset => Value = preset;
	}
	void BuildControls()
	{
		_controls = AddChild<Panel>( "controls" );
		var row1 = _controls.AddChild<Panel>( "controls-row" );
		_timeControl = row1.AddChild<NumberControl>( "control-field" );
		_timeControl.Icon = "timeline";
		_timeControl.ShowHandle = true;
		_deleteButton = row1.AddChild<ToolbarButton>( "controls-delete" );
		_deleteButton.Icon = "delete";
		_deleteButton.ToolTip = "Remove";
		_deleteButton.OnPressed = DeletePoint;
		row1.AddChild<Panel>( "controls-spacer" );
		var toolbar = row1.AddChild<Panel>( "controls-toolbar" );
		_prevButton = toolbar.AddChild<ToolbarButton>();
		_prevButton.Icon = "chevron_left";
		_prevButton.ToolTip = "Select previous";
		_prevButton.OnPressed = () => SelectNext( false );
		_nextButton = toolbar.AddChild<ToolbarButton>();
		_nextButton.Icon = "chevron_right";
		_nextButton.ToolTip = "Select next";
		_nextButton.OnPressed = () => SelectNext( true );
		var more = toolbar.AddChild<ToolbarButton>();
		more.Icon = "more_horiz";
		more.OnPressed = OpenMoreMenu;
		var row2 = _controls.AddChild<Panel>( "controls-row" );
		_colorControl = row2.AddChild<ColorControl>( "color-control" );
		_alphaControl = row2.AddChild<ColorTrack>( "alpha-control" );
		_alphaControl.Channel = ColorTrack.Channels.Alpha;
		_alphaControl.SetClass( "hidden", true );
		_controlSo = EditorTypeLibrary.GetSerializedObject( this );
		_controlSo.OnPropertyChanged = prop =>
		{
			if ( _colorBar.IsDragging || _alphaBar.IsDragging )
				return;
			UpdateFromPoints();
		};
		_timeControl.Property = _controlSo.GetProperty( nameof( TimeValue ) );
		_colorControl.Property = _controlSo.GetProperty( nameof( ColorValue ) );
		_alphaControl.Property = _controlSo.GetProperty( nameof( AlphaColorValue ) );
		UpdateSelection( null, false );
	}
	void AddStop( GradientStopBarPanel bar, float time, bool color )
	{
		var val = Value;
		if ( color )
			val.AddColor( time, val.Evaluate( time ).WithAlpha( 1 ) );
		else
			val.AddAlpha( time, val.Evaluate( time ).a );
		Value = val;
		SelectPointAt( bar, time, color );
	}
	void SelectPointAt( GradientStopBarPanel bar, float time, bool color )
	{
		GradientStopBarPanel.Stop best = null;
		var bestDelta = float.MaxValue;
		foreach ( var point in bar.Points )
		{
			var delta = MathF.Abs( point.Time - time );
			if ( delta >= bestDelta )
				continue;
			bestDelta = delta;
			best = point;
		}
		if ( best is not null )
			UpdateSelection( best, color );
	}
	[Title( "Color" ), ColorUsage( hasAlpha: false )]
	public Color ColorValue
	{
		get
		{
			if ( _selectedStop is null || !_isColorSelection )
				return Color.White;
			return _selectedStop.Value;
		}
		set
		{
			if ( _selectedStop is null || !_isColorSelection )
				return;
			_selectedStop.Value = value.WithAlpha( 1 );
			_colorBar.UpdateHandlePositions();
			UpdateFromPoints();
		}
	}
	[ColorUsage( hasAlpha: true ), Title( "Alpha" )]
	public Color AlphaColorValue
	{
		get
		{
			if ( _selectedStop is null || _isColorSelection )
				return Color.Black.WithAlpha( 0 );
			return Color.Black.WithAlpha( _selectedStop.Value.a );
		}
		set
		{
			if ( _selectedStop is null || _isColorSelection )
				return;
			_selectedStop.Value = new Color( 0, 0, 0, value.a );
			_alphaBar.UpdateHandlePositions();
			UpdateFromPoints();
		}
	}
	[Range( 0.0f, 1.0f, slider: false ), Step( 0.01f ), Title( "Location" )]
	public float TimeValue
	{
		get => _selectedStop?.Time ?? 0;
		set
		{
			if ( _selectedStop is null )
				return;
			_selectedStop.Time = value;
			_alphaBar.UpdateHandlePositions();
			_colorBar.UpdateHandlePositions();
			UpdateFromPoints();
		}
	}
	public override void Tick()
	{
		base.Tick();
		_multipleLabel.SetClass( "hidden", !(Property?.IsMultipleDifferentValues ?? false) );
		_preview.IsColorSelection = _isColorSelection;
	}
	void UpdateSelection( GradientStopBarPanel.Stop stop, bool color )
	{
		_selectedStop = stop;
		_isColorSelection = color;
		var hasStop = stop is not null;
		_timeControl.ReadOnly = !hasStop;
		_deleteButton.Disabled = !hasStop;
		_prevButton.Disabled = !hasStop;
		_nextButton.Disabled = !hasStop;
		_colorBar.SetSelected( color ? stop : null );
		_alphaBar.SetSelected( color ? null : stop );
		_colorControl.SetClass( "hidden", !color );
		_alphaControl.SetClass( "hidden", color );
		_colorControl.ReadOnly = !hasStop;
		_alphaControl.ReadOnly = !hasStop;
		_colorControl.Rebuild();
		_alphaControl.Rebuild();
		StateHasChanged();
	}
	void UpdatePoints()
	{
		if ( _skipUpdatePoints )
			return;
		if ( _colorBar.IsDragging || _alphaBar.IsDragging )
			return;
		_alphaBar.SyncPoints( Value.Alphas?.Select( x => (x.Time, new Color( 0, 0, 0, x.Value )) ) ?? [] );
		_colorBar.SyncPoints( Value.Colors?.Select( x => (x.Time, x.Value) ) ?? [] );
		_preview.SetValue( Value );
		RebindSelection();
	}
	void UpdateFromPoints( bool notifyExternal = true )
	{
		var val = Value;
		val.Colors = val.Colors?.Clear();
		val.Alphas = val.Alphas?.Clear();
		foreach ( var point in _colorBar.Points )
		{
			if ( point.Disabled ) continue;
			val.AddColor( point.Time, point.Value );
		}
		foreach ( var point in _alphaBar.Points )
		{
			if ( point.Disabled ) continue;
			val.AddAlpha( point.Time, point.Value.a );
		}
		_skipUpdatePoints = true;
		_value = val;
		_preview.SetValue( _value );
		if ( notifyExternal )
			ValueChanged?.Invoke( _value );
		_skipUpdatePoints = false;
		RebindSelection();
		StateHasChanged();
	}
	void RebindSelection()
	{
		if ( _selectedStop is null )
			return;
		if ( _selectedStop.Handle is not { IsValid: true } )
		{
			UpdateSelection( null, _isColorSelection );
			return;
		}
		var bar = _isColorSelection ? _colorBar : _alphaBar;
		if ( bar.Points.Contains( _selectedStop ) )
		{
			bar.SetSelected( _selectedStop );
			return;
		}
		var time = _selectedStop.Time;
		GradientStopBarPanel.Stop match = null;
		var best = float.MaxValue;
		foreach ( var point in bar.Points )
		{
			var delta = MathF.Abs( point.Time - time );
			if ( delta >= best )
				continue;
			best = delta;
			match = point;
		}
		if ( match is not null && best < 0.001f )
			UpdateSelection( match, _isColorSelection );
		else
			UpdateSelection( null, _isColorSelection );
	}
	void DeletePoint()
	{
		if ( _selectedStop is null )
			return;
		if ( _isColorSelection )
		{
			_selectedStop.Handle?.Delete();
			_colorBar.Points.Remove( _selectedStop );
		}
		else
		{
			_selectedStop.Handle?.Delete();
			_alphaBar.Points.Remove( _selectedStop );
		}
		_colorBar.MarkRenderDirty();
		_alphaBar.MarkRenderDirty();
		UpdateSelection( null, false );
		UpdateFromPoints();
	}
	void SelectNext( bool forward )
	{
		if ( _selectedStop is null )
			return;
		var bar = _isColorSelection ? _colorBar : _alphaBar;
		var index = bar.Points.IndexOf( _selectedStop );
		if ( index < 0 )
			return;
		var next = (index + (forward ? 1 : -1) + bar.Points.Count) % bar.Points.Count;
		UpdateSelection( bar.Points[next], _isColorSelection );
	}
	void OpenMoreMenu()
	{
		var menu = new Menu();
		menu.AddOption( "Reverse", "repeat", () =>
		{
			ReverseBar( _colorBar );
			ReverseBar( _alphaBar );
			UpdateFromPoints();
		} );
		menu.AddOption( "Distribute Evenly", "balance", () =>
		{
			DistributeBar( _colorBar );
			DistributeBar( _alphaBar );
			UpdateFromPoints();
		} );
		menu.AddOption( "Remove All", "delete_sweep", () =>
		{
			Value = new Gradient( new Gradient.ColorFrame( 0, Color.White ) );
		} );
		menu.OpenAtCursor( this );
	}
	static void ReverseBar( GradientStopBarPanel bar )
	{
		bar.Points.Reverse();
		for ( var i = 0; i < bar.Points.Count; i++ )
		{
			bar.Points[i].Time = 1f - bar.Points[i].Time;
			bar.Points[i].Index = i;
		}
		bar.UpdateHandlePositions();
	}
	static void DistributeBar( GradientStopBarPanel bar )
	{
		for ( var i = 0; i < bar.Points.Count; i++ )
			bar.Points[i].Time = (float)i / Math.Max( 1, bar.Points.Count - 1 );
		bar.UpdateHandlePositions();
	}
}
