using HalfEdgeMesh;

namespace Editor.MeshEditor;

//A material plus the tint multiplied into it; the tint is per-face, keeping one draw call
internal readonly record struct MaterialPaint( Material Material, Color32 Tint )
{
	/// <summary>The material at its own colours.</summary>
	public MaterialPaint( Material material ) : this( material, Color32.White ) { }

	public bool IsValid => Material.IsValid();

	/// <summary>Whether this paints anything but the material's own colours.</summary>
	public bool IsTinted => !Tint.Equals( Color32.White );

	/// <summary>The tint as the picker and the swatch want it.</summary>
	public Color Colour => Tint.ToColor();

	/// <summary>The same paint at a different colour.</summary>
	public MaterialPaint WithTint( Color32 tint ) => this with { Tint = tint };

	//By the path rather than by reference, because the same material loaded twice is the same material to look at - and by the colour as well, which is what makes one material at two colours two things on the shelf.
	/// <summary>The same material and the same colour, judged the way the shelf judges it.</summary>
	public bool Same( MaterialPaint other )
	{
		if ( !IsValid || !other.IsValid ) return false;
		if ( !Tint.Equals( other.Tint ) ) return false;
		if ( Material == other.Material ) return true;

		var path = Material.ResourcePath;

		return !string.IsNullOrEmpty( path ) && path == other.Material.ResourcePath;
	}

	/// <summary>Puts this onto some faces of a mesh, material and colour both.</summary>
	public void Apply( PolygonMesh mesh, IEnumerable<FaceHandle> faces )
	{
		if ( mesh is null ) return;

		var list = faces as IReadOnlyCollection<FaceHandle> ?? faces?.ToArray();
		if ( list is null || list.Count == 0 ) return;

		if ( Material.IsValid() )
			mesh.AssignMaterialToFaces( list, Material );

		foreach ( var face in list )
		{
			mesh.SetFaceColor( face, Tint );
		}
	}

	/// <summary>Puts this onto one face.</summary>
	public void Apply( PolygonMesh mesh, FaceHandle face )
	{
		if ( mesh is null || !face.IsValid ) return;

		if ( Material.IsValid() )
			mesh.SetFaceMaterial( face, Material );

		mesh.SetFaceColor( face, Tint );
	}

	//A face whose corners disagree has no one colour to lift, so what comes back is the material at white - lifting it would otherwise pick one corner of several and call it the answer.
	/// <summary>What a face is painted with now, colour and all.</summary>
	public static MaterialPaint Of( MeshFace face )
	{
		if ( !face.IsValid ) return default;

		var mesh = face.Component.Mesh;

		return new MaterialPaint( mesh.GetFaceMaterial( face.Handle ),
			mesh.GetFaceColor( face.Handle ) ?? Color32.White );
	}

	//The colour after a bar, and only where there is one. A shelf saved before there were tints is a list of bare paths and reads back as materials at their own colours, which is what they were.
	/// <summary>How it is written into a cookie: the path, then the colour.</summary>
	public string ToCookie()
	{
		if ( !IsValid ) return string.Empty;

		var path = Material.ResourcePath ?? string.Empty;

		if ( string.IsNullOrEmpty( path ) ) return string.Empty;

		return IsTinted ? $"{path}|{Tint.r:x2}{Tint.g:x2}{Tint.b:x2}" : path;
	}

	//White rather than the struct's own default, which is transparent black: nothing written down means no tint, and no tint is the colour that changes nothing when a surface multiplies by it.
	/// <summary>Six hex digits as a colour, or white where they are anything else.</summary>
	public static Color32 TintFromHex( string hex )
	{
		if ( hex is not { Length: 6 } ) return Color32.White;

		const System.Globalization.NumberStyles style = System.Globalization.NumberStyles.HexNumber;

		if ( !byte.TryParse( hex[..2], style, null, out var r ) ) return Color32.White;
		if ( !byte.TryParse( hex[2..4], style, null, out var g ) ) return Color32.White;
		if ( !byte.TryParse( hex[4..], style, null, out var b ) ) return Color32.White;

		return new Color32( r, g, b );
	}

	public static MaterialPaint FromCookie( string part )
	{
		var text = part?.Trim();
		if ( string.IsNullOrEmpty( text ) ) return default;

		var tint = Color32.White;
		var bar = text.LastIndexOf( '|' );

		if ( bar >= 0 )
		{
			tint = TintFromHex( text[( bar + 1 )..] );
			text = text[..bar];
		}

		if ( string.IsNullOrEmpty( text ) ) return default;

		var asset = AssetSystem.FindByPath( text );

		//One deleted since is a slot with nothing in it rather than a reason to shuffle the rest along.
		if ( asset is null || asset.IsDeleted ) return default;

		return new MaterialPaint( asset.LoadResource( typeof( Material ) ) as Material, tint );
	}
}
