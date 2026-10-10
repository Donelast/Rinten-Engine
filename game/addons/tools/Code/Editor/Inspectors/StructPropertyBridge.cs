namespace Editor.Inspectors;

/// <summary>Writes edits on a boxed struct copy back to its source property, including begin/end edit notes</summary>
public static class StructPropertyBridge
{
	/// <summary>Sends everything <paramref name="inner"/> reports back to the property <paramref name="control"/> is editing.</summary>
	public static void Connect( BaseControl control, SerializedObject inner )
	{
		ArgumentNullException.ThrowIfNull( control );
		ArgumentNullException.ThrowIfNull( inner );

		inner.OnPropertyChanged += _ =>
		{
			if ( control.Property is null )
				return;

			control.Property.SetValue( inner.Targets.FirstOrDefault() );
		};

		inner.OnPropertyStartEdit += _ => control.Property?.Parent?.NoteStartEdit( control.Property );
		inner.OnPropertyFinishEdit += _ => control.Property?.Parent?.NoteFinishEdit( control.Property );
	}
}
