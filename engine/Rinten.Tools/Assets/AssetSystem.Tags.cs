using System;

namespace Editor;

/// <summary>Handles asset tags.</summary>
public static class AssetTagSystem
{
	static AssetTagSystem()
	{
		//This is an expensive tag that nobody is using
		RegisterAssetTag( "@unreferenced", "Unreferenced", "Assets that are not referenced by other content. Be warned this doesn't account for code/ugc/scripts or shared with other mods.", null, x => { return !x.GetReferences( false ).Any(); } );

		RegisterAssetTag( "@nosource", "No Source", "Compiled assets that are missing sources.", null, x => { return x.HasCompiledFile && !x.HasSourceFile; } );

		//A "@published" tag used to stand here, for assets somebody had switched publishing on for. Nothing publishes
		//anything now, so the filter could only ever answer no - a chip that hides every asset when it is clicked.

		//The material tags that were here read compile info off a .vmat - decal, overlay, blendable, nodraw, the map editor flags. Nothing in
		//this editor has a .vmat to read, and a tag whose filter can only ever answer no is a filter chip that hides everything when it is clicked.

		//RegisterAssetTag( "@vtex_generated", "Auto Generated", "Textures that were automatically generated.", Color.FromBytes( 255, 255, 255 ), x => x.AssetType == AssetType.Texture && x.Path.IndexOf( ".generated." ) != -1 );
		RegisterAssetTag( "@model_gibs", "Gib Models", "Broken versions of models a.k.a. gibs.", null, x => x.AssetType is not null && x.AssetType == AssetType.Model && x.GetDependants( false ).Any( y => y.AssetType == AssetType.Model ) );
		RegisterAssetTag( "@model_materials", "Model Materials", "Materials used by models.", null, x => x.AssetType is not null && x.AssetType == AssetType.Material && x.GetDependants( false ).Any( y => y.AssetType == AssetType.Model ) );

		RegisterAssetTag( "@tex_sheet", "Sheet Texture", "A texture with a sheet", null, x => x.FindIntEditInfo( "sheet" ) > 0 );
		RegisterAssetTag( "@tex_sheet_animated", "Sheet Texture - Animated", "A texture with animation sheets", null, x => x.FindIntEditInfo( "sheet_animations" ) > 0 );
	}

	public delegate bool AssetAutoTagFilter( Asset asset );

	public struct TagDefinition
	{
		public string Tag { get; internal set; }
		public string Title { get; internal set; }
		public string Description { get; internal set; }
		public string Icon { get; internal set; }
		public bool AutoTag => Filter != null;
		public AssetAutoTagFilter Filter { get; internal set; }
		public Pixmap IconPixmap => GetTagIcon( Tag );
	}

	static readonly CaseInsensitiveConcurrentDictionary<TagDefinition> TagDefinitions = new();

	static Dictionary<string, Pixmap> TagIcons = new();

	/// <summary>Get an auto generated icon for given tag.</summary>
	public static Pixmap GetTagIcon( string tag )
	{
		if ( TagIcons.ContainsKey( tag ) )
			return TagIcons[tag];

		var tagDefs = All.Where( x => x.Tag == tag );
		var tagDef = tagDefs.FirstOrDefault();

		if ( !tagDefs.Any() )
		{
			tagDef.Title = tag.ToTitleCase();
		}

		if ( !string.IsNullOrEmpty( tagDef.Icon ) )
		{
			TagIcons[tag] = Pixmap.FromFile( tagDef.Icon );
			return TagIcons[tag];
		}

		TagIcons[tag] = DrawIcon( tagDef.Title );
		return TagIcons[tag];
	}

	static Color[] tagColors = new[]
	{
		Color.Parse( "#FF70A6" ) ?? default,
		Color.Parse( "#FF707E" ) ?? default,
		Color.Parse( "#E770FF" ) ?? default,
		Color.Parse( "#8370FF" ) ?? default,
		Color.Parse( "#70BAFF" ) ?? default,
		Color.Parse( "#70FFC1" ) ?? default,
		Color.Parse( "#70FF7C" ) ?? default,
		Color.Parse( "#C8FF70" ) ?? default,
		Color.Parse( "#FFF170" ) ?? default,
		Color.Parse( "#FFA270" ) ?? default,
		Color.Parse( "#FF7070" ) ?? default,
	};

	static Pixmap DrawIcon( string tag )
	{
		//get color from a hash of the tag so it's consistent across sessions
		var rand = new Random( tag.FastHash() );
		var color = rand.FromArray( tagColors );

		var pNewIcon = new Pixmap( 16, 16 );
		var r = new Rect( 0, pNewIcon.Size );
		pNewIcon.Clear( new Color( 0, 0, 0, 0 ) );

		using ( Paint.ToPixmap( pNewIcon ) )
		{
			Paint.TextAntialiasing = true;
			Paint.Antialiasing = true;
			Paint.ClearPen();
			Paint.SetBrush( color.Darken( 0.2f ) );
			Paint.DrawRect( r, 2 );

			Paint.SetPen( color.Lighten( 0.5f ) );
			Paint.SetDefaultFont( 7, 500 );
			Paint.DrawText( r.Shrink( 3, 0 ), tag[..1].ToUpper(), TextFlag.Center );
		}

		return pNewIcon;
	}

	/// <summary>List of all registered tags.</summary>
	public static IReadOnlyCollection<TagDefinition> All => (IReadOnlyCollection<TagDefinition>)TagDefinitions.Values;

	//Register a new asset tag.
	internal static void RegisterAssetTag( string tag, string title, string desc = "", string icon = null, AssetAutoTagFilter filter = null )
	{
		TagDefinitions[tag] = new TagDefinition() { Tag = tag, Title = title, Description = desc, Icon = icon, Filter = filter };
	}

	//Ensure a user-defined tag is registered, for display in UI.
	internal static void EnsureRegistered( string tag )
	{
		tag = tag.Trim();
		if ( string.IsNullOrWhiteSpace( tag ) )
			return;

		if ( TagDefinitions.ContainsKey( tag ) )
			return;

		TagDefinitions[tag] = new TagDefinition() { Tag = tag, Title = tag.ToTitleCase() };

		MainThread.Queue( () => EditorEvent.RunInterface<AssetSystem.IEventListener>( x => x.OnAssetTagsChanged() ) );
	}

	/// <summary>Return true if this tag is automatically applied</summary>
	public static bool IsAutoTag( string tag )
	{
		return All.Any( x => x.Tag == tag && x.AutoTag );
	}
}

