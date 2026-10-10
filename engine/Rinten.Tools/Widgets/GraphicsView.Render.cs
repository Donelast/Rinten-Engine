using System;

namespace Editor
{
	internal enum RenderHints
	{
		Antialiasing = 0x01,
		TextAntialiasing = 0x02,
		SmoothPixmapTransform = 0x04,
		LegacyPainting = 0x20,
		LosslessImageRendering = 0x40,
	};

	public partial class GraphicsView
	{
		public bool Antialiasing
		{
		set { }
		}

		public bool TextAntialiasing
		{
		set { }
		}

		public bool BilinearFiltering
		{
		set { }
		}

	}
}
