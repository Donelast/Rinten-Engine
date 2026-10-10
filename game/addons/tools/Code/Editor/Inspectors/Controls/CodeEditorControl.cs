namespace Editor.Inspectors;

using Rinten.UI;

//Everything implementing ICodeEditor is offered, installed or not: one that is not installed still says what it is, which is more use than a list that is empty because nothing was found.
/// <summary>Which code editor the editor hands a file to.</summary>
[StyleSheet( "CodeEditorControl.cs.scss" )]
[CustomEditor( typeof( ICodeEditor ) )]
public sealed class CodeEditorControl : BaseControl
{
	readonly ControlDropDown _dropdown;

	public CodeEditorControl()
	{
		ShowBackground = false;

		_dropdown = AddChild<ControlDropDown>();
		_dropdown.BuildOptions = BuildOptions;
		_dropdown.ValueChanged = Assign;
	}

	public override void Rebuild()
	{
		SyncDisplay();
		ApplyDisabled();
	}

	public override void Tick()
	{
		base.Tick();

		SyncDisplay();
	}

	protected override void OnReadOnlyChanged() => ApplyDisabled();

	List<Option> BuildOptions()
	{
		var options = new List<Option>();

		foreach ( var editor in Editors() )
		{
			var installed = editor.Create<ICodeEditor>()?.IsInstalled() ?? false;

			options.Add( new Option( installed ? editor.Title : $"{editor.Title} (not installed)", editor.Icon, editor.Name )
			{
				Tooltip = editor.Description
			} );
		}

		if ( options.Count == 0 )
			options.Add( new Option( "None - install one!", "error", "" ) );

		return options;
	}

	//The editors to offer, installed ones first within each order group.
	static IEnumerable<TypeDescription> Editors()
	{
		return EditorTypeLibrary.GetTypes<ICodeEditor>()
			.Where( x => !x.IsInterface && x.TargetType != typeof( ICodeEditor ) )
			.OrderBy( x => x.Order )
			.ThenByDescending( x => x.Create<ICodeEditor>()?.IsInstalled() ?? false )
			.ThenBy( x => x.Name );
	}

	void Assign( string name )
	{
		if ( IsReadOnly || Property is null || string.IsNullOrEmpty( name ) ) return;

		var type = EditorTypeLibrary.GetType<ICodeEditor>( name );

		if ( type is null ) return;

		Property.Parent?.NoteStartEdit( Property );
		Property.SetValue( type.Create<ICodeEditor>() );
		Property.Parent?.NoteFinishEdit( Property );
	}

	void SyncDisplay()
	{
		if ( Property is null ) return;

		var current = Property.GetValue<ICodeEditor>()?.GetType();
		var name = current is null ? "" : EditorTypeLibrary.GetType( current )?.Name ?? "";

		if ( !string.Equals( _dropdown.Value as string, name, StringComparison.Ordinal ) )
			_dropdown.Value = name;
	}

	void ApplyDisabled()
	{
		if ( _dropdown is null ) return;

		_dropdown.Disabled = IsReadOnly;
		_dropdown.AcceptsFocus = !IsReadOnly;
	}
}
