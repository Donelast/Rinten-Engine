using Rinten.Mounting;
using System.Collections.Generic;
using System.Linq;

namespace Editor.TreeBrowser;

//The games mounted on this machine, as the browser sees them.
static class MountedGames
{
	/// <summary>Mounts that are installed and can actually be browsed, by title.</summary>
	public static IEnumerable<BaseGameMount> Available()
	{
		foreach ( var entry in Directory.GetAll().Where( x => x.Available ).OrderBy( x => x.Title ) )
		{
			var source = Directory.Get( entry.Ident );
			if ( source is not null )
				yield return source;
		}
	}
}
