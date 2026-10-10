using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.UiEditor;

/// <summary>Inspector dock: rules styling the selected panel, winner first, editable in place</summary>
[SkipHotload]
public sealed class Inspector : PanelDockHost
{
	readonly InspectorPanel _panel;

	public Inspector( UiEditorWindow window ) : base( null )
	{
		Name = "Inspector";
		WindowTitle = "Inspector";
		SetWindowIcon( "tune" );
		MinimumWidth = 260;

		_panel = new InspectorPanel( window );

		SetContent( _panel );
	}

	/// <summary>Show a panel's rules, or nothing.</summary>
	public void Show( Panel panel ) => _panel.Target = panel;

	/// <summary>A sheet was written; the rules are read again once the engine has reloaded it.</summary>
	public void NoteSheetWritten( string path ) => _panel.NoteSheetWritten();
}

//The header, and the rules under it.
[StyleSheet( "Inspector.cs.scss" )]
internal sealed class InspectorPanel : EditorPanel
{
	readonly UiEditorWindow _window;

	/// <summary>The two ways of looking at one cascade.</summary>
	public enum View
	{
		/// <summary>Every rule that hits the panel, as written, winner first.</summary>
		Rules,

		/// <summary>One value per property, grouped by what it is about.</summary>
		Sections,
	}

	const string ViewCookie = "UiEditor.Inspector.View";

	readonly UILabel _title;
	readonly UILabel _source;
	readonly UILabel _rulesTab;
	readonly UILabel _sectionsTab;
	readonly Panel _scroll;
	readonly Panel _rules;

	View _view;

	Panel _target;
	StyleCascade _cascade;

	//Frames left before the rules are read again after a write - the watcher needs a moment.
	int _refreshIn;

	//Since the sheets were last compared with what the rows show.
	RealTimeSince _sinceCompared;

	public Panel Target
	{
		get => _target;
		set
		{
			_target = value;
			Rebuild();
		}
	}

	public InspectorPanel( UiEditorWindow window )
	{
		_window = window;

		AddClass( "ui-inspector" );

		var header = AddChild<Panel>( "header" );

		_title = header.AddChild<UILabel>( "title" );
		_source = header.AddChild<UILabel>( "source" );

		_title.Tokenize = false;
		_source.Tokenize = false;

		_source.AddEventListener( "onclick", () => OpenSource( _target ) );

		_view = EditorCookie.Get( ViewCookie, View.Rules );

		var tabs = AddChild<Panel>( "tabs" );

		_rulesTab = tabs.AddChild<UILabel>( "tab" );
		_rulesTab.Tokenize = false;
		_rulesTab.Text = "Rules";
		_rulesTab.AddEventListener( "onclick", () => SetView( View.Rules ) );

		_sectionsTab = tabs.AddChild<UILabel>( "tab" );
		_sectionsTab.Tokenize = false;
		_sectionsTab.Text = "Properties";
		_sectionsTab.AddEventListener( "onclick", () => SetView( View.Sections ) );

		_scroll = AddChild<Panel>( "scroll" );
		_rules = _scroll.AddChild<Panel>( "rules" );

		Rebuild();
	}

	public void NoteSheetWritten() => _refreshIn = 6;

	void SetView( View view )
	{
		if ( _view == view ) return;

		_view = view;
		EditorCookie.Set( ViewCookie, view );

		Rebuild();
	}

	/// <summary>The rules are read again when the engine has new ones: a written sheet comes back from the watcher as new blocks, and so does a hotload.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( _refreshIn > 0 && --_refreshIn == 0 )
		{
			Rebuild();
			return;
		}

		if ( !_target.IsValid() || _cascade is null ) return;

		//A few times a second rather than every frame: a cascade is a walk over the sheets, and it only has to notice a reload, not race it.
		if ( _sinceCompared < 0.25f ) return;
		_sinceCompared = 0;

		if ( BlocksChanged() ) Rebuild();
	}

	bool BlocksChanged()
	{
		var fresh = StyleCascade.For( _target, _window.Document?.ScssPath );

		if ( fresh.Rules.Count != _cascade.Rules.Count ) return true;

		for ( var i = 0; i < fresh.Rules.Count; i++ )
		{
			if ( !ReferenceEquals( fresh.Rules[i].Block, _cascade.Rules[i].Block ) ) return true;
		}

		return false;
	}

	void Rebuild()
	{
		_rules.DeleteChildren( true );
		_cascade = null;

		if ( !_target.IsValid() )
		{
			_title.Text = "Nothing selected";
			_source.Text = "";
			_source.SetClass( "hidden", true );

			var note = _rules.AddChild<UILabel>( "note" );
			note.Text = "Click a panel on the canvas, or a row in the hierarchy.";

			return;
		}

		_title.Text = Describe( _target );

		var hasSource = !string.IsNullOrEmpty( _target.SourceFile );
		_source.Text = hasSource ? $"{_target.SourceFile}:{_target.SourceLine}" : "";
		_source.SetClass( "hidden", !hasSource );

		_cascade = StyleCascade.For( _target, _window.Document?.ScssPath );

		//Styles the editor can read and not write, said at the top, where it is seen before anything is tried rather than after.
		if ( _window.Document is { HasStyleTag: true } )
			_rules.AddChild( new StyleTagBlock( this ) );

		//The tag itself, before its styles: what it is, what it wears, what it says.
		if ( _window.LocateTag( _target ) is { } tag )
		{
			_rules.AddChild( new TagBlock( this, _target, tag ) );

			//Where it hangs from - only a panel with a tag of its own in this file can be told, since that is where the answer is written.
			if ( !ReferenceEquals( _target, _window.Root ) )
				_rules.AddChild( new AnchorBlock( this, _target, tag ) );
		}

		if ( _window.States is { } states && ReferenceEquals( states.Panel, _target ) )
			_rules.AddChild( new StatesBlock( this, states ) );

		if ( _window.LocateTag( _target ) is { } eventTag )
			_rules.AddChild( new EventsBlock( this, _target, eventTag ) );

		_rulesTab.SetClass( "active", _view == View.Rules );
		_sectionsTab.SetClass( "active", _view == View.Sections );

		if ( _view == View.Rules ) BuildRules();
		else BuildSections();

		//A panel nothing of its own file dresses yet: offer to start it.
		if ( _window.Document is { } document && _cascade.EditTarget( null, document.ScssPath ) is null )
			_rules.AddChild( new AddRuleRow( this, _target ) );
	}

	void BuildRules()
	{
		if ( _cascade.Inline.Count > 0 )
			_rules.AddChild( new RuleBlock( this, null, _cascade.Inline ) );

		//Winner first: the last rule applied is the one that decided.
		for ( var i = _cascade.Rules.Count - 1; i >= 0; i-- )
			_rules.AddChild( new RuleBlock( this, _cascade.Rules[i], _cascade.Rules[i].Properties ) );

		//Then what waits on a state, dimmed, with the state it waits for.
		foreach ( var (rule, state) in _cascade.Inactive )
			_rules.AddChild( new RuleBlock( this, rule, rule.Properties, state ) );
	}

	//One row per property, the winning value in it, under the heading the property belongs to. The properties
	//a section always shows come first, blank when nothing sets them; whatever else a rule set follows.
	void BuildSections()
	{
		var set = new Dictionary<string, (string value, StyleCascade.Rule rule)>( StringComparer.OrdinalIgnoreCase );

		foreach ( var rule in _cascade.Rules )
		{
			foreach ( var property in rule.Properties )
				set[property.Name] = ( property.Value ?? property.OriginalValue, rule );
		}

		foreach ( var property in _cascade.Inline )
			set[property.Name] = ( property.Value ?? property.OriginalValue, null );

		var placed = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var section in StyleSections.All )
		{
			var block = new SectionBlock( section.Title );

			foreach ( var name in section.Always )
			{
				placed.Add( name );
				set.TryGetValue( name, out var found );
				block.AddRow( new PropertyRow( this, name, found.value, found.rule ) );
			}

			//The rest of what is set and belongs here, in the order the rules said it.
			foreach ( var (name, (value, rule)) in set )
			{
				if ( placed.Contains( name ) || !section.Owns( name ) ) continue;

				placed.Add( name );
				block.AddRow( new PropertyRow( this, name, value, rule ) );
			}

			_rules.AddChild( block );
		}

		var other = new SectionBlock( "Other" );
		var any = false;

		foreach ( var (name, (value, rule)) in set )
		{
			if ( placed.Contains( name ) ) continue;

			any = true;
			other.AddRow( new PropertyRow( this, name, value, rule ) );
		}

		if ( any ) _rules.AddChild( other );
		else other.Delete( true );
	}

	//The panel, the way a selector would write it.
	static string Describe( Panel panel )
	{
		var tag = !string.IsNullOrEmpty( panel.ElementName ) && panel.ElementName != "panel"
			? panel.ElementName
			: panel.GetType().Name == "Panel" ? "div" : panel.GetType().Name;

		var text = tag;

		if ( !string.IsNullOrEmpty( panel.Id ) ) text += "#" + panel.Id;

		foreach ( var c in panel.Class ) text += "." + c;

		return text;
	}

	//What the rows ask

	internal StyleCascade Cascade => _cascade;

	//Whether a rule's value for a property is the one in effect.
	internal bool IsWinner( StyleCascade.Rule rule, string property )
	{
		if ( _cascade is null ) return false;
		if ( rule is null ) return true;

		return _cascade.IsWinner( rule, property );
	}

	//Write a value into a rule's file, or take the property out when the value is empty.
	internal void Write( StyleCascade.Rule rule, string property, string value )
	{
		if ( rule is null ) return;

		property = property?.Trim();
		if ( string.IsNullOrEmpty( property ) ) return;

		_window.WriteStyle( rule, property, value );
	}

	//Write a property from the sections view: into the rule its value came from when that is a file
	//of the document's own, else into the rule of the document's own that the inspector would edit.
	internal void WriteProperty( string property, string value, StyleCascade.Rule from )
	{
		var own = _window.Document?.ScssPath;

		if ( _cascade is null || own is null ) return;

		var target = from is not null && from.Path is not null && string.Equals(
				System.IO.Path.GetFullPath( from.Path ), System.IO.Path.GetFullPath( own ), StringComparison.OrdinalIgnoreCase )
			? from
			: _cascade.EditTarget( property, own );

		if ( target is null )
		{
			Log.Warning( $"UI editor: no rule of {_window.Document?.Name}'s own dresses this panel - add one first" );
			return;
		}

		Write( target, property, value );
	}

	//Start a rule for the panel in the document's own sheet.
	internal void AddRule( string selector )
	{
		if ( string.IsNullOrWhiteSpace( selector ) ) return;

		_window.AddRule( selector.Trim(), Array.Empty<(string, string)>() );
	}

	//Start a rule for the panel in a state - &:hover , &.open - nested in the rule the inspector would edit, or at the top of the sheet when there is none.
	internal void AddStateRule( string suffix )
	{
		if ( _cascade is null || _window.Document is not { } document ) return;

		var target = _cascade.EditTarget( null, document.ScssPath );

		if ( target is null )
		{
			AddRule( SelectorFor( _target, _window.LocateTag( _target ) ) + suffix );
			return;
		}

		_window.AddNestedRule( target, "&" + suffix );
	}

	//The forced states changed: the rules are read again, since a state's rule is live now.
	internal void NoteStateChanged() => _refreshIn = 2;

	//The selector the panel would be addressed by: the first class its tag writes - not one its
	//type gave it, as a Button gives itself button - else its first class, its id, or its element.
	internal static string SelectorFor( Panel panel, RazorMarkup.Element tag = null )
	{
		if ( !panel.IsValid() ) return "";

		var written = tag?.Classes.Split( ' ', StringSplitOptions.RemoveEmptyEntries ).FirstOrDefault( x => !x.Contains( '@' ) );
		if ( written is not null ) return "." + written;

		if ( panel.Class.FirstOrDefault() is { } c ) return "." + c;
		if ( !string.IsNullOrEmpty( panel.Id ) ) return "#" + panel.Id;
		if ( !string.IsNullOrEmpty( panel.ElementName ) ) return panel.ElementName;

		return panel.GetType().Name;
	}

	internal void OpenSource( Panel panel )
	{
		if ( panel is null || string.IsNullOrEmpty( panel.SourceFile ) ) return;

		OpenFile( panel.SourceFile, panel.SourceLine );
	}

	internal UiEditorWindow Window => _window;

	//Open a file in the code editor: a path from a project, or an absolute one.
	internal static void OpenFile( string path, int line )
	{
		if ( string.IsNullOrEmpty( path ) ) return;

		if ( !System.IO.Path.IsPathRooted( path ) || !System.IO.File.Exists( path ) )
		{
			var fromProject = Project.Current?.GetRootPath() is { } root ? System.IO.Path.Combine( root, path ) : null;

			if ( fromProject is not null && System.IO.File.Exists( fromProject ) ) path = fromProject;
			else if ( FileSystem.Content?.GetFullPath( path ) is { } mounted && System.IO.File.Exists( mounted ) ) path = mounted;
		}

		CodeEditor.OpenFile( path, line > 0 ? line : null );
	}
}

//One rule: its selector, where it is from, and its declarations.
internal sealed class RuleBlock : Panel
{
	readonly InspectorPanel _inspector;
	readonly StyleCascade.Rule _rule;

	public RuleBlock( InspectorPanel inspector, StyleCascade.Rule rule, List<IStyleBlock.StyleProperty> properties, PseudoClass waitsFor = PseudoClass.None )
	{
		_inspector = inspector;
		_rule = rule;

		AddClass( "rule" );
		SetClass( "inactive", waitsFor != PseudoClass.None );

		var header = AddChild<Panel>( "rule-header" );

		if ( waitsFor != PseudoClass.None )
		{
			var state = header.AddChild<UILabel>( "state" );
			state.Tokenize = false;
			state.Text = ":" + waitsFor.ToString().ToLowerInvariant();
			state.Tooltip = "Applies when the panel is in this state";
		}

		var selector = header.AddChild<UILabel>( "selector" );
		selector.Tokenize = false;
		selector.Text = rule is null ? "element.style" : rule.SelectorText;

		var where = header.AddChild<UILabel>( "where" );
		where.Tokenize = false;

		if ( rule?.IsInStyleTag ?? false )
		{
			//The block's line is counted from the tag's own text and not from the file, so the tag itself is what there is to point at.
			var line = inspector.Window.Document?.StyleTagLine ?? 0;

			where.Text = "in <style>";
			where.Tooltip = "Written in the razor rather than in a stylesheet, so it is shown and not edited";
			where.AddEventListener( "onclick", () => InspectorPanel.OpenFile( rule.Path, line ) );
		}
		else if ( rule?.FileName is { } file )
		{
			where.Text = $"{System.IO.Path.GetFileName( file )}:{rule.Block.FileLine + 1}";
			where.Tooltip = rule.Path ?? file;
			where.AddEventListener( "onclick", () => InspectorPanel.OpenFile( rule.Path ?? file, rule.Block.FileLine + 1 ) );
		}
		else
		{
			where.Text = rule is null ? "" : "built in";
			where.SetClass( "still", true );
		}

		var editable = rule?.CanWrite ?? false;
		SetClass( "readonly", !editable );

		var body = AddChild<Panel>( "declarations" );

		foreach ( var property in properties )
			body.AddChild( new DeclarationRow( inspector, rule, property.Name, property.Value ?? property.OriginalValue, editable ) );

		if ( editable )
			body.AddChild( new DeclarationRow( inspector, rule, "", "", true, isNew: true ) );
	}
}

//One name: value in a rule, the value a field when the rule is in a file.
internal sealed class DeclarationRow : Panel
{
	readonly InspectorPanel _inspector;
	readonly StyleCascade.Rule _rule;

	readonly UILabel _nameLabel;
	readonly TextField _nameField;
	readonly TextField _valueField;
	readonly UILabel _valueLabel;

	string _name;
	string _value;

	/// <summary>An empty row at the end of a rule, for a declaration that is not there yet.</summary>
	public bool IsNew { get; }

	public DeclarationRow( InspectorPanel inspector, StyleCascade.Rule rule, string name, string value, bool editable, bool isNew = false )
	{
		_inspector = inspector;
		_rule = rule;
		_name = name;
		_value = value;

		IsNew = isNew;

		AddClass( "declaration" );

		if ( editable && string.IsNullOrEmpty( name ) )
		{
			_nameField = AddChild<TextField>( "name field" );
			_nameField.Placeholder = "property";
			_nameField.Submitted = _ => Commit();
			_nameField.AddEventListener( "onblur", Commit );
		}
		else
		{
			_nameLabel = AddChild<UILabel>( "name" );
			_nameLabel.Tokenize = false;
			_nameLabel.Text = name;
		}

		var colon = AddChild<UILabel>( "colon" );
		colon.Tokenize = false;
		colon.Text = ":";

		if ( editable )
		{
			_valueField = AddChild<TextField>( "value field" );
			_valueField.Value = value;
			_valueField.Placeholder = string.IsNullOrEmpty( name ) ? "value" : "";
			_valueField.Submitted = _ => Commit();
			_valueField.AddEventListener( "onblur", Commit );

			if ( !string.IsNullOrEmpty( name ) )
				StyleChoices.Attach( this, name, _valueField, _ => Commit() );
		}
		else
		{
			_valueLabel = AddChild<UILabel>( "value" );
			_valueLabel.Tokenize = false;
			_valueLabel.Text = value;
		}

		Read();
	}

	//Struck through when a later rule decided the property.
	void Read()
	{
		if ( IsNew ) return;

		SetClass( "overridden", !_inspector.IsWinner( _rule, _name ) );
	}

	void Commit()
	{
		if ( _rule is null ) return;

		var name = _nameField?.Value ?? _name;
		var value = _valueField?.Value ?? _value;

		name = name?.Trim() ?? "";
		value = value?.Trim() ?? "";

		if ( IsNew )
		{
			//Both halves, or nothing: a name alone is somebody still typing.
			if ( name.Length == 0 || value.Length == 0 ) return;
		}
		else if ( value == _value )
		{
			return;
		}

		_name = name;
		_value = value;

		_inspector.Write( _rule, name, value );
	}
}

//The offer to start a rule for a panel its own sheet does not dress yet.
internal sealed class AddRuleRow : Panel
{
	public AddRuleRow( InspectorPanel inspector, Panel target )
	{
		AddClass( "add-rule" );

		var selector = InspectorPanel.SelectorFor( target, inspector.Window.LocateTag( target ) );

		var field = AddChild<TextField>( "field" );
		field.Value = selector;
		field.Submitted = value => inspector.AddRule( value );

		var button = AddChild<UILabel>( "button" );
		button.Tokenize = false;
		button.Text = "Add rule to .razor.scss";
		button.AddEventListener( "onclick", () => inspector.AddRule( field.Value ) );
	}
}

//What a document whose styles are in a <style> is told: the rules are read and shown, they cannot be written, and here is the way out.
internal sealed class StyleTagBlock : Panel
{
	public StyleTagBlock( InspectorPanel inspector )
	{
		AddClass( "style-tag" );

		var head = AddChild<Panel>( "head" );

		var icon = head.AddChild<UILabel>( "icon" );
		icon.Tokenize = false;
		icon.Text = "warning";

		var title = head.AddChild<UILabel>( "title" );
		title.Tokenize = false;
		title.Text = "Styles are in a <style> tag";

		var note = AddChild<UILabel>( "note" );
		note.Tokenize = false;
		note.Text = "The engine reads them, but a tag is markup and the editor writes styles into a sheet. Until they are moved, the rules below are shown and not edited, and a drag on the canvas writes a rule the tag may still win over.";

		var button = AddChild<UILabel>( "button" );
		button.Tokenize = false;
		button.Text = "Move them to .razor.scss";
		button.Tooltip = "Take the <style> out of the razor and put what it says in the sheet beside it - one change, undoable";
		button.AddEventListener( "onclick", () => inspector.Window.MoveStylesToSheet() );
	}
}

//A heading with rows under it, for the sections view.
internal sealed class SectionBlock : Panel
{
	readonly Panel _rows;

	public SectionBlock( string title )
	{
		AddClass( "section" );

		var heading = AddChild<UILabel>( "heading" );
		heading.Tokenize = false;
		heading.Text = title;

		_rows = AddChild<Panel>( "rows" );
	}

	public void AddRow( PropertyRow row ) => _rows.AddChild( row );
}

//One property in the sections view: its name, its winning value as a field, and the rule the value came from.
internal sealed class PropertyRow : Panel
{
	readonly InspectorPanel _inspector;
	readonly StyleCascade.Rule _from;
	readonly TextField _field;

	readonly string _name;
	string _value;

	public PropertyRow( InspectorPanel inspector, string name, string value, StyleCascade.Rule from )
	{
		_inspector = inspector;
		_from = from;
		_name = name;
		_value = value ?? "";

		AddClass( "property" );
		SetClass( "unset", string.IsNullOrEmpty( value ) );

		var label = AddChild<UILabel>( "name" );
		label.Tokenize = false;
		label.Text = name;

		_field = AddChild<TextField>( "value field" );
		_field.Value = _value;
		_field.Placeholder = "–";
		_field.Submitted = _ => Commit();
		_field.AddEventListener( "onblur", Commit );

		StyleChoices.Attach( this, name, _field, _ => Commit() );

		var source = AddChild<UILabel>( "source" );
		source.Tokenize = false;

		if ( from is not null )
		{
			source.Text = from.SelectorText;
			source.Tooltip = from.FileName is null ? "built in" : $"{from.FileName}:{from.Block.FileLine + 1}";

			if ( from.Path is not null )
				source.AddEventListener( "onclick", () => InspectorPanel.OpenFile( from.Path, from.Block.FileLine + 1 ) );
		}
		else if ( !string.IsNullOrEmpty( value ) )
		{
			source.Text = "inline";
		}
	}

	void Commit()
	{
		var value = _field.Value?.Trim() ?? "";

		if ( value == _value ) return;

		_value = value;
		_inspector.WriteProperty( _name, value, _from );
	}
}

//Which properties go under which heading in the sections view.
internal static class StyleSections
{
	public sealed class Section
	{
		public string Title;

		/// <summary>Shown whether or not anything sets them.</summary>
		public string[] Always;

		/// <summary>Any property starting with one of these belongs here too.</summary>
		public string[] Prefixes;

		public bool Owns( string name )
		{
			foreach ( var always in Always )
			{
				if ( string.Equals( always, name, StringComparison.OrdinalIgnoreCase ) ) return true;
			}

			foreach ( var prefix in Prefixes )
			{
				if ( name.StartsWith( prefix, StringComparison.OrdinalIgnoreCase ) ) return true;
			}

			return false;
		}
	}

	public static readonly Section[] All =
	{
		new()
		{
			Title = "Layout",
			Always = new[] { "display", "position", "left", "top", "right", "bottom", "flex-direction", "justify-content", "align-items", "flex-grow", "flex-shrink", "gap", "z-index" },
			Prefixes = new[] { "flex", "align", "justify", "grid", "order", "gap", "row-gap", "column-gap", "overflow", "z-index", "inset" },
		},
		new()
		{
			Title = "Box",
			Always = new[] { "width", "height", "min-width", "min-height", "max-width", "max-height", "margin", "padding", "border", "border-radius" },
			Prefixes = new[] { "width", "height", "min-", "max-", "margin", "padding", "border", "box-sizing", "aspect-ratio" },
		},
		new()
		{
			Title = "Text",
			Always = new[] { "font-family", "font-size", "font-weight", "font-color", "text-align", "line-height", "white-space" },
			Prefixes = new[] { "font", "color", "text", "line-height", "letter-spacing", "word", "white-space" },
		},
		new()
		{
			Title = "Background",
			Always = new[] { "background-color", "background-image", "background-size", "background-position" },
			Prefixes = new[] { "background" },
		},
		new()
		{
			Title = "Effects",
			Always = new[] { "opacity", "box-shadow", "transform", "transition", "cursor", "pointer-events" },
			Prefixes = new[] { "opacity", "box-shadow", "transform", "transition", "animation", "filter", "backdrop", "cursor", "pointer-events", "mix-blend", "sound", "mask" },
		},
	};
}

//The selected panel's tag: its classes, its id, the words in it when it holds words
//and nothing else, and its other attributes - each a field that writes to the razor.
internal sealed class TagBlock : Panel
{
	public TagBlock( InspectorPanel inspector, Panel target, RazorMarkup.Element tag )
	{
		AddClass( "tag-block" );

		var heading = AddChild<UILabel>( "heading" );
		heading.Tokenize = false;
		heading.Text = $"<{tag.Name}>";

		var rows = AddChild<Panel>( "rows" );

		var window = inspector.Window;

		rows.AddChild( new TagRow( "class", tag.Classes, value => window.SetAttribute( target, "class", value ) ) );
		rows.AddChild( new TagRow( "id", tag.Attribute( "id" )?.Value ?? "", value => window.SetAttribute( target, "id", value ) ) );

		//Words between the tags, when that is all that is there: no tags, no @.
		var inner = tag.Inner( window.Document.Markup.Text );

		if ( !tag.SelfClosing && tag.Children.Count == 0 && !inner.Contains( '@' ) && !inner.Contains( '<' ) )
			rows.AddChild( new TagRow( "text", inner.Trim(), value => window.SetText( target, value ), window.BindableNames ) );

		var shown = new HashSet<string>( StringComparer.OrdinalIgnoreCase ) { "class", "id" };

		foreach ( var attribute in tag.Attributes )
		{
			if ( !shown.Add( attribute.Name ) ) continue;

			var name = attribute.Name;
			var value = attribute.HasValue ? attribute.Value : "";

			//An event or a bound parameter is code; shown, but edited in the file.
			var readOnly = name.StartsWith( '@' ) || ( value?.Contains( '@' ) ?? false );

			rows.AddChild( new TagRow( name, value, readOnly ? null : v => window.SetAttribute( target, name, v ), window.BindableNames ) );
		}

		//A component's parameters are attributes it takes, whether or not the tag gives them yet: each an empty row until it does.
		foreach ( var parameter in ParametersOf( tag.Name ) )
		{
			if ( !shown.Add( parameter ) ) continue;

			rows.AddChild( new TagRow( parameter, "", v => window.SetAttribute( target, parameter, v ), window.BindableNames ) { IsParameter = true } );
		}
	}

	//The [Parameter] properties of the component a tag names - a type of the project's or the base library's, not a plain tag.
	static IEnumerable<string> ParametersOf( string tag )
	{
		if ( TypeLibrary.GetType<Panel>( tag ) is not { } type ) return Array.Empty<string>();
		if ( type.TargetType.Assembly == typeof( Panel ).Assembly ) return Array.Empty<string>();

		return type.Properties
			.Where( x => x.HasAttribute<ParameterAttribute>() && x.IsPublic && x.CanWrite )
			.Select( x => x.Name )
			.OrderBy( x => x, StringComparer.OrdinalIgnoreCase )
			.ToList();
	}
}

//Anchor picker: sets which parent edges a panel hangs from, as plain CSS, keeping it in place
internal sealed class AnchorBlock : Panel
{
	static readonly (string name, string icon, int x, int y)[] Places =
	{
		( "top left", "north_west", 0, 0 ), ( "top, stretched across", "north", 1, 0 ), ( "top right", "north_east", 2, 0 ),
		( "left, stretched down", "west", 0, 1 ), ( "stretched both ways", "open_in_full", 1, 1 ), ( "right, stretched down", "east", 2, 1 ),
		( "bottom left", "south_west", 0, 2 ), ( "bottom, stretched across", "south", 1, 2 ), ( "bottom right", "south_east", 2, 2 ),
	};

	public AnchorBlock( InspectorPanel inspector, Panel target, RazorMarkup.Element tag )
	{
		AddClass( "anchor-block" );

		var heading = AddChild<UILabel>( "heading" );
		heading.Tokenize = false;
		heading.Text = "Anchor";

		var row = AddChild<Panel>( "row" );
		var grid = row.AddChild<Panel>( "grid" );

		var style = target.ComputedStyle;
		var absolute = style?.Position is PositionMode.Absolute;

		//Which of the three each axis is now, by what the rule says.
		var nowX = !absolute ? -1 : style.Left.IsSet() && style.Right.IsSet() ? 1 : style.Right.IsSet() ? 2 : style.Left.IsSet() ? 0 : -1;
		var nowY = !absolute ? -1 : style.Top.IsSet() && style.Bottom.IsSet() ? 1 : style.Bottom.IsSet() ? 2 : style.Top.IsSet() ? 0 : -1;

		foreach ( var (name, icon, x, y) in Places )
		{
			var button = grid.AddChild<UILabel>( "material-icon place" );
			button.Tokenize = false;
			button.Text = icon;
			button.Tooltip = $"Anchor to the {name}";
			button.SetClass( "lit", x == nowX && y == nowY );

			var (px, py) = ( x, y );
			button.AddEventListener( "onclick", () => Anchor( inspector.Window, target, px, py ) );
		}

		var side = row.AddChild<Panel>( "side" );

		var fill = side.AddChild<UILabel>( "fill" );
		fill.Tokenize = false;
		fill.Text = "Fill";
		fill.Tooltip = "Stretch to every edge of the parent, with no size of its own";
		fill.AddEventListener( "onclick", () => Fill( inspector.Window, target ) );

		var flow = side.AddChild<UILabel>( "fill" );
		flow.Tokenize = false;
		flow.Text = "In flow";
		flow.Tooltip = "Back into the parent's layout: no position, no edges";
		flow.AddEventListener( "onclick", () => Flow( inspector.Window, target ) );

		var note = AddChild<UILabel>( "note" );
		note.Tokenize = false;
		note.Text = !absolute ? "in the parent's flow" : $"{( nowX == 1 ? "stretched" : nowX == 2 ? "from the right" : "from the left" )}, {( nowY == 1 ? "stretched" : nowY == 2 ? "from the bottom" : "from the top" )}";
	}

	//The panel's distances from its parent's padding box, in style pixels: left, top, right, bottom, width, height.
	static (float left, float top, float right, float bottom, float width, float height) Measure( Panel panel )
	{
		var scale = panel.ScaleFromScreen;
		var box = panel.Box.Rect;
		var parent = panel.Parent?.Box;

		//The edges are the margin box's: left is what the layout puts between the parent's padding edge and the panel's margin, and the
		//margin goes on top of it - measured from the border box, a panel with a margin would jump by that margin the moment it was anchored.
		var outer = panel.Box.RectOuter;

		//Where the parent's content starts, its own scroll taken back out.
		var scroll = ( panel.Parent?.ScrollOffset ?? Vector2.Zero ) * panel.ScaleToScreen;
		var originX = parent is null ? 0 : parent.Rect.Left + parent.Border.Left - scroll.x;
		var originY = parent is null ? 0 : parent.Rect.Top + parent.Border.Top - scroll.y;
		var extentX = parent is null ? outer.Right : parent.Rect.Right - parent.Border.Right - scroll.x;
		var extentY = parent is null ? outer.Bottom : parent.Rect.Bottom - parent.Border.Bottom - scroll.y;

		return (
			( outer.Left - originX ) * scale,
			( outer.Top - originY ) * scale,
			( extentX - outer.Right ) * scale,
			( extentY - outer.Bottom ) * scale,
			box.Width * scale,
			box.Height * scale );
	}

	static string Px( float v ) => $"{MathF.Round( v ):0}px";

	//Hang the panel from a place on its parent, where it is now and the size it has.
	internal static void Anchor( UiEditorWindow window, Panel panel, int x, int y )
	{
		var m = Measure( panel );
		var d = new List<(string, string)> { ( "position", "absolute" ) };

		switch ( x )
		{
			case 0: d.Add( ( "left", Px( m.left ) ) ); d.Add( ( "right", "" ) ); d.Add( ( "width", Px( m.width ) ) ); break;
			case 2: d.Add( ( "right", Px( m.right ) ) ); d.Add( ( "left", "" ) ); d.Add( ( "width", Px( m.width ) ) ); break;
			default: d.Add( ( "left", Px( m.left ) ) ); d.Add( ( "right", Px( m.right ) ) ); d.Add( ( "width", "" ) ); break;
		}

		switch ( y )
		{
			case 0: d.Add( ( "top", Px( m.top ) ) ); d.Add( ( "bottom", "" ) ); d.Add( ( "height", Px( m.height ) ) ); break;
			case 2: d.Add( ( "bottom", Px( m.bottom ) ) ); d.Add( ( "top", "" ) ); d.Add( ( "height", Px( m.height ) ) ); break;
			default: d.Add( ( "top", Px( m.top ) ) ); d.Add( ( "bottom", Px( m.bottom ) ) ); d.Add( ( "height", "" ) ); break;
		}

		window.WriteStyles( panel, d );
	}

	//Every edge to the parent's, and no size of its own.
	internal static void Fill( UiEditorWindow window, Panel panel )
	{
		window.WriteStyles( panel, new List<(string, string)>
		{
			( "position", "absolute" ), ( "left", "0" ), ( "top", "0" ), ( "right", "0" ), ( "bottom", "0" ), ( "width", "" ), ( "height", "" ),
		} );
	}

	//Back into the flow: the position and every edge taken off; the size stays.
	internal static void Flow( UiEditorWindow window, Panel panel )
	{
		window.WriteStyles( panel, new List<(string, string)>
		{
			( "position", "" ), ( "left", "" ), ( "top", "" ), ( "right", "" ), ( "bottom", "" ),
		} );
	}
}

internal sealed class TagRow : Panel
{
	readonly TextField _field;
	readonly Action<string> _write;

	string _value;

	/// <summary>A parameter the tag's type takes, shown whether or not the tag gives it.</summary>
	public bool IsParameter { get; init { field = value; SetClass( "parameter", value ); } }

	public TagRow( string name, string value, Action<string> write, Func<IEnumerable<string>> bindableInit = null )
	{
		var bindable = bindableInit;
		_write = write;
		_value = value ?? "";

		AddClass( "property" );
		SetClass( "unset", string.IsNullOrEmpty( value ) );

		var label = AddChild<UILabel>( "name" );
		label.Tokenize = false;
		label.Text = name;

		if ( write is null )
		{
			var still = AddChild<UILabel>( "value still" );
			still.Tokenize = false;
			still.Text = value;
			return;
		}

		_field = AddChild<TextField>( "value field" );
		_field.Value = _value;
		_field.Placeholder = "–";
		_field.Submitted = _ => Commit();
		_field.AddEventListener( "onblur", Commit );

		//Bound to a member instead of written out: @Name goes in its place.
		if ( bindable is not null )
		{
			var bind = AddChild<UILabel>( "material-icon bind" );
			bind.Tokenize = false;
			bind.Text = "link";
			bind.Tooltip = "Bind to a member of the document";
			bind.AddEventListener( "onclick", () => OpenBindMenu( bindable ) );
		}
	}

	void OpenBindMenu( Func<IEnumerable<string>> bindable )
	{
		var names = bindable().ToList();
		var menu = new Menu( null );

		if ( names.Count == 0 )
			menu.AddOption( "No members - add one in the Data dock", "info" ).Enabled = false;

		foreach ( var name in names )
			menu.AddOption( "@" + name, "data_object", () => { _field.Value = "@" + name; Commit(); } );

		menu.OpenAtCursor();
	}

	void Commit()
	{
		var value = _field.Value?.Trim() ?? "";

		if ( value == _value ) return;

		_value = value;
		_write( value );
	}
}

//The states the panel can be put into: the pseudo-classes, and the classes its rules switch on. A state held
//makes its rules live in the list below, where they can be edited; a state with no rule yet offers to start one.
internal sealed class StatesBlock : Panel
{
	public StatesBlock( InspectorPanel inspector, PanelStates states )
	{
		AddClass( "states-block" );

		var heading = AddChild<UILabel>( "heading" );
		heading.Tokenize = false;
		heading.Text = "States";

		var row = AddChild<Panel>( "row" );

		var panel = states.Panel;
		var cascade = inspector.Cascade;

		foreach ( var (flag, name) in PanelStates.Pseudo )
		{
			//Intro and outro only where something uses them.
			var hasRule = cascade is not null && ( cascade.Inactive.Any( x => x.state == flag ) || ( panel.PseudoClass & flag ) != 0 );
			if ( flag is PseudoClass.Intro or PseudoClass.Outro && !hasRule ) continue;

			var toggle = row.AddChild<UILabel>( "state" );
			toggle.Tokenize = false;
			toggle.Text = ":" + name;
			toggle.SetClass( "on", states.IsForced( flag ) );
			toggle.SetClass( "ruled", hasRule );
			toggle.Tooltip = hasRule ? $"Hold the panel :{name}" : $"Hold the panel :{name} - no rule for it yet; right click to add one";

			toggle.AddEventListener( "onclick", () =>
			{
				states.Force( flag, !states.IsForced( flag ) );
				toggle.SetClass( "on", states.IsForced( flag ) );
				inspector.NoteStateChanged();
			} );

			toggle.AddEventListener( "onrightclick", () => inspector.AddStateRule( ":" + name ) );
		}

		foreach ( var cls in PanelStates.StateClasses( panel ) )
		{
			var toggle = row.AddChild<UILabel>( "state class" );
			toggle.Tokenize = false;
			toggle.Text = "." + cls;
			toggle.SetClass( "on", states.IsForced( cls ) );
			toggle.SetClass( "ruled", true );
			toggle.Tooltip = $"Give the panel .{cls}";

			toggle.AddEventListener( "onclick", () =>
			{
				states.Force( cls, !states.IsForced( cls ) );
				toggle.SetClass( "on", states.IsForced( cls ) );
				inspector.NoteStateChanged();
			} );
		}

		//A class state of the designer's own naming.
		var add = row.AddChild<TextField>( "new-state field" );
		add.Placeholder = ".state";
		add.Submitted = text =>
		{
			var cls = ( text ?? "" ).Trim().TrimStart( '.' );
			if ( cls.Length == 0 ) return;

			inspector.AddStateRule( "." + cls );
			add.Value = "";
		};
	}
}

//Event handlers on a tag; adding one writes the attribute and an @code method, then opens it
internal sealed class EventsBlock : Panel
{
	//The events a panel gets, as the engine names them.
	static readonly string[] Events =
	{
		"onclick", "ondoubleclick", "onrightclick", "onmiddleclick",
		"onmousedown", "onmouseup", "onmousemove", "onmouseover", "onmouseout",
		"onfocus", "onblur", "onescape", "onback", "onforward",
		"ondragenter", "ondragleave", "ondrop", "onchange", "onsubmit",
	};

	public EventsBlock( InspectorPanel inspector, Panel target, RazorMarkup.Element tag )
	{
		AddClass( "events-block" );

		var heading = AddChild<UILabel>( "heading" );
		heading.Tokenize = false;
		heading.Text = "Events";

		var rows = AddChild<Panel>( "rows" );
		var window = inspector.Window;

		foreach ( var attribute in tag.Attributes )
		{
			var name = attribute.Name.TrimStart( '@' );
			if ( !name.StartsWith( "on", StringComparison.OrdinalIgnoreCase ) ) continue;

			var row = rows.AddChild<Panel>( "property" );

			var label = row.AddChild<UILabel>( "name" );
			label.Tokenize = false;
			label.Text = name;

			var value = row.AddChild<UILabel>( "value still" );
			value.Tokenize = false;
			value.Text = attribute.Value ?? "";

			//The handler, where it is a method of the document's: a jump to it.
			var method = MethodNamed( attribute.Value );

			if ( method is not null )
			{
				value.Tooltip = "Open the handler in the code editor";
				value.AddClass( "link" );
				value.AddEventListener( "onclick", () => window.OpenCodeMember( method ) );
			}

			var remove = row.AddChild<UILabel>( "material-icon remove" );
			remove.Tokenize = false;
			remove.Text = "close";
			remove.Tooltip = "Take the handler off the tag - the method stays in @code";
			remove.AddEventListener( "onclick", () => window.SetAttribute( target, attribute.Name, "" ) );
		}

		var add = rows.AddChild<UILabel>( "add" );
		add.Tokenize = false;
		add.Text = "+ handler";
		add.AddEventListener( "onclick", () => OpenAddMenu( window, target, tag ) );
	}

	//The method a handler attribute names outright - @OnClick - or null for a lambda or nothing.
	static string MethodNamed( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) ) return null;

		var v = value.Trim().TrimStart( '@' );
		return v.All( c => char.IsLetterOrDigit( c ) || c == '_' ) ? v : null;
	}

	static void OpenAddMenu( UiEditorWindow window, Panel target, RazorMarkup.Element tag )
	{
		var menu = new Menu( null );
		var has = tag.Attributes.Select( x => x.Name.TrimStart( '@' ).ToLowerInvariant() ).ToHashSet();

		var asMethod = menu.AddMenu( "Method in @code", "code" );
		var asParameter = menu.AddMenu( "Parameter of type Action", "input" );

		foreach ( var name in Events )
		{
			if ( has.Contains( name ) ) continue;

			asMethod.AddOption( name, null, () => window.AddEventHandler( target, name, asParameter: false ) );
			asParameter.AddOption( name, null, () => window.AddEventHandler( target, name, asParameter: true ) );
		}

		menu.OpenAtCursor();
	}
}
