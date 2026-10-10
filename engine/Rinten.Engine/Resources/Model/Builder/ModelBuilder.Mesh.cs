namespace Rinten;

public sealed partial class ModelBuilder
{
	private readonly List<Mesh> meshes = [];
	private readonly List<ulong> bodyGroups = [];
	private readonly List<string> meshGroups = [];
	private readonly Dictionary<(string, int), int> _groupChoiceToIndex = [];
	private readonly HashSet<string> _groupNames = new( StringComparer.Ordinal );
	private ulong _defaultMeshGroupMask;

	private ulong DefaultMeshGroupMask => _groupNames.Count > 0 ? _defaultMeshGroupMask : ulong.MaxValue;

	/// <summary>Adds a mesh drawn in every body group.</summary>
	public ModelBuilder AddMesh( Mesh mesh )
	{
		AddMesh( mesh, ulong.MaxValue );
		return this;
	}

	/// <summary>Adds several meshes, each drawn in every body group.</summary>
	public ModelBuilder AddMeshes( Mesh[] meshes )
	{
		AddMeshes( meshes, ulong.MaxValue );
		return this;
	}

	/// <summary>Adds a mesh to a body group choice.</summary>
	public ModelBuilder AddMesh( Mesh mesh, string groupName, int choiceIndex )
	{
		return AddMesh( mesh, 1UL << EnsureGroupChoice( groupName, choiceIndex ) );
	}

	private ModelBuilder AddMesh( Mesh mesh, ulong bodyGroupMask )
	{
		if ( mesh == null || !mesh.IsValid )
			return this;

		if ( !mesh.HasVertexBuffer )
			throw new ArgumentException( "Mesh has invalid vertex buffer" );

		meshes.Add( mesh );
		bodyGroups.Add( bodyGroupMask );
		return this;
	}

	private ModelBuilder AddMeshes( Mesh[] meshes, ulong bodyGroupMask )
	{
		if ( meshes == null || meshes.Length == 0 )
			return this;

		var numMeshes = 0;
		foreach ( var mesh in meshes )
		{
			if ( mesh == null || !mesh.IsValid )
				continue;

			if ( !mesh.HasVertexBuffer )
				throw new ArgumentException( "Mesh has invalid vertex buffer" );

			this.meshes.Add( mesh );
			numMeshes++;
		}

		if ( numMeshes == 0 )
			return this;

		bodyGroups.AddRange( Enumerable.Repeat( bodyGroupMask, numMeshes ) );
		return this;
	}

	private int EnsureGroupChoice( string name, int choice )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			throw new ArgumentException( null, nameof( name ) );

		ArgumentOutOfRangeException.ThrowIfNegative( choice );

		var key = (name, choice);
		if ( _groupChoiceToIndex.TryGetValue( key, out var index ) )
			return index;

		if ( meshGroups.Count >= 64 )
			throw new InvalidOperationException( "Total bodygroup choices exceed 64 bits." );

		index = meshGroups.Count;
		meshGroups.Add( $"{name}_@{choice}" );
		_groupChoiceToIndex[key] = index;

		if ( _groupNames.Add( name ) )
			_defaultMeshGroupMask |= 1UL << index;

		return index;
	}
}
