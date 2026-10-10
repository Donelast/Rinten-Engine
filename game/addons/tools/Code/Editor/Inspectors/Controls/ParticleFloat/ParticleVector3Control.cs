namespace Editor.Inspectors;

using Rinten.UI;

/// <summary>Three <see cref="ParticleFloat"/> rows, one per axis.</summary>
[StyleSheet( "ParticleFloatControl.cs.scss" )]
[CustomEditor( typeof( ParticleVector3 ) )]
public sealed class ParticleVector3Control : BaseControl
{
	public override bool SupportsMultiEdit => true;

	public ParticleVector3Control()
	{
		ShowBackground = false;

	}

	public override void Rebuild()
	{
		DeleteChildren();

		if ( Property is null ) return;
		if ( !Property.TryGetAsObject( out var target ) ) return;

		AddAxis( target, nameof( ParticleVector3.X ), Theme.Red );
		AddAxis( target, nameof( ParticleVector3.Y ), Theme.Green );
		AddAxis( target, nameof( ParticleVector3.Z ), Theme.Blue );
	}

	protected override void OnReadOnlyChanged()
	{
		foreach ( var axis in ChildrenOfType<ParticleFloatControl>() )
			axis.ReadOnly = IsReadOnly;
	}

	void AddAxis( SerializedObject target, string name, Color color )
	{
		if ( target.GetProperty( name ) is not { } property ) return;

		var axis = AddChild<ParticleFloatControl>();

		axis.Label = name;
		axis.HighlightColor = color;
		axis.ReadOnly = IsReadOnly;
		axis.Property = property;
	}
}
