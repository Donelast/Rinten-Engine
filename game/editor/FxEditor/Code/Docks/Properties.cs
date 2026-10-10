using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.FxEditor;

/// <summary>Properties dock: editable fields of the tree selection; the effect itself shows its settings</summary>
public sealed class Properties : PanelDockHost
{
	readonly PropertiesPanel _panel;

	//No parent: the dock manager is what puts a dock somewhere, and a parent handed over here is replaced the moment the dock is opened.
	public Properties( ParticleEditorWindow window ) : base( null )
	{
		Name = "Properties";
		WindowTitle = "Properties";
		SetWindowIcon( "tune" );

		_panel = new PropertiesPanel( window, compact: false );

		SetContent( _panel );
	}

	/// <summary>Show an effect, an emitter or a module.</summary>
	public void Show( object target ) => _panel.Target = target;
}

//Who an effect's properties are edited for: the window (undo, preview) or the inspector
internal interface IEffectEditor
{
	//Something was changed: the players follow, and it is kept.
	void OnEdited( string name = null );

	//An edit finished: one step of undo, where there is undo.
	void CommitUndo( string name = null );

	void NoteAnchorRenamed( ParticleAnchor anchor, string was );
	void NoteShapeRenamed( ParticleShape shape, string was );
	void NoteParameterRenamed( ParticleDefinition.Parameter parameter, string was );
}

//The header, and the sheet under it. The window's Properties dock, and - compact, with no header,
//the card over it saying what it is - the inspector's card for what is picked in an effect's tree.
[StyleSheet( "Properties.cs.scss" )]
internal sealed class PropertiesPanel : EditorPanel
{
	readonly IEffectEditor _window;

	readonly UILabel _title;
	readonly UILabel _subtitle;
	readonly Panel _scroll;

	//The properties themselves. One sheet, told what to show.
	readonly Inspectors.ControlSheet _sheet;

	object _target;

	public object Target
	{
		get => _target;
		set
		{
			//Rebuilt even where it is the same object: an undo deserializes the effect in place,
			//and what the sheet is holding is the properties of objects that no longer exist.
			_target = value;

			Rebuild();
		}
	}

	public PropertiesPanel( IEffectEditor window, bool compact )
	{
		_window = window;

		AddClass( "particle-properties" );
		SetClass( "compact", compact );

		var header = AddChild<Panel>( "header" );

		_title = header.AddChild<UILabel>( "title" );
		_subtitle = header.AddChild<UILabel>( "subtitle" );

		_title.Tokenize = false;
		_subtitle.Tokenize = false;

		_scroll = AddChild<Panel>( "scroll" );

		//One sheet, kept. Setting its target is what rebuilds it, and it empties itself first.
		_sheet = _scroll.AddChild( new Inspectors.ControlSheet() );
	}

	void Rebuild()
	{
		if ( _target is null )
		{
			_title.Text = "Nothing selected";
			_subtitle.Text = string.Empty;
			_sheet.Target = null;
			_sheet.Rebuild();

			return;
		}

		ReadHeader();

		//The rows of a mesh module are what its model and material offer; a file saved against an older shader shows the shader as it is now.
		if ( _target is MeshRenderModule syncing )
			syncing.Sync();

		var serialized = _target.GetSerialized();

		//A parameter's name is what the modules read it by, so a name typed here carries them with it - see ParticleEditorWindow.RenameParameter.
		var parameterName = ( _target as ParticleDefinition.Parameter )?.Name;
		var shapeName = ( _target as ParticleShape )?.Name;
		var anchorName = ( _target as ParticleAnchor )?.Name;

		serialized.OnPropertyChanged += property =>
		{
			if ( _target is ParticleAnchor anchor && property?.Name == nameof( ParticleAnchor.Name ) )
			{
				_window.NoteAnchorRenamed( anchor, anchorName );
				anchorName = anchor.Name;
			}

			if ( _target is ParticleShape shape && property?.Name == nameof( ParticleShape.Name ) )
			{
				_window.NoteShapeRenamed( shape, shapeName );
				shapeName = shape.Name;
			}

			if ( _target is ParticleDefinition.Parameter parameter && property?.Name == nameof( ParticleDefinition.Parameter.Name ) )
			{
				_window.NoteParameterRenamed( parameter, parameterName );
				parameterName = parameter.Name;
			}

			_window.OnEdited();

			//A new model or material on a mesh brings its own blend shapes and shader numbers: the rows follow, and the sheet is built again to show them.
			if ( _target is MeshRenderModule mesh && property?.Name is nameof( MeshRenderModule.Model ) or nameof( MeshRenderModule.Material ) )
			{
				mesh.Sync();
				Rebuild();
			}
		};

		//A drag on a slider is a change per frame and one undo entry, not hundreds: the name is pushed when the edit finishes rather than when a value moves.
		serialized.OnPropertyFinishEdit += property =>
			_window.CommitUndo( property is null ? "Edit Properties" : $"Edit {property.Name}" );

		_sheet.Target = serialized;
		_sheet.Rebuild();
	}

	void ReadHeader() => (_title.Text, _subtitle.Text) = Describe( _target );

	//What to head a thing in the effect with, and the line under that.
	internal static (string Title, string Subtitle) Describe( object target )
	{
		switch ( target )
		{
			case ParticleDefinition:
				return ("Effect", "Duration, looping, slots");

			case ParticleDefinition.Emitter emitter:
				return (emitter.Name, "Emitter");

			case ParticleModule module:
				var info = DisplayInfo.ForType( module.GetType() );

				return (module.Name ?? info.Name,
					string.IsNullOrEmpty( info.Description ) ? $"{module.Stage} module" : FirstSentence( info.Description ));

			case ParticleAnchor anchor:
				return (anchor.Name, "Anchor: a pose the scene binds to an object");

			case ParticleShape shape:
				return (shape.Name, "Shape: geometry modules sample by name");

			case ParticleDefinition.Parameter parameter:
				return (parameter.Name, $"{ParameterUI.TypeName( ParameterList.KindOf( parameter ) )} parameter");

			default:
				return (target?.GetType().Name ?? "", string.Empty);
		}
	}

	//The first sentence of a description - the header has room for one.
	internal static string FirstSentence( string text )
	{
		if ( string.IsNullOrEmpty( text ) ) return text;

		var end = text.IndexOfAny( new[] { '.', ':', ';' } );

		return end > 0 ? text[..end] : text;
	}

	/// <summary>The name in the header follows what is typed into the name field, without the sheet being built again under the cursor.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( _target is ParticleDefinition.Emitter emitter && _title.Text != emitter.Name )
			_title.Text = emitter.Name;

		if ( _target is ParticleDefinition.Parameter parameter && _title.Text != parameter.Name )
			_title.Text = parameter.Name;

		if ( _target is ParticleShape shape && _title.Text != shape.Name )
			_title.Text = shape.Name;

		if ( _target is ParticleAnchor anchor && _title.Text != anchor.Name )
			_title.Text = anchor.Name;
	}
}
