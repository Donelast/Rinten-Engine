namespace Editor.MeshEditor;

//Named material palettes, each with a kept row of three and a recent row of six
//A material sits in only one row; kept ones are never recorded as recent
internal static class MaterialShelf
{
	/// <summary>How many are kept, which is the short row.</summary>
	public const int KeptSlots = 3;

	/// <summary>How many are remembered, which is the long one under it.</summary>
	public const int RecentSlots = 6;

	const string FavouritesCookie = "MeshEditor.Materials.Favourites";
	const string RecentCookie = "MeshEditor.Materials.Recent";
	const string PalettesCookie = "MeshEditor.Materials.Palettes";
	const string ActivePaletteCookie = "MeshEditor.Materials.ActivePalette";

	/// <summary>The palette that is always there, and the one an older project's shelf is in.</summary>
	public const string DefaultPalette = "Default";

	static readonly List<string> _palettes = [];
	static string _active = DefaultPalette;

	static readonly MaterialPaint[] _favourites = new MaterialPaint[KeptSlots];
	static readonly List<MaterialPaint> _recent = [];

	static bool _loaded;

	/// <summary>Raised when either row changes, so whatever draws them redraws.</summary>
	public static event Action Changed;

	/// <summary>The palettes there are, in the order they were made.</summary>
	public static IReadOnlyList<string> Palettes
	{
		get { Load(); return _palettes; }
	}

	/// <summary>The palette whose shelf is showing.</summary>
	public static string ActivePalette
	{
		get { Load(); return _active; }
	}

	/// <summary>Shows another palette's shelf.</summary>
	public static void SelectPalette( string name )
	{
		Load();

		if ( !_palettes.Contains( name ) || name == _active ) return;

		_active = name;
		WritePalettes();

		_loaded = false;
		Changed?.Invoke();
	}

	/// <summary>Makes an empty palette and shows it. False where the name is empty or taken.</summary>
	public static bool CreatePalette( string name )
	{
		Load();

		name = name?.Trim();

		if ( string.IsNullOrEmpty( name ) || name.Contains( ';' ) || _palettes.Contains( name ) ) return false;

		_palettes.Add( name );
		_active = name;
		WritePalettes();

		_loaded = false;
		Changed?.Invoke();

		return true;
	}

	/// <summary>Renames a palette, shelf and all. False where the new name is empty or taken.</summary>
	public static bool RenamePalette( string from, string to )
	{
		Load();

		to = to?.Trim();

		var index = _palettes.IndexOf( from );

		if ( index < 0 || string.IsNullOrEmpty( to ) || to.Contains( ';' ) || _palettes.Contains( to ) ) return false;

		Copy( from, to );
		Erase( from );

		_palettes[index] = to;
		if ( _active == from ) _active = to;
		WritePalettes();

		_loaded = false;
		Changed?.Invoke();

		return true;
	}

	/// <summary>Makes a copy of a palette, called "name Copy", and shows it.</summary>
	public static void DuplicatePalette( string name )
	{
		Load();

		if ( !_palettes.Contains( name ) ) return;

		var copy = $"{name} Copy";

		for ( var n = 2; _palettes.Contains( copy ); n++ )
			copy = $"{name} Copy {n}";

		Copy( name, copy );

		_palettes.Add( copy );
		_active = copy;
		WritePalettes();

		_loaded = false;
		Changed?.Invoke();
	}

	/// <summary>Takes a palette away, shelf and all. The last one cannot go.</summary>
	public static void DeletePalette( string name )
	{
		Load();

		var index = _palettes.IndexOf( name );

		if ( index < 0 || _palettes.Count <= 1 ) return;

		Erase( name );

		_palettes.RemoveAt( index );
		if ( _active == name ) _active = _palettes[Math.Clamp( index - 1, 0, _palettes.Count - 1 )];
		WritePalettes();

		_loaded = false;
		Changed?.Invoke();
	}

	//The default palette is on the shelf's own cookies; every other is on those with its name after.
	static string KeptCookie( string palette ) => palette == DefaultPalette ? FavouritesCookie : $"{FavouritesCookie}.{palette}";
	static string RecentCookieOf( string palette ) => palette == DefaultPalette ? RecentCookie : $"{RecentCookie}.{palette}";

	static void Copy( string from, string to )
	{
		try
		{
			ProjectCookie?.Set( KeptCookie( to ), ProjectCookie.Get( KeptCookie( from ), string.Empty ) );
			ProjectCookie?.Set( RecentCookieOf( to ), ProjectCookie.Get( RecentCookieOf( from ), string.Empty ) );
		}
		catch { }
	}

	static void Erase( string palette )
	{
		try
		{
			ProjectCookie?.Set( KeptCookie( palette ), string.Empty );
			ProjectCookie?.Set( RecentCookieOf( palette ), string.Empty );
		}
		catch { }
	}

	static void WritePalettes()
	{
		try
		{
			ProjectCookie?.Set( PalettesCookie, string.Join( ";", _palettes ) );
			ProjectCookie?.Set( ActivePaletteCookie, _active );
		}
		catch { }
	}

	/// <summary>The kept row, left to right. A slot with nothing in it is invalid.</summary>
	public static IReadOnlyList<MaterialPaint> Favourites
	{
		get { Load(); return _favourites; }
	}

	/// <summary>The last used, newest first, and never more than a row of them.</summary>
	public static IReadOnlyList<MaterialPaint> Recent
	{
		get { Load(); return _recent; }
	}

	/// <summary>False while only previewing (e.g. picker highlight), so it doesn't fill the recent row</summary>
	public static bool Recording { get; private set; } = true;

	/// <summary>Stops recording until the returned scope is let go of.</summary>
	public static Scope NotRecording()
	{
		var was = Recording;
		Recording = false;
		return new Scope( was );
	}

	public readonly struct Scope( bool was ) : IDisposable
	{
		public void Dispose() => Recording = was;
	}

	//The colour counts. One material at two tints is two entries, because to somebody painting they are two things to reach for - and the row is what is reached for, not what is owned.
	/// <summary>Somebody painted with this, so it goes to the front of recent.</summary>
	public static void Use( MaterialPaint paint )
	{
		if ( !Recording ) return;
		if ( !paint.IsValid ) return;

		Load();

		//Already kept, so it is already one press away and putting it in the row below would only cost that row a slot.
		if ( IsFavourite( paint ) ) return;

		//Once, at the front. Compared by path rather than by reference: the same material loaded twice
		//is the same material to look at, and two of it in the row is one fewer material in the row.
		if ( Same( _recent.FirstOrDefault(), paint ) ) return;

		_recent.RemoveAll( x => Same( x, paint ) );
		_recent.Insert( 0, paint );

		while ( _recent.Count > RecentSlots )
			_recent.RemoveAt( _recent.Count - 1 );

		Save();
	}

	/// <summary>Whether this is one of the kept ones.</summary>
	public static bool IsFavourite( MaterialPaint paint )
	{
		if ( !paint.IsValid ) return false;

		Load();

		return _favourites.Any( x => Same( x, paint ) );
	}

	/// <summary>Puts a paint in one of the kept slots, or empties it.</summary>
	public static void SetFavourite( int index, MaterialPaint paint )
	{
		if ( index < 0 || index >= KeptSlots ) return;

		Load();

		//Never twice on the shelf: out of whichever slot it was in, and out of recent, because keeping something is what recent was for.
		if ( paint.IsValid )
		{
			for ( var i = 0; i < KeptSlots; i++ )
			{
				if ( Same( _favourites[i], paint ) ) _favourites[i] = default;
			}

			_recent.RemoveAll( x => Same( x, paint ) );
		}

		_favourites[index] = paint;

		Save();
	}

	/// <summary>Keeps a paint in the first slot that is free, or the last.</summary>
	public static void Keep( MaterialPaint paint )
	{
		if ( !paint.IsValid ) return;

		Load();

		if ( IsFavourite( paint ) ) return;

		var free = Array.FindIndex( _favourites, x => !x.IsValid );

		SetFavourite( free < 0 ? KeptSlots - 1 : free, paint );
	}

	/// <summary>Takes a paint off the kept row, wherever it is on it.</summary>
	public static void Drop( MaterialPaint paint )
	{
		if ( !paint.IsValid ) return;

		Load();

		var changed = false;

		for ( var i = 0; i < KeptSlots; i++ )
		{
			if ( !Same( _favourites[i], paint ) ) continue;

			_favourites[i] = default;
			changed = true;
		}

		if ( changed ) Save();
	}

	//The same material at the same colour - see MaterialPaint.Same.
	static bool Same( MaterialPaint a, MaterialPaint b ) => a.Same( b );

	static void Save()
	{
		Write( KeptCookie( _active ), _favourites );
		Write( RecentCookieOf( _active ), _recent );

		Changed?.Invoke();
	}

	static void Write( string cookie, IEnumerable<MaterialPaint> paints )
	{
		var parts = paints.Select( x => x.ToCookie() );

		try { ProjectCookie?.Set( cookie, string.Join( ";", parts ) ); }
		catch { }
	}

	static void Load()
	{
		if ( _loaded ) return;
		_loaded = true;

		LoadPalettes();

		var kept = Read( KeptCookie( _active ) );

		for ( var i = 0; i < KeptSlots; i++ )
			_favourites[i] = i < kept.Count ? kept[i] : default;

		_recent.Clear();

		foreach ( var paint in Read( RecentCookieOf( _active ) ) )
		{
			if ( !paint.IsValid ) continue;
			if ( IsFavouriteLoaded( paint ) ) continue;
			if ( _recent.Any( x => Same( x, paint ) ) ) continue;

			_recent.Add( paint );

			if ( _recent.Count >= RecentSlots ) break;
		}
	}

	static void LoadPalettes()
	{
		string names, active;

		try
		{
			names = ProjectCookie?.Get( PalettesCookie, string.Empty ) ?? string.Empty;
			active = ProjectCookie?.Get( ActivePaletteCookie, DefaultPalette ) ?? DefaultPalette;
		}
		catch
		{
			names = string.Empty;
			active = DefaultPalette;
		}

		_palettes.Clear();
		_palettes.Add( DefaultPalette );

		foreach ( var name in names.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			if ( !_palettes.Contains( name ) ) _palettes.Add( name );
		}

		_active = _palettes.Contains( active ) ? active : DefaultPalette;
	}

	//IsFavourite without the load, for use from inside it.
	static bool IsFavouriteLoaded( MaterialPaint paint )
		=> _favourites.Any( x => Same( x, paint ) );

	static List<MaterialPaint> Read( string cookie )
	{
		string data;
		try { data = ProjectCookie?.Get( cookie, string.Empty ) ?? string.Empty; }
		catch { data = string.Empty; }

		var paints = new List<MaterialPaint>();

		if ( string.IsNullOrEmpty( data ) ) return paints;

		foreach ( var part in data.Split( ';' ) )
		{
			paints.Add( MaterialPaint.FromCookie( part ) );
		}

		return paints;
	}

	/// <summary>Forgets what was read, so the next look reads the project that is open now.</summary>
	[EditorEvent.Hotload]
	public static void Reload()
	{
		_loaded = false;
		Changed?.Invoke();
	}
}

//Active material, independent of any tool; an object so the bar can bind to a property
internal sealed class MaterialSelection
{
	/// <summary>The one there is.</summary>
	public static MaterialSelection Current { get; } = new();

	/// <summary>What the bar binds to.</summary>
	public static SerializedProperty Property
		=> Current.GetSerialized().GetProperty( nameof( Active ) );

	const string Cookie = "MeshTool.ActiveMaterial";
	const string TintCookie = "MeshTool.ActiveTint";

	/// <summary>The engine's own dev grid, which ships in the base addon.</summary>
	public const string Fallback = "materials/dev/grid.mat";

	Material _active;
	Color32 _tint = Color32.White;
	bool _loaded;

	[Property]
	public Material Active
	{
		get
		{
			if ( !_loaded ) Load();
			return _active;
		}
		set
		{
			if ( !_loaded ) Load();
			if ( _active == value ) return;

			_active = value;
			_loaded = true;

			Save();

			//Every way of choosing one comes through here - the picker, a slot on the shelf, a drop, and the eyedropper that takes one off a face.
			MaterialShelf.Use( new MaterialPaint( value, _tint ) );
		}
	}

	/// <summary>The colour whatever is painted is multiplied by.</summary>
	public Color32 Tint
	{
		get
		{
			if ( !_loaded ) Load();
			return _tint;
		}
		set
		{
			if ( !_loaded ) Load();
			if ( _tint.Equals( value ) ) return;

			_tint = value;

			SaveTint();
		}
	}

	/// <summary>What is being painted with, both halves of it.</summary>
	public MaterialPaint Paint
	{
		get => new( Active, Tint );
		set
		{
			//The colour first, so that setting the material records the pair rather than the material against whatever colour was there before.
			Tint = value.Tint;
			Active = value.Material;

			//The material may not have changed, in which case its setter said nothing to the shelf and this pair is still worth recording.
			Chose();
		}
	}

	/// <summary>Says the choice is finished, so the pair belongs in the recent row.</summary>
	public void Chose() => MaterialShelf.Use( Paint );

	void Save()
	{
		if ( !_active.IsValid() ) return;
		if ( string.IsNullOrEmpty( _active.ResourcePath ) ) return;

		try { ProjectCookie?.Set( Cookie, _active.ResourcePath ); }
		catch { }
	}

	void SaveTint()
	{
		try { ProjectCookie?.Set( TintCookie, $"{_tint.r:x2}{_tint.g:x2}{_tint.b:x2}" ); }
		catch { }
	}

	void Load()
	{
		_loaded = true;

		string hex;
		try { hex = ProjectCookie?.Get( TintCookie, string.Empty ) ?? string.Empty; }
		catch { hex = string.Empty; }

		_tint = MaterialPaint.TintFromHex( hex );

		string path;
		try { path = ProjectCookie?.Get( Cookie, string.Empty ) ?? string.Empty; }
		catch { path = string.Empty; }

		if ( !string.IsNullOrEmpty( path ) && Material.Load( path ) is { } saved && saved.IsValid() )
		{
			_active = saved;
			return;
		}

		_active = Material.Load( Fallback );
	}

	/// <summary>Reads the project that is open now rather than the last one.</summary>
	[EditorEvent.Hotload]
	public static void Reload() => Current._loaded = false;
}
