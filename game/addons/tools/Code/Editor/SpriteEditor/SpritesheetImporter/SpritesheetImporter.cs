using Rinten.Rendering;

namespace Editor.SpriteEditor;

/// <summary>Opens SpritesheetImporterPanel as a card on the window overlay (see EditorModal)</summary>
public static class SpritesheetImporter
{
	public static void Open( string imagePath, FilterMode filter, Action<string, IReadOnlyList<Rect>> onImport,
		string action = null, Vector2Int? cellSize = null )
	{
		SpritesheetImporterPanel panel = null;

		panel = new SpritesheetImporterPanel
		{
			ImagePath = imagePath,
			Filter = filter,
			Action = action ?? "Import Spritesheet",
			CellSize = cellSize,
			OnImport = cells =>
			{
				SpriteEditorModal.Close( panel );
				onImport?.Invoke( imagePath, cells );
			},
		};

		SpriteEditorModal.Open( panel );
	}
}
