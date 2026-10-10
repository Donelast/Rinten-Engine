namespace Editor.Inspectors;

/// <summary>Inline <see cref="ControlSheet"/> for properties marked with <see cref="InlineEditorAttribute"/>.</summary>
[StyleSheet( "InlineEditorControl.cs.scss" )]
[CustomEditor( typeof( object ), WithAllAttributes = [ typeof( InlineEditorAttribute ) ] )]
public class InlineEditorControl : BaseControl
{
	ControlSheet _sheet;
	bool _wasNull;

	public override bool SupportsMultiEdit => true;

	public InlineEditorControl()
	{
		ShowBackground = false;
		MultipleRows = true;

		_sheet = AddChild<ControlSheet>();
	}

	public override void Rebuild()
	{
		SyncSheet();
	}

	public override void Tick()
	{
		base.Tick();

		if ( Property is null || !Property.IsNullable ) return;

		var isNull = Property.IsNull;
		if ( isNull == _wasNull ) return;

		_wasNull = isNull;
		SyncSheet();
	}

	void SyncSheet()
	{
		if ( Property is null ) return;

		_wasNull = Property.IsNullable && Property.IsNull;

		if ( _wasNull )
		{
			var preview = Property.GetDefault();
			if ( preview is not null )
			{
				var so = preview.GetSerialized();
				so.ParentProperty = Property;
				_sheet.Target = so;
				_sheet.Rebuild();
				return;
			}

			_sheet.ClearBody();
			return;
		}

		if ( Property.TryGetAsObject( out var so2 ) )
		{
			_sheet.Target = so2;
			_sheet.Rebuild();
		}
	}
}
