namespace Rinten;

/// <summary>Indicates that this type should generate meta data. Tagging your asset with this will mean that the .asset file is automatically generated - which means you don't have to do that.</summary>
[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct )]
public class AutoGenerateAttribute : System.Attribute
{
}

/// <summary>Overrides the auto generated FGD type.</summary>
[AttributeUsage( AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property )]
public class FGDTypeAttribute : AssetPathAttribute
{
	string assetType;
	public string Type;
	public string Editor;

	public FGDTypeAttribute( string type, string editor = "", string editorArgs = "" )
	{
		if ( editorArgs == "png" ) editorArgs = "jpg";
		if ( editorArgs == "jpeg" ) editorArgs = "jpg";
		if ( editorArgs == "image" ) editorArgs = "jpg";
		if ( editorArgs == "tga" ) editorArgs = "jpg";

		Type = type;
		Editor = string.IsNullOrEmpty( editor ) ? "" : $"{editor}({editorArgs})";

		assetType = type;
	}


	public override string AssetTypeExtension => assetType;
}

/// <summary>Allows you to specify a string property as a resource type. This will give the property a resource finder. Type should be the file extension, ie "vmdl"</summary>
public class ResourceTypeAttribute : FGDTypeAttribute
{
	public ResourceTypeAttribute( string type ) : base( $"resource:{type}", "AssetBrowse", type )
	{
	}
}

//TODO: Once this is no longer used in assets it can be deleted. It should be derived from [System.Flags]
/// <summary>This choices type is bitflags, so we should be able to choose more than one option at a time.</summary>
[AttributeUsage( AttributeTargets.Property )]
public class BitFlagsAttribute : FGDTypeAttribute
{
	public BitFlagsAttribute() : base( "flags", "BitFlags" )
	{
	}
}

/// <summary>When added to a string property, will becomes a selector for AssetTypeExtension</summary>
public abstract class AssetPathAttribute : System.Attribute
{
	public abstract string AssetTypeExtension { get; }
}

/// <summary>When added to a string property, will become an image string selector</summary>
[AttributeUsage( AttributeTargets.Property )]
public class ImageAssetPathAttribute : AssetPathAttribute
{
	public override string AssetTypeExtension => "jpg";
}

/// <summary>When added to a string property, will become a file picker for the given extension (or all by default)</summary>
[AttributeUsage( AttributeTargets.Property )]
public class FilePathAttribute : System.Attribute
{
	public FilePathAttribute() { }
	public FilePathAttribute( string extension )
	{
		Extension = extension;
	}

	/// <summary>The extension to filter by. If empty, all files are shown. Can be a comma separated list of extensions, or a single extension.</summary>
	public string Extension { get; set; }

	/// <summary>Browse the whole disk rather than the project's assets. The property then holds the absolute path for anything picked from outside the project. Use it for files the project refers to but does not own, like an external tool.</summary>
	public bool AllowExternal { get; set; }

	/// <summary>The file is one about to be written rather than one to find, so the box names a new file instead of picking an existing one. Implies <see cref="AllowExternal"/>: there is nothing to save into the project's assets.</summary>
	public bool Saving { get; set; }
}

/// <summary>When added to a string property, will allow selection of anything that a Texture can be</summary>
[AttributeUsage( AttributeTargets.Property )]
public class TextureImagePathAttribute : System.Attribute
{

}
