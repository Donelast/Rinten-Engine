using Editor.Inspectors;
using Rinten.UI;

using UILabel = Rinten.UI.Label;

namespace Editor.FxEditor;

/// <summary>ParticlePlayer inspector: effect, open parameters, and the button into the particle editor</summary>
[CustomEditor( typeof( ParticlePlayer ) )]
[StyleSheet( "ParticlePlayerPanel.cs.scss" )]
public sealed class ParticlePlayerPanel : ComponentPanel
{
	readonly ParticlePlayer _player;
	readonly Panel _parameters;

	ParticleDefinition _shown;
	int _shownRevision = -1;

	public ParticlePlayerPanel( SerializedObject obj )
	{
		TargetObject = obj;
		_player = obj.Targets.FirstOrDefault() as ParticlePlayer;

		AddClass( "particle-player" );

		//The anchor rows pick names off this player's effect, which no open editor need have - see ParticleNameControl.Context.
		ParticleNameControl.Context = ( obj.Targets?.FirstOrDefault() as ParticlePlayer )?.Definition;

		var sheet = AddChild<Inspectors.ControlSheet>();

		sheet.PropertyFilter = Shows;
		sheet.Target = obj;
		sheet.Rebuild();

		var buttons = AddChild<Panel>( "buttons" );

		Button( buttons, "Edit", "edit", EditEffect, () => Asset() is not null );
		Button( buttons, "Restart", "replay", () => _player?.Restart(), () => _player.IsValid() );

		_parameters = AddChild<Panel>( "parameters" );

		BuildParameters();
	}

	//Which of the component's own properties the sheet shows. The three dictionaries of overrides are not
	//among them: they are what the parameter rows below are, said once and properly rather than twice.
	static bool Shows( SerializedProperty property )
	{
		if ( property.PropertyType is null ) return false;

		if ( property.Name is nameof( ParticlePlayer.FloatOverrides )
			or nameof( ParticlePlayer.VectorOverrides )
			or nameof( ParticlePlayer.ColorOverrides )
			or nameof( ParticlePlayer.ClearParameters ) )
			return false;

		if ( property.IsMethod ) return true;

		return property.HasAttribute<PropertyAttribute>();
	}

	static void Button( Panel parent, string text, string icon, Action pressed, Func<bool> enabled = null )
	{
		var button = parent.AddChild<TextButtonPanel>();

		button.Text = text;
		button.Icon = icon;
		button.Pressed = pressed;
		button.EnabledWhen = enabled;
	}

	/// <summary>The effect is swapped, or edited in the other window, without this panel being rebuilt - so what it is showing is checked against what is there.</summary>
	public override void Tick()
	{
		base.Tick();

		if ( !_player.IsValid() ) return;

		var definition = _player.Definition;

		if ( definition == _shown && ( definition?.Revision ?? -1 ) == _shownRevision )
			return;

		BuildParameters();
	}

	void BuildParameters()
	{
		_parameters.DeleteChildren();

		_shown = _player.IsValid() ? _player.Definition : null;
		_shownRevision = _shown?.Revision ?? -1;

		if ( _shown is null )
		{
			Note( "No effect. Pick a .fx above, or make one." );
			return;
		}

		var values = new ParticleParameterObject( _player, _shown );

		if ( values.IsEmpty )
		{
			Note( "This effect declares no parameters." );
			return;
		}

		var header = _parameters.AddChild<Panel>( "header" );

		var title = header.AddChild<UILabel>( "title" );
		title.Text = "Parameters";

		Button( header, "Reset", "settings_backup_restore", () =>
		{
			_player.ClearParameters();
			BuildParameters();
		} );

		//Forward parameter edits to the component's undo, since they aren't component properties
		values.OnPropertyStartEdit += property => TargetObject.NoteStartEdit( property );
		values.OnPropertyChanged += property => TargetObject.NoteChanged( property );
		values.OnPropertyFinishEdit += property => TargetObject.NoteFinishEdit( property );

		var sheet = _parameters.AddChild<Inspectors.ControlSheet>();

		sheet.Target = values;
		sheet.Rebuild();
	}

	void Note( string text )
	{
		var note = _parameters.AddChild<UILabel>( "note" );

		note.Text = text;
	}

	//The asset the open effect was loaded from, where it came from one.
	Asset Asset()
	{
		if ( _player?.Definition?.ResourcePath is not { Length: > 0 } path )
			return null;

		return AssetSystem.FindByPath( path );
	}

	void EditEffect()
	{
		if ( Asset() is not { } asset ) return;

		IAssetEditor.OpenInEditor( asset, out _ );
	}
}
