using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Editor;

//Checks via assembly metadata whether game code names a type, so exports can skip e.g. FFmpeg
partial class StandaloneExporter
{
	//Whether any of the game's compiled assemblies refers to any of these types,
	//by namespace and name - read from their metadata, without running anything.
	private bool GameReferencesAny( (string Namespace, string Name)[] wanted )
	{
		if ( _exportConfig.AssemblyFiles is null ) return false;

		foreach ( var file in _exportConfig.AssemblyFiles )
		{
			if ( !file.Key.EndsWith( ".dll", StringComparison.OrdinalIgnoreCase ) || file.Value is not byte[] bytes ) continue;

			try
			{
				using var reader = new PEReader( new MemoryStream( bytes ) );
				if ( !reader.HasMetadata ) continue;

				var metadata = reader.GetMetadataReader();

				foreach ( var handle in metadata.TypeReferences )
				{
					var type = metadata.GetTypeReference( handle );
					var name = metadata.GetString( type.Name );
					var space = metadata.GetString( type.Namespace );

					foreach ( var (wantedSpace, wantedName) in wanted )
					{
						if ( name == wantedName && space == wantedSpace ) return true;
					}
				}
			}
			catch ( BadImageFormatException )
			{
				//Not an assembly after all; it names nothing.
			}
		}

		return false;
	}
}
