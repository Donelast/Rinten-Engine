using Rinten;
using System;
using System.Linq;

namespace EngineTests;

//The fonts a window of panels draws with.
[TestClass]
[DoNotParallelize]
public class EditorFontsTests
{
	//The families an editor's panels ask for, loaded the way a tool loads them.
	[TestMethod]
	public void AToolLoadsTheFontsItsPanelsAskFor()
	{
		var root = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsNotNull( root, $"{EngineEnvironment.Folder} is not set" );

		//What a tool mounts as its content, without starting one: the engine's own folder and the base addon's assets, which is where the fonts are.
		var content = new LocalFileSystem( root ).CreateSubSystem( "/addons/base/Assets" );

		FontManager.Instance.LoadAll( content );

		var families = FontManager.Instance.FontFamilies.ToList();

		Assert.IsTrue( families.Contains( "Inter" ),
			$"the editor writes in Inter, and it was not loaded. Loaded: {string.Join( ", ", families )}" );

		Assert.IsTrue( families.Contains( "Material Icons" ),
			$"every icon in the editor is a Material Icons glyph, and it was not loaded. Loaded: {string.Join( ", ", families )}" );
	}

	//Every font's table directory fits in the file, catching fonts truncated by line-ending conversion
	[TestMethod]
	public void EveryFontIsWholeEnoughToDrawWith()
	{
		var root = Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsNotNull( root, $"{EngineEnvironment.Folder} is not set" );

		var directory = System.IO.Path.Combine( root, "addons", "base", "Assets", "fonts" );

		Assert.IsTrue( System.IO.Directory.Exists( directory ), $"no fonts at {directory}" );

		var files = System.IO.Directory.EnumerateFiles( directory )
			.Where( x => x.EndsWith( ".ttf", StringComparison.OrdinalIgnoreCase )
				|| x.EndsWith( ".otf", StringComparison.OrdinalIgnoreCase ) )
			.ToList();

		Assert.AreNotEqual( 0, files.Count, $"no fonts at {directory}" );

		var broken = files
			.Select( x => (Name: System.IO.Path.GetFileName( x ), Cut: CutTables( x )) )
			.Select( x => (x.Name, Essential: x.Cut.Where( Essential ).ToList(), x.Cut) )
			.ToList();

		var cannotDraw = broken.Where( x => x.Essential.Count > 0 ).ToList();
		var tailOnly = broken.Count( x => x.Essential.Count == 0 && x.Cut.Count > 0 );

		Assert.AreEqual( 0, cannotDraw.Count,
			"these fonts have lost a table they draw from, so they load and then draw nothing - " +
			$"which shows up as every icon becoming elided text: " +
			string.Join( ", ", cannotDraw.Select( x => $"{x.Name} ({string.Join( "/", x.Essential )})" ) ) +
			$". {tailOnly} more have lost the tail of an optional table, which is survivable but is the "	+
			"same damage: a binary that went through a line-ending conversion." );
	}

	//Tables without which a font cannot put a glyph on the screen. Everything else - substitutions,
	//variation axes, hinting - is a font that draws something slightly wrong rather than nothing at all.
	private static bool Essential( string table ) => table is
		"glyf" or "loca" or "CFF " or "CFF2" or "cmap" or "head" or "hhea" or "hmtx" or "maxp";

	//The tables whose declared extent runs past the end of the file - which is what is left of a font that has lost bytes.
	private static System.Collections.Generic.List<string> CutTables( string path )
	{
		var cut = new System.Collections.Generic.List<string>();

		using var stream = System.IO.File.OpenRead( path );
		using var reader = new System.IO.BinaryReader( stream );

		if ( stream.Length < 12 ) return cut;

		//Past the sfnt tag, which says TrueType or CFF and is not what is being checked here, to the number of tables.
		stream.Position = 4;

		var count = ReadUInt16BigEndian( reader );

		for ( var i = 0; i < count; i++ )
		{
			var entry = 12 + ( i * 16 );
			if ( entry + 16 > stream.Length ) break;

			stream.Position = entry;
			var name = System.Text.Encoding.ASCII.GetString( reader.ReadBytes( 4 ) );

			stream.Position = entry + 8;
			var offset = ReadUInt32BigEndian( reader );
			var length = ReadUInt32BigEndian( reader );

			if ( (long)offset + length > stream.Length ) cut.Add( name );
		}

		return cut;
	}

	private static ushort ReadUInt16BigEndian( System.IO.BinaryReader reader )
	{
		var b = reader.ReadBytes( 2 );
		return (ushort)( ( b[0] << 8 ) | b[1] );
	}

	private static uint ReadUInt32BigEndian( System.IO.BinaryReader reader )
	{
		var b = reader.ReadBytes( 4 );
		return (uint)( ( b[0] << 24 ) | ( b[1] << 16 ) | ( b[2] << 8 ) | b[3] );
	}
}
