using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Editor;

//Desktop builds ship materials' compiled blocks instead of pictures; unnamed pictures ship as-is
partial class StandaloneExporter
{
	//A picture the build carries compiled, by its path on disk, and the blocks that stand in for it.
	private readonly Dictionary<string, List<string>> compiledPictures = new( StringComparer.Ordinal );

	//Compiles the pictures the build's materials read into the blocks they are read as, and puts those in the manifest in place of the picture.
	private async Task CompilePictures()
	{
		compiledPictures.Clear();

		//Web and Android ship Basis blocks when the encoder is available, otherwise the raw pictures
		var universal = ( Platform.IsWeb || Platform.IsAndroid ) && BasisEncoder.Available;

		if ( Platform.IsWeb && !universal )
			Logger.Warning( "this editor's rintencore has no Basis encoder - the build carries its pictures, and the page compiles them; rebuild it with ./build.sh linux" );

		if ( !Platform.ShipsCompiledPictures && !universal ) return;

		var engineDir = Environment.CurrentDirectory;
		var baseAssets = BaseAssetsFolder( engineDir );

		string BaseName( string file ) => Path.GetRelativePath( baseAssets, file ).Replace( '\\', '/' );

		//The materials the build carries: the project's, and the base addon's, which every game carries.
		var materials = PackageManifest.Assets
			.Where( file => file.Name.EndsWith( ".mat", StringComparison.OrdinalIgnoreCase ) )
			.Select( file => file.Name )
			.Concat( BaseContent( engineDir )
				.Where( file => file.EndsWith( ".mat", StringComparison.OrdinalIgnoreCase ) )
				.Select( BaseName ) )
			.ToList();

		//Every picture those read, and how. On the main thread, where a material is
		//read. A reading is colour or data, and for a colour cut by a mask, which mask.
		var readings = new Dictionary<string, HashSet<(bool Srgb, string Cut)>>( StringComparer.OrdinalIgnoreCase );

		await OnMainThread( () =>
		{
			foreach ( var material in materials )
			{
				foreach ( var (picture, srgb, cut) in CoreSceneRenderer.PicturesOf( CoreSceneRenderer.ReadMaterial( material ) ) )
				{
					var name = picture.Replace( '\\', '/' ).TrimStart( '/' );

					if ( !readings.TryGetValue( name, out var how ) )
						readings[name] = how = new HashSet<(bool, string)>();

					how.Add( (srgb, cut) );
				}
			}
		} );

		//The pictures the build carries that a material reads: the project's, by the name the manifest gives them, and the base addon's.
		var pictures = PackageManifest.Assets
			.Where( file => file.AbsolutePath is not null && Rinten.TextureLoader.Image.IsAppropriate( file.Name ) )
			.Select( file => (Name: file.Name, Absolute: file.AbsolutePath) )
			.Concat( BaseContent( engineDir )
				.Where( file => Rinten.TextureLoader.Image.IsAppropriate( file ) )
				.Select( file => (Name: BaseName( file ), Absolute: file) ) )
			.Where( picture => readings.ContainsKey( picture.Name ) )
			.ToList();

		var failed = new List<string>();
		var kept = new List<string>();

		await Task.Run( () =>
		{
			foreach ( var (name, absolute) in pictures )
			{
				var blocks = new List<string>();

				foreach ( var (srgb, cut) in readings[name] )
				{
					var blocksPath = CompiledPaths.TextureBlocks( absolute, srgb, cut );
					var compiled = universal ? CompiledPaths.Universal( blocksPath ) : CompiledPaths.Compressed( blocksPath );

					//The mask a colour is cut by, where the material names one - folded in as it is compiled, the way the renderer does.
					var cutFull = cut is null ? null : CoreModel.ResolveOnDisk( cut.Replace( '\\', '/' ) );
					if ( cut is not null && cutFull is null ) break;

					if ( universal )
					{
						//The levels as the engine makes them - cut, brightened, halved with a cut's coverage kept - packed by Basis instead of into BC.
						if ( CorePictures.IsStale( absolute, compiled, cutFull ) || CoreUniversalTexture.Load( compiled ) is null )
						{
							var levels = CoreTextureFile.CompileLevels( absolute, cutFull );
							if ( levels is null ) break;

							//Its settings keep it out of blocks: it travels as itself.
							if ( levels.Count == 0 )
							{
								kept.Add( name );
								break;
							}

							var ktx2 = BasisEncoder.EncodeUastc( levels, srgb );
							if ( ktx2 is null ) break;

							File.WriteAllBytes( compiled, ktx2 );
						}

						blocks.Add( compiled );
						continue;
					}

					//Compiled again where it is older than the picture or its mask, or in a format this engine no longer reads - which its age does not show.
					var fresh = !CorePictures.IsStale( absolute, compiled, cutFull ) && CoreTextureFile.Load( compiled ) is not null;

					if ( !fresh && !CoreTextureFile.Compile( absolute, compiled, srgb, generateMips: true, compress: true, cutFull ) )
						break;

					blocks.Add( compiled );
				}

				if ( blocks.Count == readings[name].Count )
					compiledPictures[absolute] = blocks;
				else if ( !kept.Contains( name ) )
					failed.Add( name );
			}
		} );

		//Said, and the picture kept: a build that compiles it where it is drawn is better than one that cannot draw it at all.
		foreach ( var name in failed )
			Logger.Warning( $"{name} would not compile - the build carries the picture, and compiles it where it is drawn" );

		//The project's pictures: their blocks in the manifest in place of them, named from the picture's own name, the way the engine looks for them.
		foreach ( var file in PackageManifest.Assets.ToList() )
		{
			if ( file.AbsolutePath is null || !compiledPictures.TryGetValue( file.AbsolutePath, out var blocks ) ) continue;

			PackageManifest.Assets.Remove( file );

			foreach ( var compiled in blocks )
				await PackageManifest.AddFile( compiled, file.Name + compiled[file.AbsolutePath.Length..] );
		}

		Logger.Info( $"{compiledPictures.Count} picture(s) carried as their compiled blocks" );
	}

	//What a base addon file is carried as: the blocks a picture was compiled into, or the file itself.
	private IEnumerable<string> ShippedAs( string file )
		=> compiledPictures.TryGetValue( file, out var blocks ) ? blocks : [file];
}
