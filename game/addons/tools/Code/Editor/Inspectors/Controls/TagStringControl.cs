namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>A string of tags, edited as chips: type a word and a space to add one, click one to drop it.</summary>
[StyleSheet( "TagStringControl.cs.scss" )]
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( TagStringAttribute )] )]
public sealed class TagStringControl : BaseControl
{
	readonly Panel _chips;
	readonly TextEntry _entry;

	readonly List<string> _tags = new();

	string _shown;

	public override bool SupportsMultiEdit => true;

	public TagStringControl()
	{
		ShowBackground = false;
		AddClass( "tag-string" );

		_chips = AddChild<Panel>( "chips" );

		_entry = AddChild<TextEntry>();
		_entry.OnTextEdited = OnTyped;
	}

	public override void Rebuild()
	{
		_shown = null;
		Read();
	}

	public override void Tick()
	{
		base.Tick();

		//Somebody else may have written the property; the field is left alone while it has the focus so a half-typed tag is not swallowed.
		if ( !_entry.HasFocus )
			Read();
	}

	protected override void OnReadOnlyChanged()
	{
		if ( _entry is null ) return;

		_entry.Disabled = IsReadOnly;
	}

	protected override void OnBlur( PanelEvent e )
	{
		base.OnBlur( e );

		//Whatever is left in the field becomes a tag when editing stops.
		Convert( all: true );
	}

	//Reads the property back into chips and whatever text has not become one.
	void Read()
	{
		if ( Property is null ) return;

		var value = Property.IsMultipleDifferentValues ? "" : Property.As.String ?? "";

		if ( _shown == value ) return;

		_shown = value;
		_tags.Clear();

		var rest = new List<string>();

		foreach ( var part in value.SplitQuotesStrings() )
		{
			if ( IsTag( part, out var tag ) && !_tags.Contains( tag ) ) _tags.Add( tag );
			else rest.Add( part );
		}

		_entry.Value = string.Join( ' ', rest.Select( x => x.QuoteSafe( true ) ) );

		SetClass( "multiple", Property.IsMultipleDifferentValues );
		BuildChips();
	}

	void OnTyped( string text )
	{
		//A space is what ends a tag - until then it is still being typed.
		if ( string.IsNullOrEmpty( text ) || !char.IsWhiteSpace( text[^1] ) ) return;

		Convert( all: false );
	}

	//Turns finished words in the field into chips; the last one only once it is finished.
	void Convert( bool all )
	{
		var text = _entry.Value ?? "";
		var parts = text.SplitQuotesStrings().ToList();

		if ( parts.Count == 0 && !all ) return;

		var keep = new List<string>();
		var added = false;

		for ( var i = 0; i < parts.Count; i++ )
		{
			var last = i == parts.Count - 1;

			if ( !all && last && !char.IsWhiteSpace( text[^1] ) )
			{
				keep.Add( parts[i] );
				continue;
			}

			if ( !IsTag( parts[i], out var tag ) )
			{
				keep.Add( parts[i] );
				continue;
			}

			if ( !_tags.Contains( tag ) ) _tags.Add( tag );

			added = true;
		}

		var rest = string.Join( ' ', keep.Select( x => x.QuoteSafe( true ) ) );

		if ( !all && text.EndsWith( " " ) && keep.Count > 0 )
			rest += " ";

		//Written through Text rather than Value: Value refuses to write while the field has the focus, which is exactly when a tag is being finished.
		_entry.Text = rest;
		_entry.CaretPosition = rest.Length;

		if ( !added ) return;

		BuildChips();
		Write( rest );
	}

	void BuildChips()
	{
		_chips.DeleteChildren();

		foreach ( var tag in _tags )
		{
			var value = tag;
			var chip = _chips.AddChild( new Rinten.UI.Button( tag, "local_offer" ) );
			chip.AddClass( "chip" );
			chip.Tooltip = $"Click to remove '{tag}'";
			chip.AddEventListener( "onclick", () => Remove( value ) );
		}

		_chips.SetClass( "empty", _tags.Count == 0 );
	}

	void Remove( string tag )
	{
		if ( IsReadOnly ) return;
		if ( !_tags.Remove( tag ) ) return;

		BuildChips();
		Write( _entry.Value ?? "" );
	}

	//The chips, then anything still being typed - which is what the widget stored.
	void Write( string rest )
	{
		if ( IsReadOnly || Property is null ) return;

		var value = string.Join( " ", _tags.Select( x => x.QuoteSafe( true ) ) );

		rest = rest.Trim();
		if ( !string.IsNullOrWhiteSpace( rest ) )
			value = $"{value} {rest}".Trim();

		_shown = value;

		Property.Parent?.NoteStartEdit( Property );
		Property.As.String = value;
		Property.Parent?.NoteFinishEdit( Property );
	}

	static bool IsTag( string part, out string tag )
	{
		tag = part?.ToLowerInvariant();

		return tag.IsValidTag();
	}
}
