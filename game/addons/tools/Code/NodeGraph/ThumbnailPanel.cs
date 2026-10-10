using Rinten.UI;

namespace Editor.NodeEditor;

//The panel side of the editor draws textures, so the picture is uploaded once and kept until a different one arrives. Uploading it per frame would make a texture per frame, and the device runs out of those.
/// <summary>A node's picture, which the editor hands over as a <see cref="Pixmap"/>.</summary>
public sealed class ThumbnailPanel : Image
{
	private Pixmap _pixmap;

	public Pixmap Pixmap
	{
		get => _pixmap;
		set
		{
			if ( ReferenceEquals( _pixmap, value ) ) return;

			_pixmap = value;

			Texture = value is { IsValid: true, picture: { } picture } ? picture.ToTexture( false ) : null;
		}
	}
}
