using Rinten.UI;
using System;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>What the editor shows when the scene has no movie player to edit.</summary>
[StyleSheet( "StartupHelperPanel.cs.scss" )]
public sealed class StartupHelperPanel : Panel
{
	private readonly Rinten.UI.Button _create;

	public StartupHelperPanel( Action createPlayer )
	{
		AddClass( "movie-startup" );

		var text = AddChild<Rinten.UI.Label>( "message" );

		text.Text = "Create a Movie Player component to get started.\n"
			+ "The Movie Player is responsible for playing a movie clip in-game.";

		_create = AddChild( new Rinten.UI.Button( "Create Movie Player Component", "add_circle", createPlayer ) );

		_create.AddClass( "primary" );
	}

	public override void Tick()
	{
		base.Tick();

		_create.SetClass( "disabled", SceneEditorSession.Active is not { Scene.IsValid: true } );
	}
}
