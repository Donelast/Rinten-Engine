namespace Rinten;

public partial class RenderAttributes
{
	//A set of attributes can outlive the code that filled it - SceneObject.Attributes is kept for as long as the object is - so
	//the textures it names are held here as well, to keep them from being collected while something may still draw with them.

	private Dictionary<StringToken, Texture> usedTextures; //lazy allocated

	private void SetUsedTexture( StringToken k, Texture texture )
	{
		if ( texture is null )
		{
			usedTextures?.Remove( k );
			return;
		}

		usedTextures ??= [];
		usedTextures[k] = texture;
	}
}
