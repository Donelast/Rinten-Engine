// SPDX-License-Identifier: MIT
//
// ufbx, as the managed importer asks for it.
//
// The importer is C# (CoreFbxImport). What is here is the part that cannot be:
// ufbx describes a scene as a few hundred C structs whose layout moves between
// versions, and binding those by hand is a field out of place that nobody sees
// until a character is inside out. So the traversal stays in C and what crosses
// is numbers - counts, indices, flat arrays of floats.
//
// Nothing here decides anything. Which nodes are the skeleton, how a vertex is
// assembled, what a weight is rounded to, where the units come from: all of
// that is on the other side, where it can be read.
//
// Positions and offsets come across in the file's own metres, unscaled: the
// import scale is the caller's and is applied there.

#include "ufbx.h"

#include <stdlib.h>
#include <string.h>

#if defined( _WIN32 )
	#define RN_EXPORT __declspec( dllexport )
#else
	#define RN_EXPORT __attribute__( ( visibility( "default" ) ) )
#endif

//Loading

//Load options shared by mesh and animation paths so clips and models land in the same space
static ufbx_load_opts rn_fbx_opts( void )
{
	ufbx_load_opts opts;
	memset( &opts, 0, sizeof( opts ) );

	opts.target_axes = ufbx_axes_right_handed_y_up;

	//A unit is a metre, which is what ufbx measures in, so this asks it for the units we already count in.
	opts.target_unit_meters = 1.0f;

	//Convert geometry and every node, not just the root, or skinned verts and clips keep file units
	opts.space_conversion = UFBX_SPACE_CONVERSION_MODIFY_GEOMETRY;

	opts.generate_missing_normals = true;

	return opts;
}

RN_EXPORT ufbx_scene* rn_fbx_load( const char* path, char* out_error, int error_size )
{
	ufbx_load_opts opts = rn_fbx_opts();
	ufbx_error error;

	ufbx_scene* scene = ufbx_load_file( path, &opts, &error );

	if ( scene == NULL && out_error != NULL && error_size > 0 )
		ufbx_format_error( out_error, (size_t)error_size, &error );

	return scene;
}

RN_EXPORT void rn_fbx_free( ufbx_scene* scene )
{
	if ( scene != NULL ) ufbx_free_scene( scene );
}

//Copies a ufbx string out, always terminated. Returns its length in bytes, which may be more than was written.
static int rn_fbx_string( ufbx_string text, char* buffer, int size )
{
	int length = (int)text.length;

	if ( buffer != NULL && size > 0 )
	{
		int fits = length < size - 1 ? length : size - 1;
		if ( fits > 0 ) memcpy( buffer, text.data, (size_t)fits );
		buffer[fits] = 0;
	}

	return length;
}

//ufbx matrix to engine layout: row-major, translation in last row (ufbx stores columns)
static void rn_fbx_row_major( const ufbx_matrix* matrix, float* out )
{
	for ( int row = 0; row < 3; ++row )
	{
		out[( row * 4 ) + 0] = (float)matrix->cols[row].v[0];
		out[( row * 4 ) + 1] = (float)matrix->cols[row].v[1];
		out[( row * 4 ) + 2] = (float)matrix->cols[row].v[2];
		out[( row * 4 ) + 3] = 0.0f;
	}

	out[12] = (float)matrix->cols[3].v[0];
	out[13] = (float)matrix->cols[3].v[1];
	out[14] = (float)matrix->cols[3].v[2];
	out[15] = 1.0f;
}

//Nodes

RN_EXPORT int rn_fbx_node_count( const ufbx_scene* scene )
{
	return scene != NULL ? (int)scene->nodes.count : 0;
}

static const ufbx_node* rn_fbx_node( const ufbx_scene* scene, int index )
{
	if ( scene == NULL || index < 0 || (size_t)index >= scene->nodes.count ) return NULL;
	return scene->nodes.data[index];
}

RN_EXPORT int rn_fbx_node_name( const ufbx_scene* scene, int index, char* buffer, int size )
{
	const ufbx_node* node = rn_fbx_node( scene, index );
	if ( node == NULL ) return 0;

	return rn_fbx_string( node->name, buffer, size );
}

//The node's parent, or -1 for a root or for the scene's own root node.
RN_EXPORT int rn_fbx_node_parent( const ufbx_scene* scene, int index )
{
	const ufbx_node* node = rn_fbx_node( scene, index );
	if ( node == NULL || node->parent == NULL || node->parent->is_root ) return -1;

	return (int)node->parent->typed_id;
}

//1 root, 2 carries a mesh, 4 carries a bone, 8 carries any attribute.
RN_EXPORT int rn_fbx_node_flags( const ufbx_scene* scene, int index )
{
	const ufbx_node* node = rn_fbx_node( scene, index );
	if ( node == NULL ) return 0;

	return ( node->is_root ? 1 : 0 )
		| ( node->mesh != NULL ? 2 : 0 )
		| ( node->bone != NULL ? 4 : 0 )
		| ( node->attrib != NULL ? 8 : 0 );
}

//Where the node rests, inverted - what stands in for a bind pose on a bone no skin cluster mentions.
RN_EXPORT void rn_fbx_node_world_inverse( const ufbx_scene* scene, int index, float* out16 )
{
	const ufbx_node* node = rn_fbx_node( scene, index );
	if ( node == NULL || out16 == NULL ) return;

	ufbx_matrix inverse = ufbx_matrix_invert( &node->node_to_world );
	rn_fbx_row_major( &inverse, out16 );
}

//Skins

RN_EXPORT int rn_fbx_deformer_count( const ufbx_scene* scene )
{
	return scene != NULL ? (int)scene->skin_deformers.count : 0;
}

static const ufbx_skin_deformer* rn_fbx_deformer( const ufbx_scene* scene, int index )
{
	if ( scene == NULL || index < 0 || (size_t)index >= scene->skin_deformers.count ) return NULL;
	return scene->skin_deformers.data[index];
}

RN_EXPORT int rn_fbx_cluster_count( const ufbx_scene* scene, int deformer )
{
	const ufbx_skin_deformer* skin = rn_fbx_deformer( scene, deformer );
	return skin != NULL ? (int)skin->clusters.count : 0;
}

//Which node a cluster binds to, as an index into the node list. -1 for none.
RN_EXPORT int rn_fbx_cluster_node( const ufbx_scene* scene, int deformer, int cluster )
{
	const ufbx_skin_deformer* skin = rn_fbx_deformer( scene, deformer );
	if ( skin == NULL || cluster < 0 || (size_t)cluster >= skin->clusters.count ) return -1;

	const ufbx_node* node = skin->clusters.data[cluster]->bone_node;
	if ( node == NULL || node->is_root ) return -1;

	return (int)node->typed_id;
}

//Where the bone stood when the skin was bound, inverted - what the file actually
//recorded at bind time, which is the truth even when the rig has been moved since.
RN_EXPORT void rn_fbx_cluster_inverse_bind( const ufbx_scene* scene, int deformer, int cluster, float* out16 )
{
	const ufbx_skin_deformer* skin = rn_fbx_deformer( scene, deformer );
	if ( skin == NULL || cluster < 0 || (size_t)cluster >= skin->clusters.count || out16 == NULL ) return;

	ufbx_matrix inverse = ufbx_matrix_invert( &skin->clusters.data[cluster]->bind_to_world );
	rn_fbx_row_major( &inverse, out16 );
}

//Meshes

static const ufbx_mesh* rn_fbx_mesh( const ufbx_scene* scene, int index )
{
	const ufbx_node* node = rn_fbx_node( scene, index );
	return node != NULL ? node->mesh : NULL;
}

RN_EXPORT int rn_fbx_part_count( const ufbx_scene* scene, int node )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	return mesh != NULL ? (int)mesh->material_parts.count : 0;
}

RN_EXPORT int rn_fbx_part_triangles( const ufbx_scene* scene, int node, int part )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	if ( mesh == NULL || part < 0 || (size_t)part >= mesh->material_parts.count ) return 0;

	return (int)mesh->material_parts.data[part].num_triangles;
}

RN_EXPORT int rn_fbx_part_material( const ufbx_scene* scene, int node, int part, char* buffer, int size )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	if ( mesh == NULL || part < 0 || (size_t)part >= mesh->materials.count )
	{
		if ( buffer != NULL && size > 0 ) buffer[0] = 0;
		return 0;
	}

	return rn_fbx_string( mesh->materials.data[part]->name, buffer, size );
}

//One of a material's numbers into the flat array, and its two bits of the answer: bit
//`slot` where the file gave it a value, bit `slot + 8` where a picture is connected to it.
static int rn_fbx_surface_map( const ufbx_material_map* map, int slot, float* out, int components )
{
	int said = 0;

	for ( int i = 0; i < components; i++ ) out[i] = (float)map->value_vec4.v[i];

	if ( map->has_value ) said |= 1 << slot;
	if ( map->texture != NULL && map->texture_enabled ) said |= 1 << ( slot + 8 );

	return said;
}

//A part's PBR surface as 12 floats: base rgb+factor, rough, metal, emission rgb+factor, alpha, spare
RN_EXPORT int rn_fbx_part_surface( const ufbx_scene* scene, int node, int part, float* out12 )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	if ( out12 == NULL ) return 0;

	memset( out12, 0, sizeof( float ) * 12 );

	if ( mesh == NULL || part < 0 || (size_t)part >= mesh->materials.count ) return 0;

	const ufbx_material* material = mesh->materials.data[part];
	if ( material == NULL ) return 0;

	int said = 0;

	said |= rn_fbx_surface_map( &material->pbr.base_color, 0, out12 + 0, 3 );
	said |= rn_fbx_surface_map( &material->pbr.base_factor, 1, out12 + 3, 1 );
	said |= rn_fbx_surface_map( &material->pbr.roughness, 2, out12 + 4, 1 );
	said |= rn_fbx_surface_map( &material->pbr.metalness, 3, out12 + 5, 1 );
	said |= rn_fbx_surface_map( &material->pbr.emission_color, 4, out12 + 6, 3 );
	said |= rn_fbx_surface_map( &material->pbr.emission_factor, 5, out12 + 9, 1 );
	said |= rn_fbx_surface_map( &material->pbr.opacity, 6, out12 + 10, 1 );

	return said;
}

//Which skin deformer moves this mesh, as an index into the scene's list, or -1. A mesh with several is unusual and the first is the one that matters.
RN_EXPORT int rn_fbx_mesh_deformer( const ufbx_scene* scene, int node )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	if ( mesh == NULL || mesh->skin_deformers.count == 0 ) return -1;

	return (int)mesh->skin_deformers.data[0]->typed_id;
}

//1 normals, 2 tangents, 4 first uv set, 8 second uv set, 16 colours.
RN_EXPORT int rn_fbx_mesh_attributes( const ufbx_scene* scene, int node )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	if ( mesh == NULL ) return 0;

	int flags = 0;

	if ( mesh->vertex_normal.exists ) flags |= 1;
	if ( mesh->vertex_tangent.exists ) flags |= 2;
	if ( mesh->vertex_uv.exists ) flags |= 4;
	if ( mesh->uv_sets.count > 1 && mesh->uv_sets.data[1].vertex_uv.exists ) flags |= 8;
	if ( mesh->vertex_color.exists ) flags |= 16;

	return flags;
}

//Geometry-to-world: node transform if unskinned, bind pose if skinned, so bounds use world space
static int rn_fbx_geometry_to_world( const ufbx_scene* scene, int index, ufbx_matrix* out )
{
	const ufbx_node* node = rn_fbx_node( scene, index );
	if ( node == NULL || node->mesh == NULL ) return 0;

	*out = node->geometry_to_world;

	const ufbx_mesh* mesh = node->mesh;

	if ( mesh->skin_deformers.count > 0 && mesh->skin_deformers.data[0]->clusters.count > 0 )
	{
		const ufbx_skin_cluster* first = mesh->skin_deformers.data[0]->clusters.data[0];
		*out = ufbx_matrix_mul( &first->bind_to_world, &first->geometry_to_bone );
	}

	return 1;
}

//A vertex attribute, or zero where the file has none for this corner. The non-catch getters assert and abort the process: a UV set that does not
//cover every index, or a face bigger than a stack buffer, was taking the editor down while a project opened. Catching writes zero and goes on.
static ufbx_vec2 rn_fbx_vec2( const ufbx_vertex_vec2* attrib, uint32_t index )
{
	if ( attrib == NULL || !attrib->exists ) return ufbx_zero_vec2;

	ufbx_panic panic;
	memset( &panic, 0, sizeof( panic ) );

	ufbx_vec2 value = ufbx_catch_get_vertex_vec2( &panic, attrib, index );
	return panic.did_panic ? ufbx_zero_vec2 : value;
}

static ufbx_vec3 rn_fbx_vec3( const ufbx_vertex_vec3* attrib, uint32_t index )
{
	if ( attrib == NULL || !attrib->exists ) return ufbx_zero_vec3;

	ufbx_panic panic;
	memset( &panic, 0, sizeof( panic ) );

	ufbx_vec3 value = ufbx_catch_get_vertex_vec3( &panic, attrib, index );
	return panic.did_panic ? ufbx_zero_vec3 : value;
}

static ufbx_vec4 rn_fbx_vec4( const ufbx_vertex_vec4* attrib, uint32_t index )
{
	ufbx_vec4 missing;
	missing.x = missing.y = missing.z = missing.w = 1.0;

	if ( attrib == NULL || !attrib->exists ) return missing;

	ufbx_panic panic;
	memset( &panic, 0, sizeof( panic ) );

	ufbx_vec4 value = ufbx_catch_get_vertex_vec4( &panic, attrib, index );
	return panic.did_panic ? missing : value;
}

//One material part, triangulated, world space. Returns corner count, so call once to size buffers
RN_EXPORT int rn_fbx_part_corners( const ufbx_scene* scene, int node, int part, int capacity,
	float* positions, float* normals, float* tangents,
	float* uv0, float* uv1, float* colors,
	unsigned int* source_vertices, int* skin_clusters, float* skin_weights )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	if ( mesh == NULL || part < 0 || (size_t)part >= mesh->material_parts.count ) return 0;

	ufbx_matrix geometryToWorld;
	if ( !rn_fbx_geometry_to_world( scene, node, &geometryToWorld ) ) return 0;

	const ufbx_matrix normalMatrix = ufbx_matrix_for_normals( &geometryToWorld );

	const ufbx_mesh_part* piece = &mesh->material_parts.data[part];

	const ufbx_skin_deformer* skin = mesh->skin_deformers.count > 0
		? mesh->skin_deformers.data[0]
		: NULL;

	const ufbx_uv_set* second = mesh->uv_sets.count > 1 ? &mesh->uv_sets.data[1] : NULL;

	//ufbx turns any polygon into triangles for us; a quad becomes two, an n-gon becomes n-2. The scratch must be `max_face_triangles *
	//3` or ufbx panics and the process dies - a stack of 512 was that panic on any face with more than a hundred and seventy triangles.
	uint32_t stack_scratch[512];
	size_t scratchSize = (size_t)mesh->max_face_triangles * 3u;
	if ( scratchSize < 3 ) scratchSize = 3;

	uint32_t* scratch = stack_scratch;
	uint32_t* heap_scratch = NULL;

	if ( scratchSize > 512 )
	{
		heap_scratch = (uint32_t*)malloc( scratchSize * sizeof( uint32_t ) );
		if ( heap_scratch == NULL ) return 0;
		scratch = heap_scratch;
	}

	int written = 0;

	for ( size_t f = 0; f < piece->face_indices.count; ++f )
	{
		const ufbx_face face = mesh->faces.data[piece->face_indices.data[f]];

		ufbx_panic panic;
		memset( &panic, 0, sizeof( panic ) );

		uint32_t triangles = ufbx_catch_triangulate_face( &panic, scratch, scratchSize, mesh, face );
		if ( panic.did_panic || triangles == 0 ) continue;

		for ( uint32_t t = 0; t < triangles * 3; ++t )
		{
			const uint32_t index = scratch[t];
			const int at = written++;

			if ( at >= capacity ) continue;

			ufbx_vec3 raw = rn_fbx_vec3( &mesh->vertex_position, index );
			ufbx_vec3 placed = ufbx_transform_position( &geometryToWorld, raw );

			if ( positions != NULL )
			{
				positions[( at * 3 ) + 0] = (float)placed.x;
				positions[( at * 3 ) + 1] = (float)placed.y;
				positions[( at * 3 ) + 2] = (float)placed.z;
			}

			if ( normals != NULL )
			{
				ufbx_vec3 value = ufbx_zero_vec3;

				if ( mesh->vertex_normal.exists )
				{
					ufbx_vec3 n = rn_fbx_vec3( &mesh->vertex_normal, index );
					value = ufbx_transform_direction( &normalMatrix, n );
				}

				normals[( at * 3 ) + 0] = (float)value.x;
				normals[( at * 3 ) + 1] = (float)value.y;
				normals[( at * 3 ) + 2] = (float)value.z;
			}

			if ( tangents != NULL )
			{
				ufbx_vec3 value = ufbx_zero_vec3;

				if ( mesh->vertex_tangent.exists )
				{
					ufbx_vec3 g = rn_fbx_vec3( &mesh->vertex_tangent, index );
					value = ufbx_transform_direction( &geometryToWorld, g );
				}

				tangents[( at * 3 ) + 0] = (float)value.x;
				tangents[( at * 3 ) + 1] = (float)value.y;
				tangents[( at * 3 ) + 2] = (float)value.z;
			}

			if ( uv0 != NULL )
			{
				ufbx_vec2 value = rn_fbx_vec2( &mesh->vertex_uv, index );

				uv0[( at * 2 ) + 0] = (float)value.x;
				uv0[( at * 2 ) + 1] = (float)value.y;
			}

			if ( uv1 != NULL )
			{
				ufbx_vec2 value = second != NULL
					? rn_fbx_vec2( &second->vertex_uv, index )
					: ufbx_zero_vec2;

				uv1[( at * 2 ) + 0] = (float)value.x;
				uv1[( at * 2 ) + 1] = (float)value.y;
			}

			if ( colors != NULL )
			{
				ufbx_vec4 value = rn_fbx_vec4( &mesh->vertex_color, index );

				colors[( at * 4 ) + 0] = (float)value.x;
				colors[( at * 4 ) + 1] = (float)value.y;
				colors[( at * 4 ) + 2] = (float)value.z;
				colors[( at * 4 ) + 3] = (float)value.w;
			}

			const uint32_t vertex = index < mesh->vertex_indices.count
				? mesh->vertex_indices.data[index]
				: 0;

			if ( source_vertices != NULL ) source_vertices[at] = vertex;

			if ( skin_clusters != NULL || skin_weights != NULL )
			{
				int cluster[4] = { -1, -1, -1, -1 };
				float weight[4] = { 0, 0, 0, 0 };

				if ( skin != NULL && (size_t)vertex < skin->vertices.count )
				{
					const ufbx_skin_vertex sv = skin->vertices.data[vertex];
					uint32_t taken = sv.num_weights < 4 ? sv.num_weights : 4u;

					for ( uint32_t w = 0; w < taken; ++w )
					{
						if ( (size_t)( sv.weight_begin + w ) >= skin->weights.count ) break;

						const ufbx_skin_weight sw = skin->weights.data[sv.weight_begin + w];

						cluster[w] = (int)sw.cluster_index;
						weight[w] = (float)sw.weight;
					}
				}

				if ( skin_clusters != NULL ) memcpy( skin_clusters + ( at * 4 ), cluster, sizeof( cluster ) );
				if ( skin_weights != NULL ) memcpy( skin_weights + ( at * 4 ), weight, sizeof( weight ) );
			}
		}
	}

	free( heap_scratch );
	return written;
}

//Blend shapes

//Every named channel of every blend deformer on this mesh, flattened.
RN_EXPORT int rn_fbx_shape_count( const ufbx_scene* scene, int node )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	if ( mesh == NULL ) return 0;

	int count = 0;

	for ( size_t d = 0; d < mesh->blend_deformers.count; ++d )
		count += (int)mesh->blend_deformers.data[d]->channels.count;

	return count;
}

static const ufbx_blend_channel* rn_fbx_channel( const ufbx_scene* scene, int node, int shape )
{
	const ufbx_mesh* mesh = rn_fbx_mesh( scene, node );
	if ( mesh == NULL || shape < 0 ) return NULL;

	for ( size_t d = 0; d < mesh->blend_deformers.count; ++d )
	{
		const ufbx_blend_deformer* deformer = mesh->blend_deformers.data[d];

		if ( (size_t)shape < deformer->channels.count ) return deformer->channels.data[shape];
		shape -= (int)deformer->channels.count;
	}

	return NULL;
}

RN_EXPORT int rn_fbx_shape_name( const ufbx_scene* scene, int node, int shape, char* buffer, int size )
{
	const ufbx_blend_channel* channel = rn_fbx_channel( scene, node, shape );
	if ( channel == NULL ) return 0;

	return rn_fbx_string( channel->name, buffer, size );
}

RN_EXPORT int rn_fbx_shape_offsets( const ufbx_scene* scene, int node, int shape )
{
	const ufbx_blend_channel* channel = rn_fbx_channel( scene, node, shape );
	if ( channel == NULL || channel->target_shape == NULL ) return 0;

	return (int)channel->target_shape->num_offsets;
}

//Whether the shape carries normal offsets as well as positions.
RN_EXPORT int rn_fbx_shape_has_normals( const ufbx_scene* scene, int node, int shape )
{
	const ufbx_blend_channel* channel = rn_fbx_channel( scene, node, shape );
	if ( channel == NULL || channel->target_shape == NULL ) return 0;

	const ufbx_blend_shape* target = channel->target_shape;
	return target->normal_offsets.count == target->num_offsets ? 1 : 0;
}

//One shape's sparse offsets, through the same matrices the positions went through - as directions, because a delta is a displacement and where the mesh
//sits does not move it. Normal offsets go through the matrix normals go through, and nothing renormalises them: they are differences, not normals.
RN_EXPORT int rn_fbx_shape_read( const ufbx_scene* scene, int node, int shape, int capacity,
	unsigned int* vertices, float* position_offsets, float* normal_offsets )
{
	const ufbx_blend_channel* channel = rn_fbx_channel( scene, node, shape );
	if ( channel == NULL || channel->target_shape == NULL ) return 0;

	ufbx_matrix geometryToWorld;
	if ( !rn_fbx_geometry_to_world( scene, node, &geometryToWorld ) ) return 0;

	const ufbx_matrix normalMatrix = ufbx_matrix_for_normals( &geometryToWorld );

	const ufbx_blend_shape* target = channel->target_shape;
	const int hasNormals = target->normal_offsets.count == target->num_offsets;

	int count = (int)target->num_offsets;
	if ( count > capacity ) count = capacity;

	for ( int k = 0; k < count; ++k )
	{
		if ( vertices != NULL ) vertices[k] = target->offset_vertices.data[k];

		if ( position_offsets != NULL )
		{
			ufbx_vec3 offset = ufbx_transform_direction( &geometryToWorld, target->position_offsets.data[k] );

			position_offsets[( k * 3 ) + 0] = (float)offset.x;
			position_offsets[( k * 3 ) + 1] = (float)offset.y;
			position_offsets[( k * 3 ) + 2] = (float)offset.z;
		}

		if ( normal_offsets != NULL )
		{
			ufbx_vec3 offset;
			memset( &offset, 0, sizeof( offset ) );

			if ( hasNormals )
				offset = ufbx_transform_direction( &normalMatrix, target->normal_offsets.data[k] );

			normal_offsets[( k * 3 ) + 0] = (float)offset.x;
			normal_offsets[( k * 3 ) + 1] = (float)offset.y;
			normal_offsets[( k * 3 ) + 2] = (float)offset.z;
		}
	}

	return (int)target->num_offsets;
}

//Animation

RN_EXPORT int rn_fbx_stack_count( const ufbx_scene* scene )
{
	return scene != NULL ? (int)scene->anim_stacks.count : 0;
}

RN_EXPORT int rn_fbx_stack_name( const ufbx_scene* scene, int stack, char* buffer, int size )
{
	if ( scene == NULL || stack < 0 || (size_t)stack >= scene->anim_stacks.count ) return 0;

	return rn_fbx_string( scene->anim_stacks.data[stack]->name, buffer, size );
}

//The file's own timeline. An export at 30 frames a second is resampled to nothing - the frames land
//where they already are - and one at 24 or 60 keeps its own rate rather than being forced onto ours.
RN_EXPORT double rn_fbx_frame_rate( const ufbx_scene* scene )
{
	if ( scene == NULL ) return 30.0;

	double rate = scene->settings.frames_per_second;
	return rate > 0.0 ? rate : 30.0;
}

RN_EXPORT double rn_fbx_stack_length( const ufbx_scene* scene, int stack )
{
	if ( scene == NULL || stack < 0 || (size_t)stack >= scene->anim_stacks.count ) return 0.0;

	const ufbx_anim_stack* s = scene->anim_stacks.data[stack];
	return s->time_end - s->time_begin;
}

//Node's world transform at a time; walks to the root since parents may come after children
static const ufbx_matrix* rn_fbx_world_at( const ufbx_anim* anim, double time, const ufbx_node* node,
	ufbx_matrix* world, char* computed, const ufbx_node** pending )
{
	int count = 0;

	for ( const ufbx_node* walk = node; walk != NULL && computed[walk->typed_id] == 0; walk = walk->parent )
		pending[count++] = walk;

	//Deepest first in the list, so walking it backwards always has the parent ready by the time the child is reached.
	for ( int i = count; i > 0; --i )
	{
		const ufbx_node* current = pending[i - 1];

		ufbx_transform local = ufbx_evaluate_transform( anim, current, time );
		ufbx_matrix matrix = ufbx_transform_to_matrix( &local );

		world[current->typed_id] = current->parent != NULL
			? ufbx_matrix_mul( &world[current->parent->typed_id], &matrix )
			: matrix;

		computed[current->typed_id] = 1;
	}

	return &world[node->typed_id];
}

//Samples nodes per frame as 10 floats (pos, rot xyzw, scale), parent-local; caller owns scratch
RN_EXPORT int rn_fbx_sample( const ufbx_scene* scene, int stack,
	const int* nodes, const int* parents, int node_count, int frame_count,
	void* scratch, float* out_trs )
{
	if ( scene == NULL || nodes == NULL || parents == NULL || out_trs == NULL || scratch == NULL ) return 0;
	if ( stack < 0 || (size_t)stack >= scene->anim_stacks.count ) return 0;
	if ( node_count <= 0 || frame_count <= 0 ) return 0;

	const ufbx_anim_stack* take = scene->anim_stacks.data[stack];

	const size_t sceneNodes = scene->nodes.count;

	ufbx_matrix* world = (ufbx_matrix*)scratch;
	char* computed = (char*)( world + sceneNodes );
	const ufbx_node** pending = (const ufbx_node**)( computed + sceneNodes + ( 8 - ( sceneNodes % 8 ) ) );

	double rate = rn_fbx_frame_rate( scene );

	for ( int frame = 0; frame < frame_count; ++frame )
	{
		double time = take->time_begin + ( (double)frame / rate );

		memset( computed, 0, sceneNodes );

		for ( int b = 0; b < node_count; ++b )
		{
			const ufbx_node* node = rn_fbx_node( scene, nodes[b] );
			if ( node == NULL ) continue;

			ufbx_matrix here = *rn_fbx_world_at( take->anim, time, node, world, computed, pending );
			ufbx_matrix local = here;

			if ( parents[b] >= 0 )
			{
				const ufbx_node* above = rn_fbx_node( scene, nodes[parents[b]] );

				if ( above != NULL )
				{
					ufbx_matrix world_above = *rn_fbx_world_at( take->anim, time, above, world, computed, pending );
					ufbx_matrix inverse = ufbx_matrix_invert( &world_above );

					local = ufbx_matrix_mul( &inverse, &here );
				}
			}

			ufbx_transform transform = ufbx_matrix_to_transform( &local );

			float* out = out_trs + ( ( (size_t)b * frame_count + frame ) * 10 );

			out[0] = (float)transform.translation.x;
			out[1] = (float)transform.translation.y;
			out[2] = (float)transform.translation.z;

			out[3] = (float)transform.rotation.x;
			out[4] = (float)transform.rotation.y;
			out[5] = (float)transform.rotation.z;
			out[6] = (float)transform.rotation.w;

			//A mirrored rig stays mirrored rather than quietly turning right way out.
			out[7] = (float)transform.scale.x;
			out[8] = (float)transform.scale.y;
			out[9] = (float)transform.scale.z;
		}
	}

	return frame_count;
}

//How many bytes of workspace rn_fbx_sample wants.
RN_EXPORT int rn_fbx_scratch_size( const ufbx_scene* scene )
{
	if ( scene == NULL ) return 0;

	size_t nodes = scene->nodes.count;

	return (int)( ( nodes * sizeof( ufbx_matrix ) )
		+ nodes + 8
		+ ( ( nodes + 1 ) * sizeof( void* ) ) );
}
