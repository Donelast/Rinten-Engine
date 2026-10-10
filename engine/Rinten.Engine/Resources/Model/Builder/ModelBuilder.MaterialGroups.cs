namespace Rinten;

public sealed partial class ModelBuilder
{
	private readonly List<MaterialGroupBuilder> _materialGroups = [];

	/// <summary>Adds a named material variant and returns a builder for its materials.</summary>
	public MaterialGroupBuilder AddMaterialGroup( string name )
	{
		var builder = new MaterialGroupBuilder { Name = name };
		_materialGroups.Add( builder );
		return builder;
	}

}
