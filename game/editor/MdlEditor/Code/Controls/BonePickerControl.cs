namespace Editor.MdlEditor;

using Editor.Inspectors;

/// <summary>Base for the bone properties - the skeleton offered as one searchable list.</summary>
public abstract class BonePickerControl : StringDropdownControl
{
	protected override bool Searchable => true;

	//Names offered above the skeleton, for properties that take a mask too.
	protected virtual IEnumerable<string> Masks( Model model ) => [];

	//Names to offer when the compiled model has no skeleton yet.
	protected virtual IEnumerable<string> ExtraBones( Model model ) => [];

	//The skeleton to pick from - the graph's model, or the variation being edited.
	protected virtual Model TargetModel
	{
		get
		{
			var targets = Property?.Parent?.Targets;
			var node = targets?.OfType<Node>().FirstOrDefault()
				?? targets?.OfType<VariationDataNode.VariationData>().FirstOrDefault()?.Owner;

			return node?.Root?.TargetModel
				?? targets?.OfType<Parameter>().FirstOrDefault()?.Graph?.TargetModel;
		}
	}

	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var model = TargetModel;
		var options = new List<Rinten.UI.Option> { new( "None", "" ) };

		//A mask is not part of the skeleton, so the authored ones go above it.
		foreach ( var mask in Masks( model ) ?? [] )
		{
			if ( string.IsNullOrWhiteSpace( mask ) ) continue;

			options.Add( new Rinten.UI.Option( mask, "layers", mask ) );
		}

		foreach ( var bone in ExtraBones( model ) ?? [] )
		{
			if ( string.IsNullOrWhiteSpace( bone ) ) continue;

			options.Add( new Rinten.UI.Option( bone, "device_hub", bone ) );
		}

		foreach ( var bone in Skeleton( model ) )
		{
			if ( string.IsNullOrWhiteSpace( bone.Name ) ) continue;

			options.Add( new Rinten.UI.Option( bone.Name, "polyline", bone.Name ) { Tooltip = Chain( bone ) } );
		}

		return options;
	}

	//The model's bones, a root and then everything under it.
	static IEnumerable<BoneCollection.Bone> Skeleton( Model model )
	{
		if ( !model.IsValid() || model.Bones?.AllBones is not { } bones )
			yield break;

		foreach ( var root in bones.Where( bone => bone.Parent is null ) )
		{
			foreach ( var bone in Descend( root ) )
				yield return bone;
		}
	}

	static IEnumerable<BoneCollection.Bone> Descend( BoneCollection.Bone bone )
	{
		yield return bone;

		if ( !bone.HasChildren ) yield break;

		foreach ( var child in bone.Children )
		{
			foreach ( var under in Descend( child ) )
				yield return under;
		}
	}

	//Where a bone sits in the skeleton, for the row's tooltip.
	static string Chain( BoneCollection.Bone bone )
	{
		var names = new List<string>();

		for ( var at = bone; at is not null; at = at.Parent )
			names.Add( at.Name );

		names.Reverse();

		return string.Join( " > ", names );
	}
}

/// <summary>Picks a bone name - the skeleton comes from the graph's target model.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( AnimGraphBoneAttribute )] )]
public sealed class AnimGraphBoneControl : BonePickerControl
{
}

/// <summary>Picks a bone mask - the model's authored masks are listed above the skeleton, since a single bone works as a mask too.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( AnimGraphBoneMaskAttribute )] )]
public sealed class AnimGraphBoneMaskControl : BonePickerControl
{
	protected override IEnumerable<string> Masks( Model model ) => model.IsValid() ? model.BoneMaskNames : [];
}
