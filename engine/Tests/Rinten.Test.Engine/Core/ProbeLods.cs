using Rinten.Internal;
using Rinten.Porting.SBox;
using System.IO;

namespace EngineTests;

[TestClass]
public class ProbeLodsTest
{
	[TestMethod]
	public void Dump()
	{
		var install = SBoxBaseContent.Find();
		System.Console.WriteLine( $"INSTALL {install}" );
		if ( install is null ) return;

		var found = 0;

		foreach ( var file in Directory.EnumerateFiles( install, "*.vmdl_c", SearchOption.AllDirectories ) )
		{
			CoreImportedResource resource;
			try { resource = CoreImportedResource.Open( file ); } catch { continue; }
			if ( resource?.Find( "DATA" ) is not { } block ) continue;

			CoreImportedKeyValues3 kv;
			try { kv = CoreImportedKeyValues3.Read( resource.Read( block ) ); } catch { continue; }

			var masks = kv?.Array( "m_refLODGroupMasks" );
			if ( masks is not { Count: > 0 } ) continue;

			var bits = 0;
			foreach ( var m in masks ) bits |= m.AsInt( 255 );

			//More than the nearest level in it.
			if ( ( bits & ~1 ) == 0 ) continue;
			if ( !file.Contains( "download" ) ) continue;

			var d = kv.Array( "m_lodGroupSwitchDistances" );

			System.Console.WriteLine( $"LODS {Path.GetRelativePath( install, file )} bits={bits} " +
				$"masks=[{string.Join( ",", masks.Select( x => x.AsInt( -1 ) ) )}] " +
				$"dist=[{( d is null ? "-" : string.Join( ",", d.Select( x => x.AsNumber( -1 ).ToString( "0.##" ) ) ) )}]" );

			var info = kv["m_modelInfo"];
			var lo = info?.Array( "m_vMinBounds" );
			var hi = info?.Array( "m_vMaxBounds" );
			var size = lo is { Count: 3 } && hi is { Count: 3 }
				? $"{hi[0].AsNumber() - lo[0].AsNumber():0.#}x{hi[1].AsNumber() - lo[1].AsNumber():0.#}x{hi[2].AsNumber() - lo[2].AsNumber():0.#}"
				: "?";

			System.Console.WriteLine( $"    size={size}" );

			if ( ++found >= 40 ) break;
		}

		System.Console.WriteLine( $"FOUND {found}" );
	}
}
