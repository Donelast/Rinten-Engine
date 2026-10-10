using Rinten.UI;
using System;
using System.Collections.Generic;
using System.Linq;

using UILabel = Rinten.UI.Label;

namespace Editor.MdlEditor;

//The model's materials: a picker and a thumbnail per surface the art names, or one material over the lot where the box at the top says so.
[StyleSheet( "ModelMaterialsPanel.cs.scss" )]
sealed class ModelMaterialsPanel : Panel
{
	readonly ModelDocContext _ctx;

	public ModelMaterialsPanel( ModelDocContext ctx )
	{
		_ctx = ctx;

		AddClass( "model-materials" );

		Rebuild();
	}

	void Rebuild()
	{
		DeleteChildren();

		var definition = _ctx.Definition;
		var overridden = IsOverridden( definition );

		//Qualified: Editor.Checkbox is the widget of the same name.
		var tick = AddChild<Rinten.UI.Checkbox>();
		tick.LabelText = "Override Material";
		tick.Value = overridden;
		tick.Tooltip = "Draw every surface with one material instead of the ones the art names.";
		tick.ValueChanged = SetOverride;

		if ( overridden )
		{
			AddProperty( definition, nameof( ModelDefinition.Material ), "Material" );

			AddNote( "One material over the whole model. What the art names is ignored while this is on." );
			return;
		}

		var surfaces = Surfaces();

		if ( surfaces.Count == 0 )
		{
			//With no named materials, offer the whole-model material picker directly
			AddNote( "The art names no materials, so this model is drawn plain. "
				+ "One material over the whole of it is the only way to dress it." ).AddClass( "faint" );

			//Writing a material here is what turns the override on - see IsOverridden - so the panel is built again to say so.
			AddProperty( definition, nameof( ModelDefinition.Material ), "Material", rebuild: true );
			return;
		}

		EnsureRemaps( definition, surfaces );

		var heading = AddChild<UILabel>( "heading" );
		heading.Text = "FBX Materials";

		foreach ( var surface in surfaces )
			AddChild( new ModelMaterialRow( _ctx, surface ) );
	}

	UILabel AddNote( string text )
	{
		var note = AddChild<UILabel>( "note" );
		note.Text = text;

		return note;
	}

	//Whether the model is drawn with one material rather than with the ones its art names: the same question
	//the compiler asks, which is whether the definition names one - see ModelDefinitionCompiler.LayMaterials.
	static bool IsOverridden( ModelDefinition definition )
		=> !string.IsNullOrWhiteSpace( definition.Material );

	void SetOverride( bool on )
	{
		var definition = _ctx.Definition;

		if ( on )
		{
			//The engine's own plain white, so ticking the box puts the model into one material there and then. Empty is not a material,
			//and is how the definition says to use the art's own - so a box that left it empty would read as having done nothing.
			definition.Material = ModelDefinition.PlainMaterial;
		}
		else
		{
			definition.Material = "";

			EnsureRemaps( definition, Surfaces() );
		}

		_ctx.Host.NotifyDefinitionChanged( rebuildInspector: true );
	}

	void AddProperty( object target, string name, string title, bool rebuild = false )
	{
		var heading = AddChild<UILabel>( "heading" );
		heading.Text = title;

		var so = target.GetSerialized();
		so.OnPropertyChanged += _ => _ctx.Host.NotifyDefinitionChanged( rebuildInspector: rebuild );

		if ( so.GetProperty( name ) is not { } property )
			return;

		var sheet = AddChild( new Inspectors.ControlSheet { ShowPropertyNameInTooltip = false } );
		sheet.AddPropertyRow( property );
	}

	List<string> Surfaces()
	{
		var names = ModelSourceCatalog.Materials( _ctx.Asset, _ctx.Definition ).ToList();
		if ( names.Count > 0 )
			return names;

		var meshes = _ctx.Host.CompiledModel?.MeshInfo?.Meshes;
		if ( meshes is null )
			return names;

		var seen = new HashSet<string>( names, StringComparer.OrdinalIgnoreCase );
		foreach ( var mesh in meshes )
		{
			var name = ModelImportCatalog.Bare( mesh.Name );
			if ( string.IsNullOrWhiteSpace( name ) || !seen.Add( name ) )
				continue;

			names.Add( name );
		}

		return names;
	}

	//Adds an empty remap row per unmapped surface; empty keeps the compiler's default material
	static void EnsureRemaps( ModelDefinition definition, IReadOnlyList<string> surfaces )
	{
		definition.MaterialRemaps ??= new List<ModelDefinition.SurfaceMaterial>();

		foreach ( var surface in surfaces )
		{
			if ( definition.MaterialRemaps.Any( r =>
				r is not null && string.Equals( r.Surface, surface, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			definition.MaterialRemaps.Add( new ModelDefinition.SurfaceMaterial
			{
				Surface = surface,
				Material = ""
			} );
		}
	}
}

//One surface: what it is called, what it is drawn with, and a look at it.
sealed class ModelMaterialRow : Panel
{
	public ModelMaterialRow( ModelDocContext ctx, string surface )
	{
		AddClass( "material-row" );

		var remap = Remap( ctx.Definition, surface );

		var preview = AddChild( new MaterialThumb( remap.Material ) );

		var column = AddChild<Panel>( "column" );

		var name = column.AddChild<UILabel>( "name" );
		name.Text = surface;

		var so = remap.GetSerialized();
		so.OnPropertyChanged += _ =>
		{
			preview.Path = remap.Material;
			ctx.Host.NotifyDefinitionChanged();
		};

		if ( so.GetProperty( nameof( ModelDefinition.SurfaceMaterial.Material ) ) is not { } property )
			return;

		var sheet = column.AddChild( new Inspectors.ControlSheet { ShowPropertyNameInTooltip = false } );
		sheet.AddPropertyRow( property );
	}

	static ModelDefinition.SurfaceMaterial Remap( ModelDefinition definition, string surface )
	{
		var found = definition.MaterialRemaps.Find( r =>
			r is not null && string.Equals( r.Surface, surface, StringComparison.OrdinalIgnoreCase ) );

		if ( found is not null )
			return found;

		found = new ModelDefinition.SurfaceMaterial { Surface = surface, Material = "" };
		definition.MaterialRemaps.Add( found );
		return found;
	}
}

//A look at the material a surface is drawn with, or a mark saying there is none named. The asset system's thumbnail is a texture, so this is an
//image rather than something painted: the panel wears the picture the asset browser already made instead of asking for it to be drawn again.
sealed class MaterialThumb : Panel
{
	public string Path
	{
		get => _path;
		set
		{
			if ( _path == value )
				return;

			_path = value;
			Read();
		}
	}

	string _path;

	readonly Image _thumb;
	readonly UILabel _blank;

	public MaterialThumb( string path )
	{
		_path = path;

		AddClass( "material-thumb" );

		_thumb = AddChild<Image>( "thumb" );

		_blank = AddChild<UILabel>( "material-icon blank" );
		_blank.Text = "palette";

		Read();
	}

	void Read()
	{
		var asset = string.IsNullOrWhiteSpace( _path ) ? null : AssetSystem.FindByPath( _path );

		//The asset system answers with a Pixmap, which is a bitmap the widget side blits; a panel wears a texture, so the picture is made into one.
		var thumb = asset?.GetAssetThumb( true )?.picture?.ToTexture( mips: false );

		_thumb.Texture = thumb;
		_thumb.SetClass( "hidden", thumb is null );
		_blank.SetClass( "hidden", thumb is not null );
	}
}
