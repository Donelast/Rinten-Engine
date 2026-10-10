using Rinten.Internal;
using System.Linq.Expressions;
using System.Reflection;

namespace Editor;

public class ControlSheet : GridLayout, IControlSheet
{
	int rows = 0;
	readonly List<ControlSheetRow> sheetRows = new();
	float labelColumn;

	public bool ShowPropertyNameInTooltip = false;

	Func<SerializedProperty, bool> _filter;

	public ControlSheet() : base()
	{
		Margin = 0;
		HorizontalSpacing = 0;

		//A gap between one property and the next. Nought reads as a wall of text: the rows are the same height
		//and the same colour, and with nothing between them the eye has to count labels to see where one ends.
		VerticalSpacing = 2;
	}

	public void AddObject( SerializedObject obj, Func<SerializedProperty, bool> filter = null )
	{
		Margin = new Rinten.UI.Margin( 0, 0, 0, 0 );

		if ( obj is null )
			return;

		_filter = filter;
		IControlSheet.FilterSortAndAdd( this, obj.ToList() );
		FinalizeRows();
		_filter = default;
	}

	//Aligns label columns after every row has been created.
	internal void FinalizeRows()
	{
		labelColumn = 0;

		foreach ( var row in sheetRows )
			labelColumn = MathF.Max( labelColumn, row.LabelWantedWidth );

		foreach ( var row in sheetRows )
			row.FitLabelColumn( labelColumn );
	}

	public static Widget CreateRow( SerializedProperty property, bool showPropertyNameInTooltip = false )
	{
		return ControlSheetRow.Create( property, showPropertyNameInTooltip );
	}

	Guid FindGuid( SerializedProperty sp )
	{
		return FindGuid( sp?.Parent );
	}

	Guid FindGuid( SerializedObject so )
	{
		if ( so is null ) return Guid.Empty;

		foreach ( var target in so.Targets )
		{
			if ( target is GameObject go ) return go.Id;
			if ( target is Component cmp ) return cmp.Id;
			if ( target is Resource rc ) return new Guid( rc.ResourcePath.FastHash(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 );
		}

		return FindGuid( so.ParentProperty?.Parent );
	}

	public void AddGroup( string groupName, SerializedProperty[] props )
	{
		try
		{
			var widget = new ControlSheetGroup( groupName, props, ShowPropertyNameInTooltip );
			AddCell( 0, rows++, widget, xSpan: 2 );

			widget.UpdateVisibility();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"ControlGroupWidget: {e.Message}" );
		}
	}

	/// <summary>Add a serialized property row. This will create an editor for the row and a label.</summary>
	public ControlWidget AddRow( SerializedProperty property, float labelIndent = 0.0f )
	{
		var row = ControlSheetRow.Create( property, ShowPropertyNameInTooltip );
		if ( !row.IsValid() ) return null;

		SetColumnStretch( 0 );
		AddCell( 0, rows++, row );
		NoteRow( row );
		return row.ControlWidget;
	}

	/// <summary>Add a property but force it to use a specific type of ControlWidhet</summary>
	public T AddControl<T>( SerializedProperty property, float labelIndent = 0.0f ) where T : ControlWidget
	{
		var editor = EditorTypeLibrary.Create<T>( typeof( T ), [property] );

		var row = ControlSheetRow.Create( property, editor, ShowPropertyNameInTooltip );
		if ( !row.IsValid() ) return null;

		SetColumnStretch( 0 );
		AddCell( 0, rows++, row );
		NoteRow( row );
		return (T)row.ControlWidget;
	}

	/// <summary>Add a layout to a double wide cell</summary>
	public void AddLayout( Layout layout )
	{
		AddCell( 0, rows++, layout, 2, 1, TextFlag.LeftTop );
	}

	public static ControlSheet Create( SerializedObject so, Func<SerializedProperty, bool> filter = null )
	{
		var cs = new ControlSheet();
		cs.AddObject( so, filter );
		return cs;
	}

	public void AddProperty<T, U>( T target, Expression<Func<T, U>> property )
	{
		if ( property.Body is MemberExpression member )
		{
			var name = member.Member.Name;
			AddProperty( target, name );
		}
	}

	public ControlWidget AddProperty<T>( Expression<Func<T>> property )
	{
		if ( property.Body is not MemberExpression member || member.Member is not PropertyInfo propertyInfo )
			throw new ArgumentException( "Expression must target a property.", nameof( property ) );

		if ( propertyInfo.GetMethod is null || propertyInfo.SetMethod is null )
			throw new InvalidOperationException( $"Property '{propertyInfo.Name}' must have a getter and a setter." );

		var getter = (Func<T>)Delegate.CreateDelegate( typeof( Func<T> ), propertyInfo.GetMethod );
		var setter = (Action<T>)Delegate.CreateDelegate( typeof( Action<T> ), propertyInfo.SetMethod );

		var row = ControlSheetRow.Create( new Inspectors.ReflectionSerializedProperty<T>( propertyInfo, getter, setter ), ShowPropertyNameInTooltip );
		if ( !row.IsValid() )
			return null;

		SetColumnStretch( 0 );
		AddCell( 0, rows++, row );
		NoteRow( row );
		return row.ControlWidget;
	}

	private void AddProperty<T>( T target, string name )
	{
		var so = target.GetSerialized();
		AddRow( so.GetProperty( name ) );
	}

	public static Widget CreateLabel( SerializedProperty property )
	{
		return new ControlSheetLabel( property );
	}

	FeatureTabWidget featureTabs;

	void IControlSheet.AddFeature( IControlSheet.Feature feature )
	{
		if ( featureTabs is null )
		{
			featureTabs = new FeatureTabWidget( null );

			var guid = FindGuid( feature.Properties.FirstOrDefault() );
			if ( guid != Guid.Empty )
			{
				featureTabs.StateCookie = guid.ToString();
			}

			AddCell( 0, rows++, featureTabs, xSpan: 2 );
		}

		featureTabs.AddFeature( feature );
	}

	void IControlSheet.AddGroup( IControlSheet.Group group )
	{
		try
		{
			var widget = new ControlSheetGroup( group.Name, group.Properties.ToArray(), ShowPropertyNameInTooltip );
			AddCell( 0, rows++, widget, xSpan: 2 );

			widget.UpdateVisibility();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"ControlGroupWidget: {e.Message}" );
		}
	}

	bool IControlSheet.TestFilter( SerializedProperty prop )
	{
		if ( _filter is null ) return true;
		return _filter.Invoke( prop );
	}

	void NoteRow( ControlSheetRow row )
	{
		sheetRows.Add( row );
		labelColumn = MathF.Max( labelColumn, row.LabelWantedWidth );

		foreach ( var existing in sheetRows )
			existing.FitLabelColumn( labelColumn );
	}

	internal void RefreshEditability()
	{
		foreach ( var row in sheetRows )
		{
			if ( row.UpdateEditability() )
				row.Update();
		}
	}

	//Updates row visibility from inspector conditions without rebuilding the sheet.
	internal bool RefreshVisibility()
	{
		var changed = false;

		foreach ( var row in sheetRows )
			changed |= row.UpdateVisibility();

		return changed;
	}
}
