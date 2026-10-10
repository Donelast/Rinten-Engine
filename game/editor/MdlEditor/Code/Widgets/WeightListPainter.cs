using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//Bone mask editor: one row per bone with its final weight and a bar to set it
[StyleSheet( "WeightListPainter.cs.scss" )]
sealed class WeightListPainter : Panel
{
	readonly Model _model;
	readonly ModelDefinition.WeightList _list;
	readonly Action _changed;
	readonly Action _rebuilt;

	readonly Panel _rows;
	readonly UILabel _summary;
	readonly List<WeightBoneRow> _rowPanels = new();

	string _search = "";

	//rebuilt: for edits that change more than one weight, since the base weight is bound above this.
	public WeightListPainter( Model model, ModelDefinition.WeightList list,
		Action changed, Action rebuilt = null )
	{
		_model = model;
		_list = list;
		_changed = changed;
		_rebuilt = rebuilt;

		AddClass( "weight-list" );

		var bar = AddChild( new PanelToolbar() );

		bar.AddFilter( "Search bones..", text =>
		{
			_search = text ?? "";
			Rebuild();
		} );

		AddTool( bar, "select_all", "Every bone at full weight, in one entry on the root.", () => FillFrom( 1.0f ) );
		AddTool( bar, "block", "Every bone at nothing, which is where a mask usually starts.", () => FillFrom( 0.0f ) );
		AddTool( bar, "flip", "Swap what is in for what is out.", Invert );
		AddTool( bar, "backspace", "Take every entry off, leaving the base weight.", Clear );

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

	/// <summary>The weight a bone ends up at, for every bone in the skeleton.</summary>
	public static Dictionary<string, float> Resolve( Model model, ModelDefinition.WeightList list )
	{
		var found = new Dictionary<string, float>( StringComparer.OrdinalIgnoreCase );

		var bones = model?.Bones?.AllBones;
		if ( bones is null || list is null ) return found;

		foreach ( var bone in bones )
			found[bone.Name] = list.BaseWeight;

		//Parents first, so a child that sets its own weight is written after the parent whose entry reaches down over it.
		foreach ( var entry in list.Bones.OrderBy( x => Depth( model, x.Bone ) ) )
		{
			var bone = model.Bones?.GetBone( entry.Bone ?? "" );
			if ( bone is null ) continue;

			Apply( found, bone, entry.Weight, entry.Descendants );
		}

		return found;
	}

	static void Apply( Dictionary<string, float> found, BoneCollection.Bone bone, float weight, bool descendants )
	{
		found[bone.Name] = weight;

		if ( !descendants ) return;

		foreach ( var child in bone.Children ?? [] )
			Apply( found, child, weight, true );
	}

	static int Depth( Model model, string name )
	{
		var bone = model?.Bones?.GetBone( name ?? "" );
		if ( bone is null ) return int.MaxValue;

		var depth = 0;
		for ( var parent = bone.Parent; parent is not null; parent = parent.Parent )
			depth++;

		return depth;
	}

	internal void Rebuild()
	{
		_rows.DeleteChildren();
		_rowPanels.Clear();

		var bones = _model?.Bones?.AllBones;

		if ( bones is null || bones.Count == 0 )
		{
			var note = _rows.AddChild<UILabel>( "note" );
			note.Text = "No skeleton here yet - compile the model and the bones show up.";

			UpdateSummary();
			return;
		}

		var resolved = Resolve( _model, _list );

		foreach ( var bone in bones )
		{
			if ( _search.Length > 0 && !bone.Name.Contains( _search, StringComparison.OrdinalIgnoreCase ) )
				continue;

			var depth = 0;
			for ( var parent = bone.Parent; parent is not null; parent = parent.Parent )
				depth++;

			var row = _rows.AddChild( new WeightBoneRow( this, bone.Name, _search.Length > 0 ? 0 : depth,
				resolved.TryGetValue( bone.Name, out var weight ) ? weight : _list.BaseWeight ) );

			_rowPanels.Add( row );
		}

		//Entries for bones this skeleton has not got - a list written against another body of the same family,
		//or a bone that was renamed. Shown so they can be seen and taken off rather than silently doing nothing.
		foreach ( var entry in _list.Bones.Where( x => _model?.Bones?.GetBone( x.Bone ?? "" ) is null ).ToList() )
		{
			if ( _search.Length > 0 && !(entry.Bone ?? "").Contains( _search, StringComparison.OrdinalIgnoreCase ) )
				continue;

			var row = _rows.AddChild( new WeightBoneRow( this, entry.Bone ?? "", 0, entry.Weight ) { Missing = true } );
			_rowPanels.Add( row );
		}

		UpdateSummary();
	}

	void UpdateSummary()
	{
		if ( !_summary.IsValid() ) return;

		var resolved = Resolve( _model, _list );
		var through = resolved.Count( x => x.Value > 0.001f );

		_summary.Text = _model?.Bones?.AllBones is { Count: > 0 } bones
			? $"{_list.Bones.Count} written  ·  {through} of {bones.Count} bones let through"
			: $"{_list.Bones.Count} written";
	}

	internal ModelDefinition.WeightList.BoneWeight Entry( string bone )
		=> _list.Bones.FirstOrDefault( x => string.Equals( x.Bone, bone, StringComparison.OrdinalIgnoreCase ) );

	//Writes a bone's weight down, adding the entry where there is not one yet.
	internal void SetWeight( string bone, float weight )
	{
		if ( string.IsNullOrWhiteSpace( bone ) ) return;

		weight = Math.Clamp( weight, 0.0f, 1.0f );

		var entry = Entry( bone );

		if ( entry is null )
		{
			_list.Bones.Add( new ModelDefinition.WeightList.BoneWeight { Bone = bone, Weight = weight } );
		}
		else
		{
			entry.Weight = weight;
		}

		Changed();
	}

	internal void Remove( string bone )
	{
		var entry = Entry( bone );
		if ( entry is null ) return;

		_list.Bones.Remove( entry );
		Changed();
	}

	internal void ToggleDescendants( string bone )
	{
		var entry = Entry( bone );
		if ( entry is null ) return;

		entry.Descendants = !entry.Descendants;
		Changed();
	}

	//Whether a bone's entry reaches down over its children. Named apart from Panel.Descendants, which is the panels under one.
	internal bool ReachesChildren( string bone ) => Entry( bone )?.Descendants ?? true;

	//One entry on each root bone, which is the whole skeleton in as few lines as it takes.
	void FillFrom( float weight )
	{
		_list.Bones.Clear();
		_list.BaseWeight = weight;
	}

	void Invert()
	{
		_list.BaseWeight = 1.0f - _list.BaseWeight;

		foreach ( var entry in _list.Bones )
			entry.Weight = 1.0f - entry.Weight;
	}

	void Clear() => _list.Bones.Clear();

	internal void Changed()
	{
		_changed?.Invoke();

		foreach ( var row in _rowPanels )
		{
			if ( !row.IsValid() ) continue;
			row.Read();
		}

		UpdateSummary();
	}

	internal float WeightOf( string bone )
	{
		if ( Resolve( _model, _list ).TryGetValue( bone, out var weight ) ) return weight;

		//A bone this skeleton has not got, which only its own entry says anything about.
		return Entry( bone )?.Weight ?? _list.BaseWeight;
	}
}

//One bone of the skeleton, with the weight it ends up at as a bar that can be dragged.
sealed class WeightBoneRow : Panel
{
	readonly WeightListPainter _painter;
	readonly string _bone;

	readonly UILabel _icon;
	readonly UILabel _name;
	readonly UILabel _descend;
	readonly Panel _bar;
	readonly Panel _fill;
	readonly UILabel _value;

	float _weight;
	bool _dragging;

	public bool Missing { get; init; }

	public WeightBoneRow( WeightListPainter painter, string bone, int depth, float weight )
	{
		_painter = painter;
		_bone = bone;
		_weight = weight;

		AddClass( "weight-row" );

		Tooltip = "Drag to set the weight. Right click for the rest.";

		//The indent is per row rather than per class - how deep a bone sits is the skeleton's business, not the stylesheet's.
		Style.PaddingLeft = 4.0f + depth * 11.0f;

		_icon = AddChild<UILabel>( "material-icon mark" );
		_name = AddChild<UILabel>( "name" );

		_descend = AddChild<UILabel>( "material-icon descend hidden" );
		_descend.Text = "arrow_downward";

		_bar = AddChild<Panel>( "bar" );
		_fill = _bar.AddChild<Panel>( "fill" );
		_value = _bar.AddChild<UILabel>( "value" );

		Read();
	}

	bool Written => _painter.Entry( _bone ) is not null;

	//Reads the weight and everything that follows from it.
	internal void Read()
	{
		_weight = _painter.WeightOf( _bone );

		SetClass( "missing", Missing );
		SetClass( "written", Written );

		_icon.Text = Missing ? "error_outline" : "share";
		_name.Text = _bone;
		_value.Text = _weight.ToString( "0.##" );

		_fill.Style.Width = Length.Fraction( _weight );

		//Green at full, red at nothing - the same reading as the bones in the viewport, so the two
		//halves of the editor agree. Per weight, which is the one thing a stylesheet cannot say.
		_fill.Style.BackgroundColor = Theme.Red.LerpTo( Theme.Green, _weight ).WithAlpha( Written ? 0.95f : 0.45f );
		_fill.SetClass( "hidden", _weight <= 0.001f );

		_descend.SetClass( "hidden", !Written || !_painter.ReachesChildren( _bone ) );
	}

	protected override void OnMouseDown( MousePanelEvent e )
	{
		base.OnMouseDown( e );

		if ( e.MouseButton == MouseButtons.Right )
		{
			OpenMenu();
			e.StopPropagation();
			return;
		}

		if ( e.MouseButton != MouseButtons.Left ) return;

		if ( !IsOnBar( e.Target as Panel ) )
		{
			//The name half is a switch: in or out, which is what most of the editing of a mask actually is.
			_painter.SetWeight( _bone, _weight > 0.5f ? 0.0f : 1.0f );
			e.StopPropagation();
			return;
		}

		_dragging = true;
		Drag();

		e.StopPropagation();
	}

	protected override void OnMouseUp( MousePanelEvent e )
	{
		base.OnMouseUp( e );

		_dragging = false;
	}

	public override void Tick()
	{
		base.Tick();

		if ( !_dragging ) return;

		if ( ( Application.MouseButtons & MouseButtons.Left ) == 0 )
		{
			_dragging = false;
			return;
		}

		Drag();
	}

	//Where the pointer is along the bar, as a fraction of it. Read off the bar's own box rather than off
	//the row's width and a constant. The bar is a panel now, so where it is is a thing that can be asked.
	void Drag()
	{
		if ( !_bar.IsValid() ) return;

		var box = _bar.Box.Rect;
		if ( box.Width <= 0.0f ) return;

		var along = Math.Clamp( ( Application.CursorPosition.x - box.Left * _bar.ScaleFromScreen )
			/ ( box.Width * _bar.ScaleFromScreen ), 0.0f, 1.0f );

		//Snapped to twentieths unless somebody asks for the exact value, since what a mask wants is nearly always a round number.
		if ( !Application.IsKeyDown( KeyCode.Control ) )
			along = MathF.Round( along * 20.0f ) / 20.0f;

		_painter.SetWeight( _bone, along );
	}

	bool IsOnBar( Panel target )
	{
		for ( var p = target; p is not null; p = p.Parent )
		{
			if ( p == _bar ) return true;
			if ( p == this ) return false;
		}

		return false;
	}

	void OpenMenu()
	{
		var menu = new Menu();

		menu.AddOption( "Full", "check_circle", () => _painter.SetWeight( _bone, 1.0f ) );
		menu.AddOption( "Half", "adjust", () => _painter.SetWeight( _bone, 0.5f ) );
		menu.AddOption( "None", "block", () => _painter.SetWeight( _bone, 0.0f ) );

		if ( Written )
		{
			menu.AddSeparator();

			var option = menu.AddOption( "Affects Children", "account_tree", () => _painter.ToggleDescendants( _bone ) );
			option.Checkable = true;
			option.Checked = _painter.ReachesChildren( _bone );

			menu.AddOption( "Remove Entry", "backspace", () =>
			{
				_painter.Remove( _bone );
				_painter.Rebuild();
			} );
		}

		menu.OpenAtCursor();
	}
}
