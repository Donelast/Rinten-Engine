using System.Collections.Immutable;

namespace Rinten
{
	public partial class Model
	{
		ImmutableArray<Material> _materials;

		/// <summary>Number of material groups this model has.</summary>
		public int MaterialGroupCount => core.MaterialGroupCount;

		/// <summary>Returns name of a material group at given group index.</summary>
		public string GetMaterialGroupName( int groupIndex )
		{
			if ( groupIndex < 0 || groupIndex >= MaterialGroupCount )
			{
				throw new ArgumentOutOfRangeException( "groupIndex", $"Tried to access out of range group index {groupIndex}, range is 0-{MaterialGroupCount - 1}" );
			}

			return core.MaterialGroupName( groupIndex );
		}

		/// <summary>Retrieves the index of a material group given its name.</summary>
		public int GetMaterialGroupIndex( string groupIndex )
		{

			for ( var i = 0; i < core.MaterialGroupCount; i++ )
			{
				if ( string.Equals( core.MaterialGroupName( i ), groupIndex, StringComparison.OrdinalIgnoreCase ) )
					return i;
			}

			return -1;
		}

		/// <summary>Retrieves an enumerable collection of all Materials on the meshes. This is fast, and cached. The order of these items is the same order used in ModelRenderer.Materials etc</summary>
		public ImmutableArray<Material> Materials
		{
			get
			{
				if ( _materials.IsDefault )
				{
					//The core names a material per mesh and loads it by name, in the order the meshes are in.
					_materials = Enumerable.Range( 0, core.MeshCount )
							.Select( i => Material.Load( core.GroupMaterial( -1, i ) ) )
							.ToImmutableArray();
				}

				return _materials;
			}
		}

		/// <summary>Retrieves an enumerable collection of Materials belonging to a specified group.</summary>
		public IEnumerable<Material> GetMaterials( int groupIndex )
		{
			if ( groupIndex < 0 || groupIndex >= MaterialGroupCount )
			{
				throw new ArgumentOutOfRangeException( "groupIndex", $"Tried to access out of range group index {groupIndex}, range is 0-{MaterialGroupCount - 1}" );
			}

					//One per mesh, and the mesh's own where the group says nothing about it - which the core already works out.
					for ( var i = 0; i < core.MeshCount; ++i )
					{
						yield return Material.Load( core.GroupMaterial( groupIndex, i ) );
					}

					yield break;
		}

		/// <summary>Retrieves an enumerable collection of Materials belonging to a specified group.</summary>
		public IEnumerable<Material> GetMaterials( string groupName )
		{
			var groupIndex = GetMaterialGroupIndex( groupName );

			if ( groupIndex < 0 )
			{
				throw new ArgumentException( "groupName", $"The group name '{groupName}' does not exist." );
			}

			return GetMaterials( groupIndex );
		}

		/// <summary>Used to mark a property as a material group, for the editor</summary>
		public sealed class MaterialGroupAttribute : System.Attribute
		{
			public string ModelParameter { get; set; } = "Model";
		}

		/// <summary>Used to mark a property as a material material override dictionary, for the editor</summary>
		public sealed class MaterialOverrideAttribute : System.Attribute
		{
			public string ModelParameter { get; set; } = "Model";
		}
	}
}
