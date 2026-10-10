namespace Rinten;

public partial class Material
{
	internal Surface Surface
	{
		get
		{
			var index = core.SurfaceIndex;
			if ( index < 0 ) return null;

			return Surface.FindByIndex( index );
		}
	}
}
