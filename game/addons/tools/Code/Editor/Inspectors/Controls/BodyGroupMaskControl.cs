namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>A row per body part of the model, choosing which of its bodies is shown.</summary>
[StyleSheet( "BodyGroupMaskControl.cs.scss" )]
[CustomEditor( typeof( ulong ), WithAllAttributes = [typeof( Model.BodyGroupMaskAttribute )] )]
public sealed class BodyGroupMaskControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	int _built;

	public BodyGroupMaskControl()
	{
		ShowBackground = false;
		MultipleRows = true;

	}

	public override void Rebuild()
	{
		DeleteChildren();

		_built = 0;

		if ( Property is null ) return;

		_built = PartsHash();

		var parts = Parts().ToArray();

		if ( !Property.IsMultipleValues && ModelOf( Property ) is null )
		{
			AddNote( "No Model" );
			return;
		}

		if ( parts.Sum( x => x.Choices.Count ) <= 1 )
		{
			AddNote( "None" );
			return;
		}

		foreach ( var part in parts )
		{
			AddChild( new PartRow( this, part ) );
		}
	}

	/// <summary>Which body parts there are comes from the model, which is picked in another row of the same inspector - so the list is watched rather than read once.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( Property is null || PartsHash() == _built ) return;

		Rebuild();
	}

	protected override void OnReadOnlyChanged()
	{
		foreach ( var row in ChildrenOfType<PartRow>() )
			row.SetReadOnly( IsReadOnly );
	}

	//The parts worth showing: the model's, or the ones every selected model shares.
	IEnumerable<Model.BodyPart> Parts()
	{
		if ( Property is null ) return [];

		return Property.IsMultipleValues
			? CommonParts()
			: PartsOf( ModelOf( Property ) );
	}

	//A part only counts when every selected model has it.
	HashSet<Model.BodyPart> CommonParts()
	{
		HashSet<Model.BodyPart> common = null;

		foreach ( var property in Property.MultipleProperties )
		{
			var parts = PartsOf( ModelOf( property ) );

			if ( parts.Count == 0 ) return new();

			if ( common is null )
				common = parts;
			else
				common.IntersectWith( parts );

			if ( common.Count == 0 ) break;
		}

		return common ?? new();
	}

	//A part with one body in it is not a choice, so it isn't offered as one.
	static HashSet<Model.BodyPart> PartsOf( Model model )
	{
		if ( !model.IsValid() ) return new();

		return new HashSet<Model.BodyPart>( model.Parts.All.Where( x => x.Choices.Count > 1 ) );
	}

	static Model ModelOf( SerializedProperty property )
	{
		if ( !property.TryGetAttribute<Model.BodyGroupMaskAttribute>( out var attribute ) )
			return null;

		return ModelProperties.Find( property, attribute.ModelParameter );
	}

	int PartsHash()
	{
		var hash = new HashCode();

		foreach ( var part in Parts() )
			hash.Add( part );

		return hash.ToHashCode();
	}

	void AddNote( string text )
	{
		var label = AddChild<Rinten.UI.Label>( "note" );

		label.Tokenize = false;
		label.Text = text;
	}

	ulong Mask => Property?.GetValue<ulong>() ?? 0;

	//Puts one part's choice into the mask, leaving every other part alone.
	void SetChoice( Model.BodyPart part, ulong choiceMask )
	{
		if ( IsReadOnly || Property is null ) return;

		var mask = (Mask & ~part.Mask) | choiceMask;

		if ( mask == Mask && !Property.IsMultipleDifferentValues ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( mask );
		Property.Parent?.NoteFinishEdit( Property );
	}

	//One body part: what it is called, and which of its bodies is shown.
	private sealed class PartRow : Panel
	{
		readonly BodyGroupMaskControl _control;
		readonly Model.BodyPart _part;
		readonly ControlDropDown _dropdown;

		public PartRow( BodyGroupMaskControl control, Model.BodyPart part )
		{
			_control = control;
			_part = part;

			AddClass( "part" );

			var label = AddChild<Rinten.UI.Label>( "name" );
			label.Tokenize = false;
			label.Text = $"{part.Name}:";

			_dropdown = AddChild<ControlDropDown>();
			_dropdown.Options = part.Choices
				.Select( x => new Rinten.UI.Option( x.Name, x.Mask ) )
				.ToList();
			_dropdown.ValueChanged = OnPicked;

			SetReadOnly( control.IsReadOnly );
			SyncDisplay();
		}

		public override void Tick()
		{
			base.Tick();

			SyncDisplay();
		}

		public void SetReadOnly( bool readOnly )
		{
			_dropdown.Disabled = readOnly;
			_dropdown.AcceptsFocus = !readOnly;
		}

		void OnPicked( string value )
		{
			if ( !ulong.TryParse( value, out var choiceMask ) ) return;

			_control.SetChoice( _part, choiceMask );
		}

		void SyncDisplay()
		{
			if ( _control.Property is null ) return;

			if ( _control.Property.IsMultipleDifferentValues )
			{
				_dropdown.SetClass( "multiple", true );
				_dropdown.Text = "Multiple Values";
				return;
			}

			_dropdown.SetClass( "multiple", false );

			var value = $"{_control.Mask & _part.Mask}";

			if ( !string.Equals( _dropdown.Value as string, value, StringComparison.Ordinal ) )
				_dropdown.Value = value;
		}
	}
}
