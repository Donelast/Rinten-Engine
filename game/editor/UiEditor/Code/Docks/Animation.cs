using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.UiEditor;

/// <summary>Dock editing the document's @keyframes as a table of stops by properties; rebuilt on hotload</summary>
[SkipHotload]
public sealed class AnimationDock : PanelDockHost
{
	readonly AnimationPanel _panel;

	public AnimationDock( UiEditorWindow window ) : base( null )
	{
		Name = "Animation";
		WindowTitle = "Animation";
		SetWindowIcon( "animation" );
		MinimumHeight = 120;

		_panel = new AnimationPanel( window );

		SetContent( _panel );
	}

	public void SetDocument( RazorDocument document ) => _panel.Rebuild();

	public void NoteSheetWritten() => _panel.NoteSheetWritten();
}

//The animations, and the table of the one chosen.
[StyleSheet( "Animation.cs.scss" )]
internal sealed class AnimationPanel : EditorPanel
{
	readonly UiEditorWindow _window;
	readonly Panel _names;
	readonly Panel _table;

	string _chosen;
	int _refreshIn;

	public AnimationPanel( UiEditorWindow window )
	{
		_window = window;

		AddClass( "ui-animation" );

		var bar = AddChild<PanelToolbar>();

		_names = bar.AddChild<Panel>( "names" );

		bar.AddStretch();

		bar.AddText( "Use", "Put the animation on the selection's rule: animation: name 1s", UseOnSelection );

		var well = AddChild<Panel>( "well" );
		_table = well.AddChild<Panel>( "table" );

		Rebuild();
	}

	public void NoteSheetWritten() => _refreshIn = 6;

	public override void Tick()
	{
		base.Tick();

		if ( _refreshIn > 0 && --_refreshIn == 0 ) Rebuild();
	}

	//What there is

	ScssDocument Sheet => _window.Document is { } document ? ScssDocument.Load( document.ScssPath ) : null;

	//The keyframes blocks of the document's sheet, by name.
	static IEnumerable<(string name, ScssDocument.Block block)> Animations( ScssDocument sheet )
	{
		if ( sheet is null ) yield break;

		foreach ( var block in sheet.Blocks )
		{
			if ( !block.Selector.StartsWith( "@keyframes", StringComparison.OrdinalIgnoreCase ) ) continue;

			var name = block.Selector.Substring( "@keyframes".Length ).Trim();
			if ( name.Length > 0 ) yield return ( name, block );
		}
	}

	public void Rebuild()
	{
		_names.DeleteChildren( true );
		_table.DeleteChildren( true );

		var sheet = Sheet;
		var animations = Animations( sheet ).ToList();

		if ( animations.Count == 0 )
		{
			var note = _table.AddChild<UILabel>( "note" );
			note.Text = _window.Document is null ? "No file open" : "No @keyframes in the sheet yet - write one there, or Use on a rule.";
			_chosen = null;
			return;
		}

		if ( _chosen is null || animations.All( x => x.name != _chosen ) ) _chosen = animations[0].name;

		foreach ( var (name, _) in animations )
		{
			var tab = _names.AddChild<UILabel>( "name" );
			tab.Tokenize = false;
			tab.Text = name;
			tab.SetClass( "active", name == _chosen );
			tab.AddEventListener( "onclick", () => { _chosen = name; Rebuild(); } );
		}

		var chosen = animations.First( x => x.name == _chosen ).block;

		//Stops in order along the animation, from first to last.
		var stops = chosen.Children.OrderBy( x => StopFraction( x.Selector ) ).ToList();

		var header = _table.AddChild<Panel>( "row header" );
		var corner = header.AddChild<UILabel>( "cell name" );
		corner.Tokenize = false;
		corner.Text = "property";

		foreach ( var stop in stops )
		{
			var cell = header.AddChild<UILabel>( "cell stop" );
			cell.Tokenize = false;
			cell.Text = stop.Selector;
		}

		var addStop = header.AddChild<TextField>( "cell new-stop field" );
		addStop.Placeholder = "+ 50%";
		addStop.Submitted = text =>
		{
			var stop = ( text ?? "" ).Trim();
			if ( stop.Length == 0 ) return;
			if ( !stop.EndsWith( '%' ) && stop is not ( "from" or "to" ) ) stop += "%";

			_window.AddKeyframeStop( _chosen, stop );
			addStop.Value = "";
		};

		//Every property any stop sets, in the order first seen.
		var properties = new List<string>();

		foreach ( var stop in stops )
		{
			foreach ( var declaration in stop.Declarations )
			{
				if ( !properties.Contains( declaration.Name, StringComparer.OrdinalIgnoreCase ) ) properties.Add( declaration.Name );
			}
		}

		foreach ( var property in properties )
			_table.AddChild( new TrackRow( this, property, stops ) );

		_table.AddChild( new TrackRow( this, null, stops ) );
	}

	//Where a stop is along the animation: from 0, to 1, a percentage between.
	static float StopFraction( string stop )
	{
		if ( string.Equals( stop, "from", StringComparison.OrdinalIgnoreCase ) ) return 0;
		if ( string.Equals( stop, "to", StringComparison.OrdinalIgnoreCase ) ) return 1;

		return float.TryParse( stop.TrimEnd( '%' ), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v ) ? v / 100.0f : 0;
	}

	internal void Write( string stop, string property, string value ) => _window.WriteKeyframe( _chosen, stop, property, value );

	void UseOnSelection()
	{
		if ( _chosen is null || _window.Selection is not { } panel ) return;

		_window.WriteStyles( panel, new List<(string, string)> { ( "animation", $"{_chosen} 1s ease-in-out" ) } );
	}
}

//One property across the stops: a field per stop, blank where the stop does not set it; a nameless row is a property not yet named.
internal sealed class TrackRow : Panel
{
	public TrackRow( AnimationPanel animation, string property, List<ScssDocument.Block> stops )
	{
		AddClass( "row" );
		SetClass( "new", property is null );

		TextField nameField = null;

		if ( property is null )
		{
			nameField = AddChild<TextField>( "cell name field" );
			nameField.Placeholder = "+ property";
		}
		else
		{
			var name = AddChild<UILabel>( "cell name" );
			name.Tokenize = false;
			name.Text = property;
		}

		foreach ( var stop in stops )
		{
			var declaration = property is null ? null : stop.Declarations.LastOrDefault( x => string.Equals( x.Name, property, StringComparison.OrdinalIgnoreCase ) );
			var shown = declaration?.Value ?? "";

			var field = AddChild<TextField>( "cell value field" );
			field.Value = shown;
			field.Placeholder = "–";

			var stopName = stop.Selector;

			void Commit()
			{
				var value = field.Value?.Trim() ?? "";
				if ( value == shown ) return;

				var into = property ?? nameField?.Value?.Trim();
				if ( string.IsNullOrEmpty( into ) ) return;

				shown = value;
				animation.Write( stopName, into, value );
			}

			field.Submitted = _ => Commit();
			field.AddEventListener( "onblur", Commit );
		}

		//The width of the header's add-a-stop field, so the columns line up.
		AddChild<Panel>( "cell tail" );
	}
}
