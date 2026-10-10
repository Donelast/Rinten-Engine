using Rinten.Engine;

namespace Rinten.UI;

public class StyleSheet
{
	public static List<StyleSheet> Loaded { get; internal set; } = new List<StyleSheet>();

	//Between sessions we clear the stylesheets, so one gamemode can't accidentally use cached values from another.
	internal static void ResetStyleSheets()
	{
		//Only reset sheets belonging to the current context, so we don't stomp another context (eg the menu)
		var context = GlobalContext.Current;

		for ( int i = Loaded.Count - 1; i >= 0; i-- )
		{
			var sheet = Loaded[i];
			if ( sheet == null || !ReferenceEquals( sheet.Context, context ) )
				continue;

			sheet.Release();
			Loaded.RemoveAt( i );
		}
	}

	public List<StyleBlock> Nodes { get; set; } = new List<StyleBlock>();

	//Rules bucketed by the class their subject needs, so a panel only tests rules for its own classes instead of every rule. Rules without
	//a class subject (element/id/*) go in _other. Built once when parsing finishes (main thread); only read during the threaded style build.
	Dictionary<string, List<StyleBlock>> _byClass;
	List<StyleBlock> _other;

	public string FileName { get; internal set; }
	internal FileWatch Watcher { get; private set; }
	public List<string> IncludedFiles { get; set; } = new List<string>();
	public Dictionary<string, string> Variables;
	public Dictionary<string, KeyFrames> KeyFrames = new Dictionary<string, KeyFrames>( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, MixinDefinition> Mixins = new Dictionary<string, MixinDefinition>( StringComparer.OrdinalIgnoreCase );

	internal GlobalContext Context { get; private set; }

	/// <summary>Releases the filesystem watcher so we won't get file changed events.</summary>
	public void Release()
	{
		Watcher?.Dispose();
		Watcher = null;
	}

	public static StyleSheet FromFile( string filename, IEnumerable<(string key, string value)> variables = null, bool failSilently = false )
	{
		filename = BaseFileSystem.NormalizeFilename( filename );
		var context = GlobalContext.Current;

		var alreadyLoaded = Loaded.FirstOrDefault( x => x.FileName == filename && ReferenceEquals( x.Context, context ) );
		if ( alreadyLoaded != null )
			return alreadyLoaded;

		var sheet = new StyleSheet();
		sheet.Context = context;
		sheet.UpdateFromFile( filename, failSilently, context, variables );

		sheet.AddVariables( variables );
		sheet.FileName = filename;
		sheet.AddWatcher( filename );

		Loaded.Add( sheet );

		return sheet;
	}

	internal void AddFilename( string filename )
	{
		IncludedFiles.Add( filename );
		Watcher?.AddFile( filename );
	}

	//lineOffset is what to add to every line the sheet reports, for text taken out of a larger file - see StyleParser.ParseSheet.
	public static StyleSheet FromString( string styles, string filename = "none", IEnumerable<(string key, string value)> variables = null, int lineOffset = 0 )
	{
		try
		{
			return StyleParser.ParseSheet( styles, filename, variables, recover: true, lineOffset: lineOffset );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error parsing stylesheet: {e.Message}\n{e.StackTrace}" );
			return new StyleSheet();
		}
	}

	/// <summary>Replaces text of every loaded copy of this sheet in all contexts and restyles their panels</summary>
	public static void ReplaceFile( string path, string text )
	{
		if ( string.IsNullOrEmpty( path ) || text is null ) return;

		foreach ( var sheet in Loaded.ToArray() )
		{
			if ( sheet is null || !IsSameFile( sheet, path ) ) continue;

			sheet.ReplaceWith( text );
		}

		DirtyEveryUi();
		FileWatch.NotifyDiskWrite( path );
	}

	/// <summary>Parse this text in place of what the sheet last read, and dirty every panel wearing it.</summary>
	public bool ReplaceWith( string text )
	{
		text = EditorTokens.PrepareSheetText( text, FileName );

		if ( !UpdateFromString( text, FileName ?? "none", true ) ) return false;

		Context?.UISystem?.DirtyAllStyles();
		return true;
	}

	static void DirtyEveryUi()
	{
		var seen = new HashSet<UISystem>();

		foreach ( var sheet in Loaded )
		{
			var ui = sheet?.Context?.UISystem;

			if ( ui is not null && seen.Add( ui ) )
				ui.DirtyAllStyles();
		}

		var current = GlobalContext.Current?.UISystem;

		if ( current is not null && seen.Add( current ) )
			current.DirtyAllStyles();
	}

	static bool IsSameFile( StyleSheet sheet, string path )
	{
		if ( sheet?.Context?.FileMount is { } mount )
		{
			var full = mount.GetFullPath( sheet.FileName );

			if ( !string.IsNullOrEmpty( full ) && PathsEqual( full, path ) )
				return true;
		}

		return IsSameFile( sheet?.FileName, path );
	}

	static bool IsSameFile( string sheetName, string path )
	{
		if ( string.IsNullOrEmpty( sheetName ) || string.IsNullOrEmpty( path ) ) return false;

		sheetName = sheetName.Replace( '\\', '/' ).TrimStart( '/' );
		path = path.Replace( '\\', '/' );

		if ( path.EndsWith( sheetName, StringComparison.OrdinalIgnoreCase ) )
		{
			var before = path.Length - sheetName.Length;
			return before == 0 || path[before - 1] == '/';
		}

		return string.Equals( System.IO.Path.GetFileName( sheetName ), System.IO.Path.GetFileName( path ), StringComparison.OrdinalIgnoreCase );
	}

	static bool PathsEqual( string a, string b )
	{
		try
		{
			return string.Equals( System.IO.Path.GetFullPath( a ), System.IO.Path.GetFullPath( b ), StringComparison.OrdinalIgnoreCase );
		}
		catch ( Exception )
		{
			return string.Equals( a, b, StringComparison.OrdinalIgnoreCase );
		}
	}

	internal bool UpdateFromFile( string name, bool failSilently = false, GlobalContext ctx = null, IEnumerable<(string key, string value)> parseVariables = null )
	{
		ctx ??= GlobalContext.Current;

		if ( ctx.FileMount is null )
		{
			return false;
		}

		if ( failSilently && !ctx.FileMount.FileExists( name ) )
		{
			Nodes = new();
			return true;
		}

		try
		{
			var text = ctx.FileMount.ReadAllText( name );
			if ( text is null ) throw new System.IO.FileNotFoundException( "File not found", name );

			var prepared = EditorTokens.PrepareSheetText( text, name );

			using ( new GlobalContext.GlobalContextScope( ctx ) )
			{
				var ok = UpdateFromString( prepared, name, failSilently, parseVariables );

				//The tokens and theme went in front of the file, so every line the parser counted is that many further down than it is on the disk.
				//Taken back off, so a block says where it is in the file somebody would open - the style picker, an error, a jump to the rule.
				var added = CountLines( prepared ) - CountLines( text );

				if ( ok && added > 0 )
				{
					foreach ( var block in Nodes )
					{
						if ( block is null || block.FileName != name ) continue;

						//One from the preamble is in no line of this file.
						block.FileLine = block.FileLine > added ? block.FileLine - added : 0;
					}
				}

				return ok;
			}
		}
		catch ( Exception e )
		{
			if ( !failSilently )
			{
				Log.Warning( e, $"Error opening stylesheet: {name} ({e.Message})" );
			}

			Nodes = new();
		}

		return false;
	}

	static int CountLines( string text )
	{
		var count = 0;

		foreach ( var c in text )
		{
			if ( c == '\n' ) count++;
		}

		return count;
	}

	internal bool UpdateFromString( string text, string filename = "none", bool failSilently = false, IEnumerable<(string key, string value)> parseVariables = null )
	{
		try
		{
			//Keep any variables that were injected from outside (eg inherited from a parent) so a reparse doesn't lose them
			var injected = Variables?.Select( x => (x.Key, x.Value) ).ToArray();

			var variables = parseVariables;

			if ( injected is not null )
			{
				variables = variables is null
					? injected
					: variables.Concat( injected );
			}

			var sheet = FromString( text, filename, variables );

			Nodes = sheet.Nodes;
			Variables = sheet.Variables;
			KeyFrames = sheet.KeyFrames;
			Mixins = sheet.Mixins;
			_byClass = sheet._byClass;
			_other = sheet._other;

			//Don't overwrite the included files if the stylesheet failed to load, because it won't be able to hotload
			if ( sheet.IncludedFiles.Any() )
			{
				IncludedFiles = sheet.IncludedFiles;
			}

			sheet.Release();

			return true;
		}
		catch ( Exception e )
		{
			if ( !failSilently )
			{
				Log.Warning( e, $"Error opening stylesheet: {filename} ({e.Message})" );
			}

			Nodes = new();
		}

		return false;
	}

	void AddWatcher( string name )
	{
		Watcher?.Dispose();
		Watcher = null;

		if ( GlobalContext.Current.FileMount is null )
			return;

		//Store the current context to pass through to the watcher because we might be in a different scope later, and won't be able to find the files
		var context = Context ?? GlobalContext.Current;

		Watcher = context.FileMount.Watch();
		Watcher.OnChanges += x =>
		{
			UpdateFromFile( name, true, context );

			//Watch any files that got @import'd during this reparse so editing them hotloads too
			foreach ( var file in IncludedFiles )
				Watcher?.AddFile( file );

			context.UISystem.DirtyAllStyles();
		};

		foreach ( var file in IncludedFiles )
		{
			Watcher.AddFile( file );
		}
	}

	internal void SetVariable( string key, string value, bool isdefault = false )
	{
		Variables ??= new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		if ( isdefault && Variables.ContainsKey( key ) ) return;

		//If it's another variable, straight swap it
		value = ReplaceVariables( value );

		Variables[key] = value;
	}

	public string GetVariable( string name, string defaultValue = default )
	{
		if ( Variables == null ) return defaultValue;
		if ( Variables.TryGetValue( name, out var val ) ) return val;
		return defaultValue;
	}

	public string ReplaceVariables( string str )
	{
		if ( !str.Contains( '$' ) ) return str; //fast exit

		//Match whole $variable tokens only, so $col doesn't stomp $color and a literal $ (eg $5.00) is left alone
		return System.Text.RegularExpressions.Regex.Replace( str, @"\$[A-Za-z_][A-Za-z0-9_-]*", m =>
		{
			if ( Variables != null && Variables.TryGetValue( m.Value, out var value ) )
				return value;

			throw new Exception( $"Unknown variable '{m.Value}'" );
		} );
	}

	internal void AddVariables( IEnumerable<(string key, string value)> variables )
	{
		if ( variables == null ) return;

		foreach ( var var in variables )
		{
			SetVariable( var.key, var.value );
		}
	}

	public void AddKeyFrames( KeyFrames frames )
	{
		KeyFrames[frames.Name] = frames;
	}

	/// <summary>Register a mixin definition.</summary>
	public void SetMixin( MixinDefinition mixin )
	{
		Mixins[mixin.Name] = mixin;
	}

	/// <summary>Try to get a mixin by name.</summary>
	public bool TryGetMixin( string name, out MixinDefinition mixin )
	{
		return Mixins.TryGetValue( name, out mixin );
	}

	/// <summary>Get a mixin by name or null if not found.</summary>
	public MixinDefinition GetMixin( string name )
	{
		Mixins.TryGetValue( name, out var mixin );
		return mixin;
	}

	//Build the class index from Nodes. Call after Nodes is finalised (parse/hotload).
	internal void BuildIndex()
	{
		_byClass = new Dictionary<string, List<StyleBlock>>( StringComparer.OrdinalIgnoreCase );
		_other = new List<StyleBlock>();

		foreach ( var block in Nodes )
		{
			if ( block.Selectors == null )
				continue;

			foreach ( var sel in block.Selectors )
			{
				//Bucket by the subject's first class. A panel must have that class to match, so
				//this is a safe narrowing - rules without a class subject always get tested.
				if ( sel.Classes != null && sel.Classes.Length > 0 )
				{
					var key = sel.Classes[0];
					if ( !_byClass.TryGetValue( key, out var list ) )
						_byClass[key] = list = new List<StyleBlock>();

					list.Add( block );
				}
				else
				{
					_other.Add( block );
				}
			}
		}
	}

	//Add the rules that could match a target with these classes to output. The exact
	//TestBroadphase still runs, so the result is the same as testing every rule - just cheaper.
	internal void GatherCandidates( HashSet<string> classes, IStyleTarget target, HashSet<StyleBlock> seen, List<StyleBlock> output )
	{
		if ( _byClass == null )
		{
			Take( Nodes, target, seen, output ); //not indexed - test everything
			return;
		}

		Take( _other, target, seen, output );

		if ( classes == null )
			return;

		foreach ( var c in classes )
		{
			if ( _byClass.TryGetValue( c, out var list ) )
				Take( list, target, seen, output );
		}
	}

	static void Take( List<StyleBlock> blocks, IStyleTarget target, HashSet<StyleBlock> seen, List<StyleBlock> output )
	{
		foreach ( var block in blocks )
		{
			if ( seen.Add( block ) && block.TestBroadphase( target ) )
				output.Add( block );
		}
	}
}
