using System.IO;

namespace Editor.MdlEditor;

/// <summary>Picker for the IK chains a model names - the limbs a graph node reaches with, listed off the graph's target model rather than typed by hand.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( AnimGraphIkChainAttribute )] )]
public class IkChainControlWidget : DropdownControlWidget<string>
{
	public override bool IsControlButton => true;

	public IkChainControlWidget( SerializedProperty property ) : base( property )
	{
	}

	protected override string GetDisplayText()
	{
		var value = SerializedProperty.GetValue<string>();
		return string.IsNullOrEmpty( value ) ? "None" : value;
	}

	protected override void OnItemSelected( object item )
	{
		var name = item is Entry entry ? entry.Value : item as string ?? "";

		PropertyStartEdit();
		SerializedProperty.SetValue( name );
		PropertyFinishEdit();
	}

	//Which of the model's lists this picker offers.
	protected virtual List<string> Chains( Model model ) => IkChainCatalog.Names( model );

	protected override IEnumerable<object> GetDropdownValues()
	{
		yield return new Entry { Value = "", Label = "None" };

		var model = IkChainCatalog.ModelOf( SerializedProperty );
		var value = SerializedProperty.GetValue<string>();
		var names = Chains( model );

		foreach ( var name in names )
			yield return new Entry { Value = name, Label = name, Icon = "polyline" };

		//A chain the model no longer has is still what this node says, and dropping it out of the list is how somebody loses it without noticing.
		if ( !string.IsNullOrWhiteSpace( value ) && !names.Contains( value, StringComparer.OrdinalIgnoreCase ) )
			yield return new Entry { Value = value, Label = $"{value}  (not on this model)", Icon = "error" };
	}
}

/// <summary>Picker for the look-at chains a model names - the runs of bones that turn together to point at something.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( AnimGraphLookChainAttribute )] )]
public class LookChainControlWidget : IkChainControlWidget
{
	public LookChainControlWidget( SerializedProperty property ) : base( property )
	{
	}

	protected override List<string> Chains( Model model ) => IkChainCatalog.LookNames( model );
}

//The IK chains a model names, for the graph half to pick from. Read off the definition open in an editor where there is one, so a
//chain added on the model page can be picked in the graph before the file has been saved, and off the .mdl beside the model otherwise.
static class IkChainCatalog
{
	public static List<string> Names( Model model )
	{
		var definition = DefinitionOf( model );
		if ( definition?.IkChains is not { Count: > 0 } chains ) return [];

		var names = new List<string>();

		foreach ( var chain in chains )
		{
			if ( string.IsNullOrWhiteSpace( chain?.Name ) ) continue;
			names.Add( chain.Name );
		}

		return names;
	}

	/// <summary>The look-at chains a model names - a different list, picked the same way.</summary>
	public static List<string> LookNames( Model model )
	{
		var definition = DefinitionOf( model );
		if ( definition?.LookAtChains is not { Count: > 0 } chains ) return [];

		var names = new List<string>();

		foreach ( var chain in chains )
		{
			if ( string.IsNullOrWhiteSpace( chain?.Name ) ) continue;
			names.Add( chain.Name );
		}

		return names;
	}

	/// <summary>Whether a graph node's chain is one the model actually has.</summary>
	public static bool Has( Model model, string name )
		=> !string.IsNullOrWhiteSpace( name )
			&& Names( model ).Contains( name, StringComparer.OrdinalIgnoreCase );

	/// <summary>The same for a look-at chain.</summary>
	public static bool HasLook( Model model, string name )
		=> !string.IsNullOrWhiteSpace( name )
			&& LookNames( model ).Contains( name, StringComparer.OrdinalIgnoreCase );

	public static ModelDefinition DefinitionOf( Model model ) => ModelGraphResolver.DefinitionOf( model );

	static bool Same( string a, string b )
	{
		if ( string.IsNullOrWhiteSpace( a ) || string.IsNullOrWhiteSpace( b ) ) return false;

		a = a.Replace( '\\', '/' );
		b = b.Replace( '\\', '/' );

		return a.Equals( b, StringComparison.OrdinalIgnoreCase )
			|| a.EndsWith( "/" + b, StringComparison.OrdinalIgnoreCase )
			|| b.EndsWith( "/" + a, StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>The model a property being edited belongs to - the graph's, or the variation's.</summary>
	public static Model ModelOf( SerializedProperty property )
	{
		var targets = property?.Parent?.Targets;

		var node = targets?.OfType<Node>().FirstOrDefault()
			?? targets?.OfType<VariationDataNode.VariationData>().FirstOrDefault()?.Owner;

		return node?.Root?.TargetModel
			?? targets?.OfType<Parameter>().FirstOrDefault()?.Graph?.TargetModel;
	}
}
