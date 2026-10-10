namespace Rinten
{
	public partial class Material
	{

		/// <summary>The interface's own drawing, as materials.</summary>
		public static class UI
		{
			/// <summary>As basic 2D drawing material. Supports Texture and vertex color.</summary>
			public static Material Basic { get => field ??= Engine( EngineDraw.Picture, "ui.picture" ); internal set; }

			/// <summary>CSS Box rendering - a rounded rectangle with its border, laid out by the interface and drawn by CoreUiRenderer.</summary>
			public static Material Box { get => field ??= Engine( EngineDraw.Box, "ui.box" ); internal set; }

			//A run of boxes drawn from a buffer on the card. Nothing draws these yet - see EngineDraw.Batched and UIBatcher.Draw.
			internal static Material BatchedBox { get => field ??= Engine( EngineDraw.Batched, "ui.boxes" ); set; }

			//CSS Text rendering: the atlas the text renderer filled, drawn as a picture in the rectangle the text was measured into.
			internal static Material Text { get => field ??= Engine( EngineDraw.Text, "ui.text" ); set; }

			//CSS Box Shadow; this and the next four have no pass yet and draw as a plain tinted picture
			internal static Material BoxShadow { get => field ??= Engine( EngineDraw.Picture, "ui.boxshadow" ); set; }
			internal static Material BackdropFilter { get => field ??= Engine( EngineDraw.Picture, "ui.backdropfilter" ); set; }
			internal static Material Filter { get => field ??= Engine( EngineDraw.Picture, "ui.filter" ); set; }

			//For filter: border-wrap( ... );
			internal static Material BorderWrap { get => field ??= Engine( EngineDraw.Picture, "ui.borderwrap" ); set; }

			//For filter: drop-shadow( ... );
			internal static Material DropShadow { get => field ??= Engine( EngineDraw.Picture, "ui.dropshadow" ); set; }

			//CSS Outline rendering
			internal static Material Outline { get => field ??= Engine( EngineDraw.Picture, "ui.outline" ); set; }
		}
	}
}
