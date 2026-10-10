namespace Rinten;

public sealed partial class ModelBuilder
{
	private readonly List<HitboxSetBuilder> _hitboxSets = [];

	/// <summary>Adds a named hitbox set.</summary>
	public HitboxSetBuilder AddHitboxSet( string name = "default" )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		var builder = new HitboxSetBuilder { Name = name };
		_hitboxSets.Add( builder );
		return builder;
	}

}
