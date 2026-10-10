namespace Editor.Inspectors;

using UILabel = Rinten.UI.Label;

public abstract class DateTimeControlBase : BaseControl
{
	UILabel valueLabel;
	DateTimeEditorPopup editorPopup;

	protected virtual bool IncludeTime => true;

	protected virtual string DisplayIcon => "access_time";

	protected virtual string FormatValue( DateTime value ) => value.ToString();

	protected DateTimeControlBase()
	{
		ShowBackground = false;
		AddClass( "datetime-control" );
	}

	public override bool SupportsMultiEdit => true;

	public override void Rebuild()
	{
		DeleteChildren();

		ControlHandle.AddTo( this, icon: DisplayIcon, color: Theme.Yellow );

		var field = AddChild<Panel>( "field" );
		valueLabel = field.AddChild<UILabel>( "value" );
		UpdateDisplay();
	}

	public override void Tick()
	{
		base.Tick();

		if ( editorPopup is not null && !editorPopup.IsValid() )
			editorPopup = null;

		UpdateDisplay();
	}

	protected override void OnReadOnlyChanged()
	{
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( Property is null )
			return;

		if ( e.MouseButton != MouseButtons.Left )
			return;

		if ( IsReadOnly )
			return;

		OpenEditorPopup();
		e.StopPropagation();
	}

	protected abstract DateTime ReadValue();
	protected abstract void WriteValue( DateTime value );

	void UpdateDisplay()
	{
		if ( valueLabel is null || Property is null )
			return;

		if ( Property.IsMultipleDifferentValues )
		{
			valueLabel.Text = "Multiple Values";
			return;
		}

		valueLabel.Text = FormatValue( ReadValue() );
	}

	void OpenEditorPopup()
	{
		if ( editorPopup is { IsValid: true } )
		{
			editorPopup.Delete();
			editorPopup = null;
			return;
		}

		editorPopup = DateTimeEditorPopup.Open( this, ReadValue(), WriteValue, IncludeTime );
	}

	public override void OnPropertyContextMenu( Menu menu )
	{
		if ( Property is null ) return;

		var editable = !IsReadOnly;
		var dateTime = ReadValue();

		//The months are added one by one rather than through AddOptions, which hands back nothing to say a row cannot be chosen.
		var months = menu.AddMenu( "Set Month", "edit_calendar" );

		for ( var i = 0; i < 12; i++ )
		{
			var month = new DateTime( dateTime.Year, i + 1, 1 );

			months.AddOption( month.ToString( "MM MMMM" ), action: () => WriteValue( month ) ).Enabled = editable;
		}

		menu.AddSeparator();
		menu.AddOption( "Set Current Date", "calendar_today", () =>
		{
			var now = DateTime.Now;
			WriteValue( IncludeTime
				? new DateTime( now.Year, now.Month, now.Day, dateTime.Hour, dateTime.Minute, dateTime.Second )
				: new DateTime( now.Year, now.Month, now.Day ) );
		} ).Enabled = editable;

		if ( !IncludeTime ) return;

		menu.AddOption( "Set Current Time", "access_time", () =>
		{
			var now = DateTime.Now;
			WriteValue( new DateTime( dateTime.Year, dateTime.Month, dateTime.Day, now.Hour, now.Minute, now.Second ) );
		} ).Enabled = editable;

		menu.AddOption( "Set Current Date + Time", "today", () =>
		{
			var now = DateTime.Now;
			WriteValue( new DateTime( now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second ) );
		} ).Enabled = editable;
	}
}

[StyleSheet( "DateTimeControl.cs.scss" )]
[CustomEditor( typeof( DateTime ) )]
public sealed class DateTimeControl : DateTimeControlBase
{
	protected override DateTime ReadValue() => Property?.GetValue<DateTime>() ?? default;

	protected override void WriteValue( DateTime value ) => Property?.SetValue( value );
}

[StyleSheet( "DateTimeControl.cs.scss" )]
[CustomEditor( typeof( DateTimeOffset ) )]
public sealed class DateTimeOffsetControl : DateTimeControlBase
{
	protected override DateTime ReadValue() => Property?.GetValue<DateTimeOffset>().DateTime ?? default;

	protected override void WriteValue( DateTime value ) => Property?.SetValue( new DateTimeOffset( value ) );
}

[StyleSheet( "DateTimeControl.cs.scss" )]
[CustomEditor( typeof( DateOnly ) )]
public sealed class DateOnlyControl : DateTimeControlBase
{
	protected override bool IncludeTime => false;

	protected override string DisplayIcon => "calendar_today";

	protected override string FormatValue( DateTime value ) => DateOnly.FromDateTime( value ).ToString();

	protected override DateTime ReadValue()
	{
		if ( Property is null )
			return default;

		return Property.GetValue<DateOnly>().ToDateTime( TimeOnly.MinValue );
	}

	protected override void WriteValue( DateTime value ) => Property?.SetValue( DateOnly.FromDateTime( value ) );
}
