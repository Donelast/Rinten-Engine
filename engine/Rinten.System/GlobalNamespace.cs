using System;
using System.IO;

namespace Rinten.Internal
{
	public static class GlobalSystemNamespace
	{
		public static Rinten.Diagnostics.Logger Log { get; } = new( "Generic" );

		//Avoiding the temptation to swamp this will global properties like IsServer etc - at least for now.
	}
}
