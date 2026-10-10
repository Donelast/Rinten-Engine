using System;
using Rinten.UI;

namespace Editor;

//Overlay panel that eats the pick click so it never reaches the button underneath
internal sealed class SourceEyedropperCatcher
{
	readonly Action picked;
	readonly Dictionary<EditorOverlay, Sheet> sheets = new();

	public SourceEyedropperCatcher( Action picked )
	{
		this.picked = picked;
	}

	/// <summary>Covers every editor window, including ones opened later, so a pick works anywhere</summary>
	public void CoverEverything()
	{
		foreach ( var window in Window.All )
		{
			if ( window is not { IsValid: true } ) continue;
			if ( !window.Visible ) continue;

			if ( EditorOverlay.For( window ) is not { } layer ) continue;

			if ( sheets.TryGetValue( layer, out var already ) && already.IsValid() )
				continue;

			var sheet = new Sheet { Picked = picked };

			layer.AddChild( sheet );

			sheets[layer] = sheet;
		}
	}

	public void Destroy()
	{
		foreach ( var sheet in sheets.Values )
		{
			sheet?.Delete( true );
		}

		sheets.Clear();
	}

	sealed class Sheet : Panel
	{
		public Action Picked { get; init; }

		public Sheet()
		{
			AddClass( SourceEyedropperTool.FurnitureClass );
			AddClass( "picker-catcher" );
		}

		protected override void OnMouseDown( MousePanelEvent e )
		{
			base.OnMouseDown( e );

			e.StopPropagation();

			Picked?.Invoke();
		}
	}
}
