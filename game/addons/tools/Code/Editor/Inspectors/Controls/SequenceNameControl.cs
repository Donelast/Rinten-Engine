namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>Picks an animation sequence off the model the renderer beside this property holds.</summary>
[CustomEditor( typeof( string ), WithAllAttributes = [typeof( SequenceNameAttribute )] )]
public sealed class SequenceNameControl : StringDropdownControl
{
	protected override List<Rinten.UI.Option> BuildOptions()
	{
		var options = new List<Rinten.UI.Option> { new( "None", "" ) };
		var accessor = Accessor();

		foreach ( var name in accessor?.SequenceNames ?? [] )
		{
			if ( string.IsNullOrWhiteSpace( name ) ) continue;

			options.Add( new Rinten.UI.Option( name, name ) );
		}

		return options;
	}

	//The name goes onto the accessor rather than the property. A sequence is chosen on the renderer, not stored on it: the accessor drives the model,
	//and the components either side of it have to be told before and after so the change is one edit rather than a value appearing from nowhere.
	protected override void Assign( string value )
	{
		var sequence = Property?.Parent?.ParentProperty;
		var accessor = sequence?.GetValue<SkinnedModelRenderer.SequenceAccessor>();

		if ( accessor is null ) return;

		var path = $"{nameof( SkinnedModelRenderer.Sequence )}.{nameof( SkinnedModelRenderer.SequenceAccessor.Name )}";
		var targets = sequence.Parent.Targets.OfType<Component>();

		targets.DispatchPreEdited( path );
		accessor.Name = string.IsNullOrEmpty( value ) ? null : value;
		targets.DispatchEdited( path );
	}

	SkinnedModelRenderer.SequenceAccessor Accessor() =>
		Property?.Parent?.ParentProperty?.GetValue<SkinnedModelRenderer.SequenceAccessor>();
}
