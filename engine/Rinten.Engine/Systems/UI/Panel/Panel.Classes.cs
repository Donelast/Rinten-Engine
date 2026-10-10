namespace Rinten.UI;

public partial class Panel
{
	/// <summary>A list of CSS classes applied to this panel.</summary>
	[Hide]
	public IEnumerable<string> Class => _class ?? Enumerable.Empty<string>();

	internal HashSet<string> _class;
	internal string _classes;

	//Order-independent hash of the class set, for cheap change detection without building Classes.
	//The count is in it too, so a set and the same set with a class whose hash is nought added don't read the same.
	internal int ClassHash
	{
		get
		{
			if ( _class is null ) return 0;

			int hash = 0;
			foreach ( var c in _class ) hash += c.GetHashCode();
			return HashCode.Combine( hash, _class.Count );
		}
	}

	/// <summary>All CSS classes applied to this panel, separated with spaces.</summary>
	[Property]
	public string Classes
	{
		get
		{
			if ( _classes == null )
				_classes = string.Join( " ", Class );

			return _classes;
		}
		set
		{
			bool had = _class != null && _class.Count > 0;

			_class?.Clear();
			_classes = null;

			if ( had )
				StyleSelectorsChanged( true, true );

			AddClass( value );
		}
	}

	/// <summary>Adds CSS class(es) separated by spaces to this panel.</summary>
	public void AddClass( string classname )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		if ( classname.Contains( ' ' ) )
		{
			AddClasses( classname );
			return;
		}

		//Asked before lowering it. The set ignores case, so it can answer as the name arrived - and the answer is nearly always yes: every widget of an editor
		//sets the classes it already has once a frame, and lowering a name that is already in there was a string allocated per widget per frame for nothing.
		_class ??= new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		if ( _class.Contains( classname ) ) return;

		_class.Add( classname.ToLowerInvariant() );
		_classes = null;

		//Adding a class changes the selector so children may have rules that now match - need to update those too.
		StyleSelectorsChanged( true, true );
	}

	/// <summary>Sets a specific CSS class active or not.</summary>
	public void SetClass( string classname, bool active )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		if ( active ) AddClass( classname );
		else RemoveClass( classname );
	}

	/// <summary>Add a class for a set amount of seconds. If called multiple times, we will stomp the earlier call.</summary>
	public void FlashClass( string classname, float seconds )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		AddClass( classname );
		InvokeOnce( $"FlashClass;{classname}", seconds, () => RemoveClass( classname ) );
	}

	/// <summary>Add a class if we don't have it, remove a class if we do have it</summary>
	public void ToggleClass( string classname )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		SetClass( classname, !HasClass( classname ) );
	}

	/// <summary>Adds several space-separated classes, invalidating styles once instead of once per class</summary>
	public void AddClasses( System.Collections.Generic.IReadOnlyList<string> names )
	{
		if ( names is null || names.Count == 0 ) return;

		var added = false;

		_class ??= new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var name in names )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			//Lowercased like the single-name path, so what a stylesheet is matched against does not depend on which door the class came in by.
			if ( !_class.Add( name.ToLowerInvariant() ) ) continue;

			added = true;
		}

		if ( !added ) return;

		_classes = null;

		StyleSelectorsChanged( true, true );
	}

	void AddClasses( string classname )
	{
		if ( string.IsNullOrWhiteSpace( classname ) )
			return;

		foreach ( var cname in classname.Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries ) )
		{
			AddClass( cname );
		}
	}

	/// <summary>Removes given CSS class from this panel.</summary>
	public void RemoveClass( string classname )
	{
		if ( _class == null ) return;
		if ( string.IsNullOrWhiteSpace( classname ) ) return;

		if ( classname.Contains( ' ' ) )
		{
			foreach ( var cname in classname.Split( new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries ) )
			{
				RemoveClass( cname );
			}
			return;
		}

		//The same the other way: a name that is not in there is the common case, and the set can say so without a lowered copy of it.
		if ( !_class.Contains( classname ) ) return;

		if ( _class.Remove( classname ) )
		{
			//Removing a class changes the selector so children may have rules that now match - need to update those too.
			StyleSelectorsChanged( true, true );
			_classes = null;
		}
	}

	/// <summary>Whether we have the given CSS class or not.</summary>
	public bool HasClass( string classname )
	{
		if ( _class == null ) return false;
		if ( string.IsNullOrWhiteSpace( classname ) ) return false;

		if ( _class.Contains( classname ) ) return true;
		return false;
	}

	//Whether if we have all of these CSS classes.
	internal bool HasClasses( string[] classes )
	{
		if ( _class == null ) return false;

		for ( int i = 0; i < classes.Length; i++ )
		{
			if ( !_class.Contains( classes[i] ) )
				return false;
		}

		return true;
	}

	//Dirty the styles on this panel
	internal void DirtyStylesRecursive()
	{
		StyleSelectorsChanged( true, true );
		Style.InvalidateBroadphase();
	}

	/// <summary>This panel and everything under it take their styles again - a sheet they wear was rewritten, and the rules they last gathered are the old ones.</summary>
	public void Restyle() => DirtyStylesRecursive();

	//Dirty the styles of this class and its children recursively.
	internal void DirtyStylesWithStyle( Styles withStyles, bool skipTransitions = false )
	{
		if ( Style.ContainsStyle( withStyles ) )
		{
			Style.UnderlyingStyleHasChanged();
			StyleSelectorsChanged( false, false );

			if ( skipTransitions )
				SkipTransitions();
		}

		foreach ( var child in Children )
		{
			child.DirtyStylesWithStyle( withStyles, skipTransitions );
		}
	}

	Dictionary<string, Func<bool>> classBinds;

	/// <summary>Switch the class on or off depending on the value of the bool.</summary>
	public void BindClass( string className, Func<bool> func )
	{
		classBinds ??= new();
		classBinds[className] = func;

		RefreshTicksEveryFrame();
	}

	private void RunClassBinds()
	{
		if ( classBinds is null ) return;

		foreach ( var c in classBinds )
		{
			SetClass( c.Key, c.Value() );
		}
	}
}
