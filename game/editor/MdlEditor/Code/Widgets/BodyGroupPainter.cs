using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Body group as a mesh-by-choice table; click moves a mesh to a choice, Ctrl+click adds it
[StyleSheet( "BodyGroupPainter.cs.scss" )]
sealed class BodyGroupPainter : Panel
{
	readonly ModelDefinition.BodyGroup _group;
	readonly List<string> _meshes;
	readonly Action _changed;
	readonly Action _rebuilt;

	readonly Panel _rows;
	readonly UILabel _summary;
	readonly BodyChoiceHeader _header;
	readonly List<BodyMeshRow> _rowPanels = new();

	string _search = "";
	int _highlight = -1;

	/// <summary>The choice being edited on its own, drawn brighter than the rest so the column somebody selected in the tree can be picked out of the table.</summary>
	public int Highlight
	{
		get => _highlight;
		set
		{
			if ( _highlight == value ) return;

			_highlight = value;
			Rebuild();
		}
	}

	internal IReadOnlyList<ModelDefinition.BodyChoice> Choices
		=> _group.Choices ?? new List<ModelDefinition.BodyChoice>();

	//rebuilt: for edits that add or remove a row, since the rows are panels.
	public BodyGroupPainter( ModelDefinition.BodyGroup group, List<string> meshes,
		Action changed, Action rebuilt = null )
	{
		_group = group;
		_group.Choices ??= new List<ModelDefinition.BodyChoice>();
		_meshes = meshes ?? new List<string>();
		_changed = changed;
		_rebuilt = rebuilt;

		AddClass( "body-group" );

		var bar = AddChild( new PanelToolbar() );

		bar.AddFilter( "Search meshes..", text =>
		{
			_search = text ?? "";
			Rebuild();
		} );

		AddTool( bar, "call_split",
			"One mesh per choice, in order - a starting point for a group whose options are one mesh each.", SplitEvenly );
		AddTool( bar, "backspace", "Take every mesh out of every choice of this group.", Clear );

		_header = AddChild( new BodyChoiceHeader( this ) );

		var well = AddChild<Panel>( "well" );
		_rows = well.AddChild<Panel>( "rows" );

		_summary = AddChild<UILabel>( "summary" );

		Rebuild();
	}

	void AddTool( PanelToolbar bar, string icon, string tip, Action run )
	{
		bar.AddIcon( icon, tip, () =>
		{
			run();

			if ( _rebuilt is not null )
			{
				_rebuilt();
				return;
			}

			Changed();
			Rebuild();
		} );
	}

	//-- what the table says ---------------------------------------------------

	internal bool Draws( int choice, string mesh )
	{
		var choices = Choices;
		if ( choice < 0 || choice >= choices.Count ) return false;

		return choices[choice]?.Meshes?.Contains( mesh, StringComparer.OrdinalIgnoreCase ) == true;
	}

	//How many choices of this group draw a mesh - two is a mistake worth showing.
	internal int Claims( string mesh )
	{
		var many = 0;

		for ( var i = 0; i < Choices.Count; i++ )
		{
			if ( Draws( i, mesh ) ) many++;
		}

		return many;
	}

	//Put a mesh in one choice and, unless somebody asked otherwise, in no other choice of the group.
	internal void Toggle( int choice, string mesh, bool keepOthers )
	{
		var choices = Choices;
		if ( choice < 0 || choice >= choices.Count || string.IsNullOrWhiteSpace( mesh ) ) return;

		var had = Draws( choice, mesh );

		if ( !keepOthers )
		{
			foreach ( var other in choices )
				other?.Meshes?.RemoveAll( x => string.Equals( x, mesh, StringComparison.OrdinalIgnoreCase ) );
		}
		else
		{
			choices[choice].Meshes.RemoveAll( x => string.Equals( x, mesh, StringComparison.OrdinalIgnoreCase ) );
		}

		//Clicking the choice a mesh is already in takes it out, which leaves it claimed by nothing and so drawn whatever this group is showing.
		if ( !had ) choices[choice].Meshes.Add( mesh );

		Changed();
	}

	//Take a mesh out of every choice, so nothing in this group hides it.
	internal void Release( string mesh )
	{
		foreach ( var choice in Choices )
			choice?.Meshes?.RemoveAll( x => string.Equals( x, mesh, StringComparison.OrdinalIgnoreCase ) );

		Changed();
	}

	void SplitEvenly()
	{
		var choices = Choices;
		if ( choices.Count == 0 ) return;

		var meshes = VisibleMeshes().ToList();

		foreach ( var choice in choices )
			choice?.Meshes?.Clear();

		for ( var i = 0; i < meshes.Count && i < choices.Count; i++ )
			choices[i].Meshes.Add( meshes[i] );
	}

	void Clear()
	{
		foreach ( var choice in Choices )
			choice?.Meshes?.Clear();
	}

	//The meshes the table is showing, filter and all.
	IEnumerable<string> VisibleMeshes()
	{
		foreach ( var mesh in _meshes )
		{
			if ( _search.Length > 0 && !mesh.Contains( _search, StringComparison.OrdinalIgnoreCase ) ) continue;

			yield return mesh;
		}
	}

	//Names written in a choice that the export has not got.
	IEnumerable<string> Missing()
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var choice in Choices )
		{
			foreach ( var mesh in choice?.Meshes ?? new List<string>() )
			{
				if ( string.IsNullOrWhiteSpace( mesh ) ) continue;
				if ( _meshes.Contains( mesh, StringComparer.OrdinalIgnoreCase ) ) continue;
				if ( !seen.Add( mesh ) ) continue;
				if ( _search.Length > 0 && !mesh.Contains( _search, StringComparison.OrdinalIgnoreCase ) ) continue;

				yield return mesh;
			}
		}
	}

	internal void Rebuild()
	{
		_rows.DeleteChildren();
		_rowPanels.Clear();

		_header?.Read();

		if ( Choices.Count == 0 )
		{
			Note( "This group has no choices yet - add one and the columns show up." );
			UpdateSummary();
			return;
		}

		if ( _meshes.Count == 0 )
		{
			Note( "Nothing to choose from - the definition names no meshes yet." );
			UpdateSummary();
			return;
		}

		foreach ( var mesh in VisibleMeshes() )
			_rowPanels.Add( _rows.AddChild( new BodyMeshRow( this, mesh ) ) );

		//Names written against another export, or a mesh that was renamed. Shown so they can be seen and taken off rather than silently claiming nothing.
		foreach ( var mesh in Missing() )
			_rowPanels.Add( _rows.AddChild( new BodyMeshRow( this, mesh ) { Missing = true } ) );

		UpdateSummary();
	}

	void UpdateSummary()
	{
		if ( !_summary.IsValid() ) return;

		var claimed = _meshes.Count( x => Claims( x ) > 0 );
		var hides = Choices.Count( x => ( x?.Meshes?.Count( m => !string.IsNullOrWhiteSpace( m ) ) ?? 0 ) == 0 );

		_summary.Text = $"{claimed} of {_meshes.Count} meshes in this group  ·  "
			+ $"{_meshes.Count - claimed} always drawn  ·  {hides} choice{( hides == 1 ? "" : "s" )} hiding the part";
	}

	void Note( string text )
	{
		var note = _rows.AddChild<UILabel>( "note" );
		note.Text = text;
	}

	internal void Changed()
	{
		_changed?.Invoke();

		foreach ( var row in _rowPanels )
		{
			if ( row.IsValid() ) row.Read();
		}

		_header?.Read();

		UpdateSummary();
	}
}

//The choice names over the columns, with the default and the hiding ones marked.
sealed class BodyChoiceHeader : Panel
{
	readonly BodyGroupPainter _painter;
	readonly Panel _cells;

	public BodyChoiceHeader( BodyGroupPainter painter )
	{
		_painter = painter;

		AddClass( "choice-header" );

		var name = AddChild<UILabel>( "name" );
		name.Text = "mesh";

		_cells = AddChild<Panel>( "cells" );

		Read();
	}

	internal void Read()
	{
		_cells.DeleteChildren();

		var choices = _painter.Choices;

		for ( var i = 0; i < choices.Count; i++ )
		{
			var choice = choices[i];
			var hides = ( choice?.Meshes?.Count( x => !string.IsNullOrWhiteSpace( x ) ) ?? 0 ) == 0;
			var lit = i == _painter.Highlight;

			var cell = _cells.AddChild<UILabel>( "cell" );

			cell.Text = string.IsNullOrWhiteSpace( choice?.Name ) ? $"#{i}" : choice.Name;
			cell.SetClass( "lit", lit );
			cell.SetClass( "hides", hides );
			cell.SetClass( "first", i == 0 );
		}
	}
}

//One mesh of the export, and which choices of the group draw it.
sealed class BodyMeshRow : Panel
{
	readonly BodyGroupPainter _painter;
	readonly string _mesh;

	readonly UILabel _icon;
	readonly UILabel _name;
	readonly Panel _cells;

	public bool Missing { get; init; }

	public BodyMeshRow( BodyGroupPainter painter, string mesh )
	{
		_painter = painter;
		_mesh = mesh;

		AddClass( "mesh-row" );

		Tooltip = "Click a cell to put this mesh in that choice. Ctrl to leave it in the others too.";

		_icon = AddChild<UILabel>( "material-icon mark" );
		_name = AddChild<UILabel>( "name" );
		_cells = AddChild<Panel>( "cells" );

		Read();
	}

	internal void Read()
	{
		var choices = _painter.Choices;
		var claims = _painter.Claims( _mesh );

		//Red for a name the export has not got, plain for a mesh nothing in this group
		//claims - which is a mesh that is simply always drawn, and so not a problem.
		SetClass( "missing", Missing );
		SetClass( "claimed", claims > 0 );

		_icon.Text = Missing ? "error_outline" : "view_in_ar";
		_name.Text = _mesh;

		_cells.DeleteChildren();

		for ( var i = 0; i < choices.Count; i++ )
		{
			var at = i;

			var cell = _cells.AddChild<Panel>( "cell" );

			cell.SetClass( "lit", i == _painter.Highlight );
			cell.SetClass( "on", _painter.Draws( i, _mesh ) );

			//Amber where the same mesh is in more than one choice of the group: it is then drawn whichever is showing, so the group cannot swap it.
			cell.SetClass( "clash", claims > 1 );

			cell.AddEventListener( "onclick", () =>
				_painter.Toggle( at, _mesh, Application.IsKeyDown( KeyCode.Control ) ) );
		}
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton != MouseButtons.Right ) return;

		OpenMenu();
		e.StopPropagation();
	}

	void OpenMenu()
	{
		var menu = new Menu();

		menu.AddOption( "Always Drawn", "block", () =>
		{
			_painter.Release( _mesh );
			_painter.Rebuild();
		} ).ToolTip = "Take this mesh out of every choice, so this group never hides it.";

		for ( var i = 0; i < _painter.Choices.Count; i++ )
		{
			var at = i;
			var choice = _painter.Choices[i];
			var name = string.IsNullOrWhiteSpace( choice?.Name ) ? $"#{i}" : choice.Name;

			var option = menu.AddOption( name, "check_box_outline_blank", () => _painter.Toggle( at, _mesh, false ) );
			option.Checkable = true;
			option.Checked = _painter.Draws( at, _mesh );
		}

		menu.OpenAtCursor();
	}
}
