using Rinten;

namespace EngineTests;

//Reads legacy text materials and Targa images so imported projects keep their textures
[TestClass]
public class ImportedContentImportTest
{
	//The shape of a material as their compiler writes it: a layer, quoted names, quoted values, and comments between the groups.
	private const string Material = """
		// THIS FILE IS AUTO-GENERATED

		Layer0
		{
			shader "shaders/complex.shader"

			//---- Color ----
			g_vColorTint "[0.500000 0.250000 0.125000 0.000000]"
			TextureColor "materials/dev/primary_white_color.tga"

			//---- Normal ----
			TextureNormal "materials/default/default_normal.tga"

			//---- Translucent ----
			F_TRANSLUCENT "1"
			g_flAlphaTestReference "0.400"
		}
		""";

	[TestMethod]
	public void MaterialMapsAndTint()
	{
		var fields = CoreImportedMaterial.Parse( Material );

		Assert.AreEqual( "materials/dev/primary_white_color.tga", fields.ColorMap );
		Assert.AreEqual( "materials/default/default_normal.tga", fields.Normal );

		Assert.AreEqual( 0.5f, fields.Tint.r, 0.001f );
		Assert.AreEqual( 0.25f, fields.Tint.g, 0.001f );
		Assert.AreEqual( 0.125f, fields.Tint.b, 0.001f );

		Assert.IsTrue( fields.Translucent, "F_TRANSLUCENT says it is see-through" );
		Assert.AreEqual( 0.4f, fields.AlphaCutoff, 0.001f );
	}

	//A material naming a shader we have nothing special for draws with the standard one rather than with nothing.
	[TestMethod]
	public void UnknownShaderFallsBackToStandard()
	{
		var fields = CoreImportedMaterial.Parse( Material );

		Assert.AreEqual( "", fields.Shader );
	}

	//Two pixels, uncompressed, 32 bit, bottom-up - the way most of the content's Targas are written.
	private static byte[] Targa( bool topDown )
	{
		var header = new byte[18];

		header[2] = 2;    //uncompressed true colour
		header[12] = 2;   //width
		header[14] = 1;   //height
		header[16] = 32;  //bits a pixel
		header[17] = (byte)( topDown ? 0x20 : 0 );

		//Written as blue, green, red, alpha.
		var pixels = new byte[]
		{
			0, 0, 255, 255,     //red
			255, 0, 0, 128,     //blue, half transparent
		};

		var file = new byte[header.Length + pixels.Length];

		header.CopyTo( file, 0 );
		pixels.CopyTo( file, header.Length );

		return file;
	}

	[TestMethod]
	public void TargaPixelsAndChannels()
	{
		using var bitmap = Bitmap.CreateFromTgaBytes( Targa( topDown: true ) );

		Assert.IsNotNull( bitmap, "a two pixel targa should read" );
		Assert.AreEqual( 2, bitmap.Width );
		Assert.AreEqual( 1, bitmap.Height );

		var pixels = bitmap.GetPixels32();

		Assert.AreEqual( 255, pixels[0].r, "first pixel is red" );
		Assert.AreEqual( 0, pixels[0].g );
		Assert.AreEqual( 0, pixels[0].b );

		Assert.AreEqual( 255, pixels[1].b, "second pixel is blue" );
		Assert.AreEqual( 128, pixels[1].a, "and half transparent" );
	}

	//A bottom-up file - which is the usual - reads the same way up as a top-down one, or every imported texture is upside down.
	[TestMethod]
	public void TargaRowOrder()
	{
		using var top = Bitmap.CreateFromTgaBytes( Targa( topDown: true ) );
		using var bottom = Bitmap.CreateFromTgaBytes( Targa( topDown: false ) );

		var a = top.GetPixels32();
		var b = bottom.GetPixels32();

		//One row, so the two orderings must agree exactly.
		Assert.AreEqual( a[0].r, b[0].r );
		Assert.AreEqual( a[1].b, b[1].b );
	}

	//Anything that is not a Targa is refused rather than read as noise.
	[TestMethod]
	public void NotATarga()
	{
		using var nothing = Bitmap.CreateFromTgaBytes( new byte[] { 1, 2, 3 } );

		Assert.IsNull( nothing );
		Assert.IsFalse( Bitmap.IsTga( System.Text.Encoding.UTF8.GetBytes( "not an image at all, just words" ) ) );
	}

	//The engine's own content, which still has plenty of theirs in it.
	private static string Content( string relative )
	{
		var game = System.Environment.GetEnvironmentVariable( EngineEnvironment.Folder );

		Assert.IsFalse( string.IsNullOrWhiteSpace( game ), "the tests need the engine path" );

		var path = System.IO.Path.Combine( game, relative.Replace( '/', System.IO.Path.DirectorySeparatorChar ) );

		Assert.IsTrue( System.IO.File.Exists( path ), $"{relative} is not where it should be" );

		return path;
	}

	//A material with no text beside it, which is every material a package brings: the compiled one
	//is the only copy, and it says the same things under the names their compiler renamed them to.
	[TestMethod]
	public void CompiledMaterialIsRead()
	{
		CoreImportedMaterial.Forget();

		var fields = CoreImportedMaterial.Read(
			Content( "download/assets/m1helmet/m1helmet.2bb189d5ac19735a.vmat_c" ) );

		Assert.IsNotNull( fields, "a compiled material should read" );

		//Their standard surface shader, which is ours as well - so nothing special is asked for.
		Assert.AreEqual( "", fields.Shader );

		Assert.AreEqual( 1.0f, fields.Roughness, 0.001f, "g_flRoughnessScaleFactor" );
		Assert.AreEqual( 1.0f, fields.Tint.r, 0.001f );
		Assert.AreEqual( 1.0f, fields.Tint.b, 0.001f );

		Assert.IsFalse( fields.Translucent );
		Assert.IsFalse( fields.TwoSided );
		Assert.IsFalse( fields.Masked );
	}

	//The flags come through the compiled form too - this one draws its back faces, and being glass makes it see-through without saying so.
	[TestMethod]
	public void CompiledMaterialFlags()
	{
		CoreImportedMaterial.Forget();

		var fields = CoreImportedMaterial.Read( Content(
			"download/assets/clothing/sunglasses/sunglasses_black/lens.8fb1afe20bbc1e1b.vmat_c" ) );

		Assert.IsNotNull( fields );

		Assert.IsTrue( fields.TwoSided, "F_RENDER_BACKFACES" );
		Assert.IsTrue( fields.Translucent, "a lens is glass" );
	}

	//A material that is neither on disk as text nor compiled is nothing, not an empty material that quietly draws grey.
	[TestMethod]
	public void MaterialThatIsNotThere()
	{
		CoreImportedMaterial.Forget();

		Assert.IsNull( CoreImportedMaterial.Read( "materials/nothing/here.vmat" ) );
		Assert.IsNull( CoreImportedMaterial.Read( "materials/nothing/here.mat" ) );
	}
}
