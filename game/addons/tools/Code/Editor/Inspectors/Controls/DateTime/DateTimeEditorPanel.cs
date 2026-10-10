namespace Editor.Inspectors;

using UILabel = Rinten.UI.Label;

[StyleSheet( "DateTimeEditorPanel.cs.scss" )]
public sealed class DateTimeEditorPanel : Panel
{
	Panel header;
	ToolbarButton buttonLeft;
	Panel headerLabelHost;
	UILabel headerLabel;
	TextEntry headerEntry;
	ToolbarButton buttonRight;
	Panel calendar;
	Panel timeRow;

	DateTime _value = DateTime.Now;
	DateTime _viewing;
	bool _timeFieldsBuilt;
	readonly bool _includeTime;

	public Action<DateTime> ValueChanged { get; set; }

	public bool IncludeTime => _includeTime;

	public DateTime Value
	{
		get => _value;
		set
		{
			_value = value;
			_viewing = value;
			RebuildCalendar();
			UpdateHeader();
			ValueChanged?.Invoke( _value );
		}
	}

	public int Hour
	{
		get => _value.Hour;
		set
		{
			var hour = Math.Clamp( value, 0, 23 );
			_value = new DateTime( _value.Year, _value.Month, _value.Day, hour, _value.Minute, _value.Second );
			UpdateHeader();
			ValueChanged?.Invoke( _value );
		}
	}

	public int Minute
	{
		get => _value.Minute;
		set
		{
			var minute = Math.Clamp( value, 0, 59 );
			_value = new DateTime( _value.Year, _value.Month, _value.Day, _value.Hour, minute, _value.Second );
			UpdateHeader();
			ValueChanged?.Invoke( _value );
		}
	}

	public int Second
	{
		get => _value.Second;
		set
		{
			var second = Math.Clamp( value, 0, 59 );
			_value = new DateTime( _value.Year, _value.Month, _value.Day, _value.Hour, _value.Minute, second );
			UpdateHeader();
			ValueChanged?.Invoke( _value );
		}
	}

	public DateTimeEditorPanel( bool includeTime = true )
	{
		_includeTime = includeTime;
		AddClass( "datetime-editor-panel" );
		if ( !includeTime )
			AddClass( "date-only" );

		header = AddChild<Panel>( "header" );

		buttonLeft = header.AddChild<ToolbarButton>();
		buttonLeft.Icon = "arrow_back";
		buttonLeft.OnPressed = ButtonLeft;

		headerLabelHost = header.AddChild<Panel>( "header-label-host" );

		headerLabel = headerLabelHost.AddChild<UILabel>( "header-label" );
		headerLabelHost.AddEventListener( "onmousedown", OnHeaderLabelMouseDown );

		headerEntry = header.AddChild<TextEntry>( "header-entry" );
		headerEntry.SetClass( "hidden", true );
		headerEntry.OnTextEdited = CommitHeaderEntry;

		buttonRight = header.AddChild<ToolbarButton>();
		buttonRight.Icon = "arrow_forward";
		buttonRight.OnPressed = ButtonRight;

		calendar = AddChild<Panel>( "calendar" );

		timeRow = AddChild<Panel>( "time" );

		_viewing = _value;
		RebuildCalendar();
		UpdateHeader();
	}

	void EnsureTimeFields()
	{
		if ( !_includeTime || _timeFieldsBuilt )
			return;

		_timeFieldsBuilt = true;

		var so = EditorTypeLibrary.GetSerializedObject( this );
		so.OnPropertyChanged += _ =>
		{
			UpdateHeader();
			ValueChanged?.Invoke( _value );
		};

		AddTimeField( so, nameof( Hour ), "h", Theme.Yellow );
		AddTimeField( so, nameof( Minute ), "m", Theme.Green );
		AddTimeField( so, nameof( Second ), "s", Theme.Blue );
	}

	void AddTimeField( SerializedObject so, string name, string label, Color color )
	{
		var prop = so.GetProperty( name );
		if ( prop is null )
			return;

		var field = timeRow.AddChild<NumberControl>();
		field.Label = label;
		field.Color = color;
		field.Property = prop;
	}

	void RebuildCalendar()
	{
		if ( !IsValid || calendar is null || !calendar.IsValid )
			return;

		EnsureTimeFields();
		calendar.DeleteChildren();

		var weekdayRow = calendar.AddChild<Panel>( "weekdays" );

		var firstSunday = new DateTime( 1970, 1, 4 );
		for ( var i = 0; i < 7; i++ )
		{
			var weekday = firstSunday.AddDays( i );
			var label = weekdayRow.AddChild<UILabel>( "weekday" );
			label.Text = weekday.DayOfWeek.ToString()[..2];
			SetCalendarCell( label );
		}

		var weeks = calendar.AddChild<Panel>( "weeks" );

		var first = new DateTime( _viewing.Year, _viewing.Month, 1 );
		var startOffset = (int)first.DayOfWeek;
		var daysInMonth = DateTime.DaysInMonth( _viewing.Year, _viewing.Month );
		var totalCells = (int)Math.Ceiling( (startOffset + daysInMonth) / 7.0 ) * 7;

		Panel row = null;
		var dayNumber = 1;

		for ( var i = 0; i < totalCells; i++ )
		{
			if ( i % 7 == 0 )
			{
				row = weeks.AddChild<Panel>( "week" );
			}

			if ( i < startOffset || dayNumber > daysInMonth )
			{
				AddCalendarSpacer( row );
				continue;
			}

			AddDayCell( row, new DateTime( _viewing.Year, _viewing.Month, dayNumber ) );
			dayNumber++;
		}

		buttonLeft.Disabled = _viewing is { Month: 1, Year: 1 };
	}

	static void SetCalendarCell( Panel cell ) => cell.AddClass( "cell" );

	static void AddCalendarSpacer( Panel row )
	{
		var spacer = row.AddChild<Panel>( "spacer" );
		SetCalendarCell( spacer );
	}

	void AddDayCell( Panel row, DateTime day )
	{
		var today = new DateTime( day.Year, day.Month, day.Day );
		var cell = row.AddChild<Panel>( "day" );
		cell.AddClass( "day-cell" );
		SetCalendarCell( cell );

		var selected = day.Year == _value.Year && day.Month == _value.Month && day.Day == _value.Day;
		cell.SetClass( "selected", selected );

		var label = cell.AddChild<UILabel>( "label" );
		label.Text = day.Day.ToString();

		cell.AddEventListener( "onclick", () =>
		{
			Value = _includeTime
				? new DateTime( today.Year, today.Month, today.Day, _value.Hour, _value.Minute, _value.Second )
				: new DateTime( today.Year, today.Month, today.Day );
		} );
	}

	void UpdateHeader()
	{
		if ( !IsValid || headerLabel is null || headerLabelHost is null || headerEntry is null )
			return;

		headerLabel.Text = _viewing.ToString( "MMMM yyyy" );
		headerEntry.Value = _includeTime ? _value.ToString() : DateOnly.FromDateTime( _value ).ToString();
		headerEntry.SetClass( "hidden", true );
		headerLabelHost.SetClass( "hidden", false );
	}

	void ButtonLeft()
	{
		if ( _viewing is { Month: 1, Year: 1 } )
			return;

		_viewing = _viewing.AddMonths( -1 );
		RebuildCalendar();
		UpdateHeader();
	}

	void ButtonRight()
	{
		_viewing = _viewing.AddMonths( 1 );
		RebuildCalendar();
		UpdateHeader();
	}

	void CommitHeaderEntry( string text )
	{
		if ( _includeTime )
		{
			if ( DateTime.TryParse( text, out var result ) )
				Value = result;

			return;
		}

		if ( DateOnly.TryParse( text, out var date ) )
			Value = date.ToDateTime( TimeOnly.MinValue );
	}

	void OnHeaderLabelMouseDown( PanelEvent e )
	{
		if ( e is not MousePanelEvent mpe )
			return;

		if ( mpe.MouseButton is not (MouseButtons.Left or MouseButtons.Right) )
			return;

		OpenHeaderMenu();
		mpe.StopPropagation();
	}

	void OpenHeaderMenu()
	{
		var menu = new Menu();
		var months = new List<DateTime>();
		for ( var i = 0; i < 12; i++ )
			months.Add( new DateTime( _viewing.Year, i + 1, 1 ) );

		menu.AddOptions( months, x => "Set Month/" + x.ToString( "MM MMMM" ), x =>
		{
			_viewing = x;
			RebuildCalendar();
			UpdateHeader();
		}, false, true, "edit_calendar" );

		menu.AddSeparator();
		menu.AddOption( "Edit Value", "edit", () =>
		{
			if ( !IsValid || headerLabelHost is null || headerEntry is null )
				return;

			headerLabelHost.SetClass( "hidden", true );
			headerEntry.SetClass( "hidden", false );
			headerEntry.Value = _includeTime ? _value.ToString() : DateOnly.FromDateTime( _value ).ToString();
			headerEntry.Focus();
		} );
		menu.AddOption( "Set Current Date", "calendar_today", () =>
		{
			var now = DateTime.Now;
			Value = _includeTime
				? new DateTime( now.Year, now.Month, now.Day, _value.Hour, _value.Minute, _value.Second )
				: new DateTime( now.Year, now.Month, now.Day );
		} );

		if ( _includeTime )
		{
			menu.AddOption( "Set Current Time", "access_time", () =>
			{
				var now = DateTime.Now;
				Value = new DateTime( _value.Year, _value.Month, _value.Day, now.Hour, now.Minute, now.Second );
			} );
			menu.AddOption( "Set Current Date + Time", "today", () =>
			{
				var now = DateTime.Now;
				Value = new DateTime( now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second );
			} );
		}

		menu.OpenAtCursor( headerLabelHost );
	}
}
