using System.Runtime.InteropServices;

namespace Rinten.Native;

//ufbx via the rn_fbx_* shim: C walks the scene, only numbers cross; positions in unscaled metres
internal static unsafe partial class Ufbx
{
	private const string Library = "rintencore";

	/// <summary>What <see cref="rn_fbx_node_flags"/> answers with.</summary>
	[Flags]
	public enum NodeFlags
	{
		Root = 1,
		Mesh = 2,
		Bone = 4,
		Attribute = 8,
	}

	/// <summary>What <see cref="rn_fbx_mesh_attributes"/> answers with.</summary>
	[Flags]
	public enum MeshAttributes
	{
		Normals = 1,
		Tangents = 2,
		TexCoord0 = 4,
		TexCoord1 = 8,
		Colours = 16,
	}

	/// <summary>Reads a scene, or null. <paramref name="outError"/> is filled with what ufbx said when it could not.</summary>
	[LibraryImport( Library, StringMarshalling = StringMarshalling.Utf8 )]
	public static partial IntPtr rn_fbx_load( string path, byte* outError, int errorSize );

	[LibraryImport( Library )]
	public static partial void rn_fbx_free( IntPtr scene );

	//-- nodes --------------------------------------------------------------

	[LibraryImport( Library )]
	public static partial int rn_fbx_node_count( IntPtr scene );

	/// <summary>Writes the name and answers its length, which may be more.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_node_name( IntPtr scene, int index, byte* buffer, int size );

	/// <summary>The node's parent, or -1 for a root or for the scene's own root node.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_node_parent( IntPtr scene, int index );

	/// <summary>See <see cref="NodeFlags"/>.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_node_flags( IntPtr scene, int index );

	/// <summary>Where the node rests, inverted - what stands in for a bind pose on a bone no skin cluster mentions. Row-major, translation in the last row.</summary>
	[LibraryImport( Library )]
	public static partial void rn_fbx_node_world_inverse( IntPtr scene, int index, float* out16 );

	//-- skins --------------------------------------------------------------

	[LibraryImport( Library )]
	public static partial int rn_fbx_deformer_count( IntPtr scene );

	[LibraryImport( Library )]
	public static partial int rn_fbx_cluster_count( IntPtr scene, int deformer );

	/// <summary>Which node a cluster binds to, as an index into the node list. -1 for none.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_cluster_node( IntPtr scene, int deformer, int cluster );

	/// <summary>Where the bone stood when the skin was bound, inverted - what the file actually recorded at bind time, which is the truth even when the rig has been moved since.</summary>
	[LibraryImport( Library )]
	public static partial void rn_fbx_cluster_inverse_bind( IntPtr scene, int deformer, int cluster, float* out16 );

	//-- meshes -------------------------------------------------------------

	[LibraryImport( Library )]
	public static partial int rn_fbx_part_count( IntPtr scene, int node );

	[LibraryImport( Library )]
	public static partial int rn_fbx_part_triangles( IntPtr scene, int node, int part );

	[LibraryImport( Library )]
	public static partial int rn_fbx_part_material( IntPtr scene, int node, int part, byte* buffer, int size );

	/// <summary>What a part's material says its surface is in numbers: twelve floats - base colour rgb and its factor, roughness, metalness, emission rgb and its factor, opacity, a spare. Answers with bits: bit n where the file gave the nth of those seven a value, bit n + 8 where a picture is connected to it.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_part_surface( IntPtr scene, int node, int part, float* out12 );

	/// <summary>Which skin deformer moves this mesh, or -1.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_mesh_deformer( IntPtr scene, int node );

	/// <summary>See <see cref="MeshAttributes"/>.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_mesh_attributes( IntPtr scene, int node );

	/// <summary>One material part of a mesh, triangulated, in world space.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_part_corners( IntPtr scene, int node, int part, int capacity,
		float* positions, float* normals, float* tangents,
		float* uv0, float* uv1, float* colors,
		uint* sourceVertices, int* skinClusters, float* skinWeights );

	//-- blend shapes -------------------------------------------------------

	/// <summary>Every named channel of every blend deformer on this mesh, flattened.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_shape_count( IntPtr scene, int node );

	[LibraryImport( Library )]
	public static partial int rn_fbx_shape_name( IntPtr scene, int node, int shape, byte* buffer, int size );

	[LibraryImport( Library )]
	public static partial int rn_fbx_shape_offsets( IntPtr scene, int node, int shape );

	/// <summary>Whether the shape carries normal offsets as well as positions.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_shape_has_normals( IntPtr scene, int node, int shape );

	/// <summary>One shape's sparse offsets, through the same matrices the positions went through - as directions, because a delta is a displacement and where the mesh sits does not move it. Nothing renormalises them: they are differences, not normals.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_shape_read( IntPtr scene, int node, int shape, int capacity,
		uint* vertices, float* positionOffsets, float* normalOffsets );

	//-- animation ----------------------------------------------------------

	[LibraryImport( Library )]
	public static partial int rn_fbx_stack_count( IntPtr scene );

	[LibraryImport( Library )]
	public static partial int rn_fbx_stack_name( IntPtr scene, int stack, byte* buffer, int size );

	/// <summary>The file's own timeline, in frames a second.</summary>
	[LibraryImport( Library )]
	public static partial double rn_fbx_frame_rate( IntPtr scene );

	[LibraryImport( Library )]
	public static partial double rn_fbx_stack_length( IntPtr scene, int stack );

	/// <summary>Samples nodes over a take, ten floats per frame (pos, rot xyzw, scale), local to parent, unscaled</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_sample( IntPtr scene, int stack,
		int* nodes, int* parents, int nodeCount, int frameCount,
		void* scratch, float* outTrs );

	/// <summary>How many bytes of workspace <see cref="rn_fbx_sample"/> wants.</summary>
	[LibraryImport( Library )]
	public static partial int rn_fbx_scratch_size( IntPtr scene );
}
