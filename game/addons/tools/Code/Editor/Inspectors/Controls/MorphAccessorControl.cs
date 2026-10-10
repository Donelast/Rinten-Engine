namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>The morphs a model has, grouped, each with a strength and a left/right blend.</summary>
[StyleSheet( "MorphAccessorControl.cs.scss" )]
[CustomEditor( typeof( SkinnedModelRenderer.MorphAccessor ) )]
public sealed class MorphAccessorControl : BaseControl
{
	/// <summary>The rows name themselves, so the property's own name adds nothing - unless there are no rows, when the name is all there is to go on.</summary>
	public override bool IncludeLabel => (Accessor?.Names?.Length ?? 0) == 0;

	const string SortCookie = "morph_sort_alphabetically";

	bool _sortAlphabetically = EditorCookie.Get( SortCookie, false );
	readonly List<MorphRowPanel> _rows = new();

	int _built;

	public MorphAccessorControl()
	{
		ShowBackground = false;
		MultipleRows = true;

	}

	SkinnedModelRenderer.MorphAccessor Accessor => Property?.GetValue<SkinnedModelRenderer.MorphAccessor>();

	public override void Rebuild()
	{
		DeleteChildren();

		_rows.Clear();
		_built = 0;

		if ( Property is null ) return;

		var names = Accessor?.Names;

		//No renderer behind it, or a model with no morphs - say so rather than leaving the row blank.
		if ( names is null || names.Length == 0 )
		{
			var note = AddChild<Rinten.UI.Label>( "note" );

			note.Tokenize = false;
			note.Text = "None";

			return;
		}

		_built = NamesHash( names );

		BuildHeader();
		BuildRows( names );
	}

	/// <summary>Which morphs there are follows the model, which is picked elsewhere.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( Property is null ) return;

		if ( NamesHash( Accessor?.Names ) != _built )
			Rebuild();
	}

	protected override void OnReadOnlyChanged()
	{
	}

	static int NamesHash( string[] names )
	{
		if ( names is null ) return 0;

		var hash = names.Length;

		foreach ( var name in names )
			hash = HashCode.Combine( hash, name );

		return hash;
	}

	void BuildHeader()
	{
		var header = AddChild<Panel>( "header" );

		var clear = header.AddChild<ToolbarButton>();
		clear.Icon = "replay";
		clear.ToolTip = "Clear All";
		clear.OnPressed = () => _rows.ForEach( x => x.Clear() );

		var random = header.AddChild<ToolbarButton>();
		random.Icon = "casino";
		random.ToolTip = "Randomize";
		random.OnPressed = () => _rows.ForEach( x => x.SetValues( Random.Shared.Float( 0, 1 ), Random.Shared.Float( 0, 1 ) ) );

		header.AddChild<Panel>( "spacer" );

		var sort = header.AddChild<ToolbarButton>();
		sort.Icon = "sort_by_alpha";
		sort.ToolTip = "Sort Alphabetically";
		sort.Active = _sortAlphabetically;
		sort.OnPressed = () =>
		{
			_sortAlphabetically = !_sortAlphabetically;
			EditorCookie.Set( SortCookie, _sortAlphabetically );
			Rebuild();
		};
	}

	void BuildRows( string[] names )
	{
		var body = AddChild<Panel>( "groups" );

		foreach ( var (key, rows) in Group( BuildRowPanels( names ) ) )
		{
			var label = body.AddChild<Rinten.UI.Label>( "group" );
			label.Tokenize = false;
			label.Text = key;

			foreach ( var row in rows )
				body.AddChild( row );
		}
	}

	//A row per morph, except where two of them are the left and right of the same
	//thing - those share a row, with one slider for strength and one for which side.
	List<MorphRowPanel> BuildRowPanels( string[] names )
	{
		var unprocessed = names.ToList();

		foreach ( var pair in names.GroupBy( x => x[..^1] ).ToArray() )
		{
			if ( pair.Count() != 2 ) continue;
			if ( !pair.Any( x => x.ToLower().EndsWith( 'l' ) ) && !pair.Any( x => x.ToLower().EndsWith( 'r' ) ) ) continue;

			unprocessed.RemoveAll( x => pair.Contains( x ) );

			_rows.Add( new MorphRowPanel( Property, pair.Order().ToArray(), pair.Key ) );
		}

		foreach ( var name in unprocessed )
		{
			_rows.Add( new MorphRowPanel( Property, [name], name ) );
		}

		return _rows;
	}

	//The rows by group, in the order the sort button asked for.
	IEnumerable<(string Key, IEnumerable<MorphRowPanel> Rows)> Group( List<MorphRowPanel> rows )
	{
		var groups = rows
			.GroupBy( x => GroupOf( x.Title ) )
			.Select( x => (Key: x.Key, Rows: x.AsEnumerable()) );

		if ( !_sortAlphabetically )
			return groups;

		return groups
			.OrderBy( x => x.Key )
			.Select( x => (x.Key, Rows: x.Rows.OrderBy( y => y.Title ).AsEnumerable()) );
	}

	static string GroupOf( string title )
	{
		if ( title.Contains( "brow", StringComparison.OrdinalIgnoreCase ) ) return "Eyes";
		if ( title.Contains( "eye", StringComparison.OrdinalIgnoreCase ) ) return "Eyes";
		if ( title.Contains( "cheek", StringComparison.OrdinalIgnoreCase ) ) return "Face";
		if ( title.Contains( "nose", StringComparison.OrdinalIgnoreCase ) ) return "Face";
		if ( title.Contains( "nostril", StringComparison.OrdinalIgnoreCase ) ) return "Face";
		if ( title.Contains( "chin", StringComparison.OrdinalIgnoreCase ) ) return "Face";
		if ( title.Contains( "jaw", StringComparison.OrdinalIgnoreCase ) ) return "Mouth";
		if ( title.Contains( "lip", StringComparison.OrdinalIgnoreCase ) ) return "Mouth";
		if ( title.Contains( "mouth", StringComparison.OrdinalIgnoreCase ) ) return "Mouth";

		return "Misc";
	}
}

//One morph, or the left and right of one, as a title and up to two sliders.
sealed class MorphRowPanel : Panel
{
	readonly SkinnedModelRenderer.MorphAccessor _accessor;
	readonly SerializedProperty[] _properties;
	readonly string[] _keys;

	readonly Rinten.UI.Label _label;
	readonly SliderControl _strength;
	readonly SliderControl _side;

	float _appliedLeft;
	float _appliedRight;

	public string Title { get; }

	public MorphRowPanel( SerializedProperty property, string[] names, string title )
	{
		_accessor = property.GetValue<SkinnedModelRenderer.MorphAccessor>();
		_keys = names;

		var target = _accessor.GetSerialized();
		target.ParentProperty = property;

		//Editing a morph is not writing a property, so each key is given one to say that
		//it changed - that is what carries the edit to undo and to whatever is watching.
		_properties = names
			.Select( x => Game.TypeLibrary.CreateProperty<float>( x, () => _accessor.Get( x ), value => _accessor.Set( x, value ), null, target ) )
			.ToArray();

		Title = Prettify( title );

		AddClass( "morph-row" );

		_label = AddChild<Rinten.UI.Label>( "title" );
		_label.Tokenize = false;
		_label.Text = Title;
		_label.AddEventListener( "onclick", ClearIfSet );

		_strength = AddChild<SliderControl>();
		_strength.AddClass( "strength" );
		_strength.Min = 0;
		_strength.Max = 1;
		_strength.Step = 0.01f;
		_strength.ShowScale = false;
		_strength.OnValueChanged = _ => Write();
		_strength.Tooltip = "Strength";

		if ( names.Length > 1 )
		{
			_side = AddChild<SliderControl>();
			_side.AddClass( "side" );
			_side.Min = -1;
			_side.Max = 1;
			_side.Step = 0.01f;
			_side.ShowScale = false;
			_side.OnValueChanged = _ => Write();
			_side.Tooltip = "Left/Right";
		}

		Read();
	}

	public override void Tick()
	{
		base.Tick();

		//Somebody else changed this morph - cleared them all, or undid an edit.
		if ( Left() != _appliedLeft || Right() != _appliedRight )
			Read();

		_label.SetClass( "active", HasOverride() );
	}

	/// <summary>Sets every key of this row, which is what Randomize does.</summary>
	public void SetValues( params float[] values )
	{
		DispatchPreEdited();

		for ( var i = 0; i < _keys.Length; i++ )
		{
			if ( i < values.Length )
				_accessor.Set( _keys[i], values[i] );
			else
				_accessor.Clear( _keys[i] );
		}

		DispatchEdited();

		Read();
	}

	public void Clear()
	{
		DispatchPreEdited();

		foreach ( var key in _keys )
			_accessor.Clear( key );

		DispatchEdited();

		Read();
	}

	bool HasOverride() => _accessor.ContainsOverride( _keys[0] );

	void ClearIfSet()
	{
		if ( !HasOverride() ) return;

		Clear();
	}

	float Left() => Value( _keys[0] );

	float Right() => _keys.Length > 1 ? Value( _keys[1] ) : 0f;

	float Value( string key ) => _accessor.ContainsOverride( key ) ? _accessor.Get( key ) : 0f;

	//Puts what the accessor holds onto the sliders.
	void Read()
	{
		var left = Left();
		var right = Right();

		_appliedLeft = left;
		_appliedRight = right;

		if ( _side is null )
		{
			_strength.Value = left;
			return;
		}

		_strength.Value = MathF.Max( left, right );

		if ( right > left ) _side.Value = left.Remap( 0, right, -1, 0 );
		else if ( left > right ) _side.Value = right.Remap( 0, left, 1, 0 );
		else _side.Value = 0;
	}

	//Puts what is on the sliders back onto the accessor.
	void Write()
	{
		DispatchPreEdited();

		if ( _side is not null )
		{
			_accessor.Set( _keys[0], _strength.Value * _side.Value.Remap( 0, -1, 1, 0 ) );
			_accessor.Set( _keys[1], _strength.Value * _side.Value.Remap( 0, 1, 1, 0 ) );
		}
		else
		{
			foreach ( var key in _keys )
				_accessor.Set( key, _strength.Value );
		}

		DispatchEdited();

		//What is on the sliders is what was asked for, so it is not worked out again
		//from the accessor - that would snap the blend around while it is being dragged.
		_appliedLeft = Left();
		_appliedRight = Right();
	}

	void DispatchPreEdited()
	{
		foreach ( var property in _properties )
			property.DispatchPreEdited();
	}

	void DispatchEdited()
	{
		foreach ( var property in _properties )
			property.DispatchEdited();
	}

	//Morph names run words together, so they are put back into words.
	static string Prettify( string title )
	{
		string[] words =
		[
			"lower", "upper", "raiser", "inflate", "bulge", "suck", "thrust", "sideways",
			"depressor", "corner", "puller", "pucker", "wrinkle", "jaw", "mouth"
		];

		foreach ( var word in words )
			title = title.Replace( word, char.ToUpper( word[0] ) + word[1..] );

		return title.ToTitleCase();
	}
}
