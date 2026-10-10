namespace Editor.Inspectors;

using Rinten.Internal;

//Candidate editors for a property, best first; filtered once and scored without allocating
static class ControlEditors
{
	static (TypeDescription Type, CustomEditorAttribute Attribute)[] _game;
	static (TypeDescription Type, CustomEditorAttribute Attribute)[] _editor;

	//Scratch for scoring, so choosing an editor allocates nothing. Handed straight back to the caller, so it
	//holds good only until the next call: read it out before creating anything that could ask for an editor too.
	[ThreadStatic]
	static List<(float Score, TypeDescription Type)> _scored;

	/// <summary>The editors game code registers, which is where a project's own controls are.</summary>
	public static List<(float Score, TypeDescription Type)> ForGame( SerializedProperty property )
	{
		return Score( _game ??= Filter( Game.TypeLibrary ), property );
	}

	/// <summary>The editors the tools themselves register.</summary>
	public static List<(float Score, TypeDescription Type)> ForEditor( SerializedProperty property )
	{
		return Score( _editor ??= Filter( EditorTypeLibrary ), property );
	}

	[EditorEvent.Hotload]
	static void FlushCache()
	{
		_game = null;
		_editor = null;
	}

	//Everything with a CustomEditorAttribute that is actually a control.
	static (TypeDescription Type, CustomEditorAttribute Attribute)[] Filter( TypeLibrary library )
	{
		if ( library is null ) return [];

		return library.GetTypesWithAttribute<CustomEditorAttribute>( false )
			.Where( x => x.Type.TargetType.IsAssignableTo( typeof( BaseControl ) ) )
			.Select( x => (x.Type, x.Attribute) )
			.ToArray();
	}

	static List<(float Score, TypeDescription Type)> Score(
		(TypeDescription Type, CustomEditorAttribute Attribute)[] editors, SerializedProperty property )
	{
		var scored = _scored ??= new List<(float Score, TypeDescription Type)>();

		scored.Clear();

		foreach ( var (type, attribute) in editors )
		{
			var score = attribute.GetEditorScore( property );

			if ( score <= 0 ) continue;

			scored.Add( (score, type) );
		}

		scored.Sort( static ( a, b ) => b.Score.CompareTo( a.Score ) );

		return scored;
	}
}
