using System;
using System.Collections.Generic;
using System.IO;

namespace Rinten;

//Built-in flat textures for unfilled material slots, under materials/default/ with no files on disk
internal static class CoreDevTextures
{
	//Where these live, as a material names them.
	internal const string Folder = "materials/default/";

	//White. A surface with no picture is painted its own colour.
	internal const string White = Folder + "white.png";

	//Facing straight out of the surface: (0,0,1) packed into bytes. What somebody means by a bald normal map. A surface
	//wearing this is lit by the geometry's own normal, which is what it was lit by before there was a slot for one.
	internal const string FlatNormal = Folder + "normal.png";

	//Black: emitting nothing.
	internal const string Black = Folder + "black.png";

	//Picked-colour paths with no file behind them; colour/ is sRGB and value/ is linear
	internal const string PickedColour = Folder + "colour/";

	internal const string PickedValue = Folder + "value/";

	//The path a slot holding this one flat colour is written as.
	internal static string PathFor( Color colour, bool srgb )
	{
		static byte Byte( float v ) => (byte)Math.Clamp( (int)MathF.Round( v * 255.0f ), 0, 255 );

		var hex = $"{Byte( colour.r ):x2}{Byte( colour.g ):x2}{Byte( colour.b ):x2}{Byte( colour.a ):x2}";

		return ( srgb ? PickedColour : PickedValue ) + hex + ".png";
	}

	//One flat colour, and whether it is read as colour or as numbers.
	private readonly record struct Flat( byte R, byte G, byte B, byte A, bool Srgb );

	//Every built-in, by the name a material knows it as. Keyed without the folder or the extension, so
	//that a project which writes the path with a leading slash or in another case still lands on one.
	private static readonly Dictionary<string, Flat> Pictures = new( StringComparer.OrdinalIgnoreCase )
	{
		["white"] = new Flat( 255, 255, 255, 255, Srgb: true ),
		["normal"] = new Flat( 128, 128, 255, 255, Srgb: false ),
		["black"] = new Flat( 0, 0, 0, 255, Srgb: false ),
	};

	//The ones already on the device, by that same name.
	private static readonly Dictionary<string, ulong> made = new( StringComparer.OrdinalIgnoreCase );

	//The same handles, to be asked about one at a time - see Owns.
	private static readonly HashSet<ulong> ours = new();

	//Whether this path is one of ours to make rather than one to read.
	internal static bool IsBuiltIn( string resourcePath ) => Key( resourcePath ) is not null;

	//Whether it is a colour somebody chose rather than one of the four.
	internal static bool IsPicked( string resourcePath ) =>
		Key( resourcePath ) is { } key && ( key.StartsWith( "colour/" ) || key.StartsWith( "value/" ) );

	//Whether a slot holds a stand-in; feature-off shader branches match it, so the sample can be skipped
	internal static bool IsStandIn( string resourcePath ) =>
		IsBuiltIn( resourcePath ) && !IsPicked( resourcePath );

	//Whether a slot holds something somebody chose to put there.
	internal static bool IsChosen( string resourcePath ) =>
		!string.IsNullOrWhiteSpace( resourcePath ) && !IsStandIn( resourcePath );

	//Whether a slot holds a real project picture (or a missing one); flat colours can be constants
	internal static bool IsPicture( string resourcePath ) =>
		!string.IsNullOrWhiteSpace( resourcePath ) && !IsBuiltIn( resourcePath );

	//The colour a shader reads for a built-in path after sRGB decode, so constants match the sample
	internal static Color? AsRead( string resourcePath )
	{
		if ( Parse( resourcePath ) is not { } found ) return null;

		var flat = found.Flat;

		var colour = new Color( flat.R / 255.0f, flat.G / 255.0f, flat.B / 255.0f, flat.A / 255.0f );

		return flat.Srgb ? colour.ToLinear() : colour;
	}

	//The one colour a built-in is, or nothing where the path names none. For anything that wants to show one without a device:
	//the material editor paints the slot itself rather than looking for a file, because there is no file - see MaterialMapPanel.
	internal static Color? Colour( string resourcePath )
	{
		if ( Parse( resourcePath ) is not { } found ) return null;

		var flat = found.Flat;

		return new Color( flat.R / 255.0f, flat.G / 255.0f, flat.B / 255.0f, flat.A / 255.0f );
	}

	//One of them on the device, built the first time it is asked for.
	internal static ulong Get( string resourcePath )
	{
		if ( Parse( resourcePath ) is not { } found ) return 0;

		var key = found.Key;

		if ( made.TryGetValue( key, out var held ) && held != 0 ) return held;

		var flat = found.Flat;

		var handle = CoreRenderDevice.CreateTextureRgba(
			stackalloc byte[] { flat.R, flat.G, flat.B, flat.A }, 1, 1, flat.Srgb );

		if ( handle != 0 )
		{
			made[key] = handle;
			ours.Add( handle );
		}

		return handle;
	}

	//A built-in path as a shared Texture, created on first use, so materials loaded as objects get it
	internal static Texture AsTexture( string resourcePath )
	{
		if ( Parse( resourcePath ) is not { } found ) return null;

		if ( textures.TryGetValue( found.Key, out var held ) && held.IsValid() ) return held;

		var flat = found.Flat;
		var made = Texture.Create( 1, 1 ).WithData( new byte[] { flat.R, flat.G, flat.B, flat.A } ).Finish();

		if ( made?.core is not null && !flat.Srgb ) made.core.RawPixels = true;

		if ( made is not null ) textures[found.Key] = made;

		return made;
	}

	private static readonly Dictionary<string, Texture> textures = new( StringComparer.OrdinalIgnoreCase );

	//Whether this handle is a shared built-in texture that nobody else may destroy
	internal static bool Owns( ulong handle ) => handle != 0 && ours.Contains( handle );

	//Destroys every one of them and forgets it, for a device that is going away. The handles are held here for the life of the
	//process, so without this a device that restarted left every entry naming nothing - the same fault as above, and the same symptom.
	internal static void Drop()
	{
		foreach ( var handle in ours ) CoreRenderDevice.DestroyTexture( handle );

		ours.Clear();
		made.Clear();
	}

	//Which of them a path names, or nothing where it names none. The folder has to match as well as
	//the name: a project is allowed a picture of its own called white.png, and it is not this one.
	private static string Key( string resourcePath ) => Parse( resourcePath )?.Key;

	//One of ours, named or picked: what it is and what to file it under.
	private static (string Key, Flat Flat)? Parse( string resourcePath )
	{
		if ( string.IsNullOrWhiteSpace( resourcePath ) ) return null;

		var path = resourcePath.Replace( '\\', '/' ).TrimStart( '/' );

		if ( !path.StartsWith( Folder, StringComparison.OrdinalIgnoreCase ) ) return null;

		var name = Path.GetFileNameWithoutExtension( path );

		//A colour somebody picked: eight hex digits under one of the two folders, which is the whole of what it takes to describe it.
		var srgb = path.StartsWith( PickedColour, StringComparison.OrdinalIgnoreCase );
		var value = path.StartsWith( PickedValue, StringComparison.OrdinalIgnoreCase );

		if ( srgb || value )
		{
			if ( !TryHex( name, out var flat, srgb ) ) return null;

			return ( ( srgb ? "colour/" : "value/" ) + name.ToLowerInvariant(), flat );
		}

		return Pictures.TryGetValue( name, out var known ) ? ( name, known ) : null;
	}

	//rrggbbaa, and nothing else.
	private static bool TryHex( string name, out Flat flat, bool srgb )
	{
		flat = default;

		if ( name is not { Length: 8 } ) return false;

		Span<byte> bytes = stackalloc byte[4];

		for ( var i = 0; i < 4; i++ )
		{
			if ( !byte.TryParse( name.AsSpan( i * 2, 2 ), System.Globalization.NumberStyles.HexNumber,
				System.Globalization.CultureInfo.InvariantCulture, out bytes[i] ) )
				return false;
		}

		flat = new Flat( bytes[0], bytes[1], bytes[2], bytes[3], srgb );

		return true;
	}
}
