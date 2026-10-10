namespace Editor.MdlEditor;

using Editor.Inspectors;
using Rinten.UI;

/// <summary>Bone picker for model definition fields, using the compiled model beside the page.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( ModelDocBoneAttribute )] )]
public sealed class ModelDocBoneControl : StringDropdownControl
{
	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( "None", "" ) };

		foreach ( var name in ModelDocBones.Names() )
			options.Add( new Rinten.UI.Option( name, "device_hub", name ) );

		return options;
	}
}

//Every bone name a model definition field can hold. There is nothing to walk up from on a panel, so the names come from
//whichever model editor is open - which is the fallback the widget this replaced already had whenever it was built outside one.
static class ModelDocBones
{
	public static List<string> Names()
	{
		Model model = null;
		Asset asset = null;
		ModelDefinition definition = null;

		//The model most recently opened in a model editor - see OpenModels.
		if ( OpenModels.All.FirstOrDefault() is { } editor )
		{
			model = editor.OpenModel;
			asset = editor.ModelAsset;
			definition = editor.ModelDefinition;
		}

		var names = new List<string>();
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		void Add( string name )
		{
			if ( string.IsNullOrWhiteSpace( name ) || !seen.Add( name ) ) return;
			names.Add( name );
		}

		if ( model is { IsValid: true } )
		{
			foreach ( var bone in model.Bones?.AllBones ?? [] )
				Add( bone.Name );

			foreach ( var bone in ModelImportCatalog.ClipBoneNames( model ) )
				Add( bone );
		}

		foreach ( var bone in ModelSourceCatalog.Bones( asset, definition ) )
			Add( bone );

		foreach ( var extra in definition?.Bones ?? [] )
			Add( extra?.Name );

		names.Sort( StringComparer.OrdinalIgnoreCase );
		return names;
	}
}
