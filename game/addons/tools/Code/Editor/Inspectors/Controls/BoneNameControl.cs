namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>The bone a thing hangs off, picked out of the model's own skeleton.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( BoneNameAttribute )] )]
public sealed class BoneNameControl : StringDropdownControl
{
	//What an empty parent is called, since "" reads as a missing row.
	const string Nothing = "(the model itself)";

	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( Nothing, "" ) };

		foreach ( var name in Bones() )
			options.Add( new Rinten.UI.Option( name, name ) );

		return options;
	}

	//Every name that can be a parent here: the model's skeleton, then the definition's own bones.
	IEnumerable<string> Bones()
	{
		var definition = Definition();

		if ( definition is null ) yield break;

		var model = Model.Load( definition.ResourcePath );

		if ( model.IsValid() && model.Bones?.AllBones is { } bones )
		{
			foreach ( var bone in bones )
			{
				if ( string.IsNullOrWhiteSpace( bone.Name ) ) continue;

				yield return bone.Name;
			}
		}

		//A bone the definition adds. Not the one being edited, and not one that already hangs off it - a bone that is its
		//own grandparent is a loop, and the thing that has to notice is this list rather than the code that walks it later.
		var editing = Editing( definition );

		foreach ( var bone in definition.Bones )
		{
			if ( bone is null || string.IsNullOrWhiteSpace( bone.Name ) ) continue;
			if ( editing is not null && Reaches( definition, bone, editing ) ) continue;

			yield return bone.Name;
		}
	}

	//The added bone this property belongs to, if it is one of those.
	ModelDefinition.ExtraBone Editing( ModelDefinition definition )
	{
		var owner = Property?.Parent?.Targets?.OfType<ModelDefinition.ExtraBone>().FirstOrDefault();

		return owner is not null && definition.Bones.Contains( owner ) ? owner : null;
	}

	//Whether a bone hangs off the one being edited, however far down.
	static bool Reaches( ModelDefinition definition, ModelDefinition.ExtraBone bone, ModelDefinition.ExtraBone editing )
	{
		var seen = new HashSet<ModelDefinition.ExtraBone>();
		var at = bone;

		while ( at is not null && seen.Add( at ) )
		{
			if ( at == editing ) return true;

			var parent = at.Parent;

			at = definition.Bones.FirstOrDefault( x =>
				x is not null && string.Equals( x.Name, parent, StringComparison.OrdinalIgnoreCase ) );
		}

		return false;
	}

	//The model definition this property is somewhere inside. Walked up rather than passed in: the property being edited belongs
	//to an attachment in a list on the definition, and an attachment on its own knows nothing about the model it is part of.
	ModelDefinition Definition()
	{
		var at = Property?.Parent;

		for ( var depth = 0; at is not null && depth < 16; depth++ )
		{
			if ( at.Targets?.OfType<ModelDefinition>().FirstOrDefault() is { } definition )
				return definition;

			at = at.ParentProperty?.Parent;
		}

		return null;
	}
}
