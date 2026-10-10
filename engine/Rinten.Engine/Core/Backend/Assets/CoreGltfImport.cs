using System.Runtime.InteropServices;

namespace Rinten;

//Managed glTF import (geometry, skin, clips) via GltfFile; axes and units already match ours
internal static unsafe class CoreGltfImport
{
	/// <summary>Reads a .gltf or .glb into the model builder and says how many meshes came out. Negative means the file could not be read at all.</summary>
	public static int ImportModel( CoreAssets.ModelBuilder builder, string path, float scale )
	{
		if ( builder is null ) return -1;

		var file = GltfFile.Load( path );
		if ( file is null ) return -1;

		var unitScale = scale > 0.0f ? scale : 1.0f;

		var meshes = 0;

		//The skeleton, before any geometry: a vertex's bone indices mean nothing until the bones they index
		//exist. Only the first skin is taken - a file with two skeletons is two models wearing one filename.
		var skin = file.Skins.Length > 0 ? file.Skins[0] : null;

		//Which of the builder's bones each of this file's joints is. A model is built out of several files that
		//share a rig, so a joint the builder already holds by name is that bone rather than a second copy of it.
		var jointBone = new int[skin?.Joints.Length ?? 0];

		if ( skin is not null )
		{
			Span<float> inverseBind = stackalloc float[16];
			Span<float> raw = stackalloc float[16];

			//The parent of each joint, as a place in this same list. A joint whose parent is outside the skeleton is a root as far as we care.
			var jointParent = new int[skin.Joints.Length];

			for ( var j = 0; j < skin.Joints.Length; ++j )
			{
				jointParent[j] = -1;

				var node = skin.Joints[j];
				var above = node >= 0 && node < file.Nodes.Length ? file.Nodes[node].Parent : -1;

				for ( var other = 0; other < skin.Joints.Length; ++other )
				{
					if ( skin.Joints[other] != above ) continue;

					jointParent[j] = other;
					break;
				}
			}

			//Parents before children, because a bone goes in with its parent's place in the builder and glTF does not promise the joints are listed in that order.
			foreach ( var j in ParentsFirst( jointParent ) )
			{
				var joint = skin.Joints[j];
				var parent = jointParent[j];

				if ( skin.InverseBindMatrices >= 0 )
				{
					file.ReadFloat( skin.InverseBindMatrices, j, raw );
					GltfMath.ToRowMajor( raw, unitScale, inverseBind );
				}
				else
				{
					//glTF allows leaving them out, meaning identity - a skeleton already at rest in the mesh's own space.
					GltfMath.Identity( inverseBind );
				}

				var name = joint >= 0 && joint < file.Nodes.Length ? file.Nodes[joint].Name : null;
				name ??= "bone";

				var held = builder.FindBone( name );
				if ( held >= 0 )
				{
					jointBone[j] = held;
					continue;
				}

				fixed ( float* matrix = inverseBind )
				{
					jointBone[j] = CoreModelBones.Add( builder, name,
						parent >= 0 ? jointBone[parent] : -1, matrix );
				}
			}
		}

		Span<float> world = stackalloc float[16];
		Span<float> placed = stackalloc float[3];
		Span<float> rotated = stackalloc float[3];
		Span<float> scratch3 = stackalloc float[3];
		Span<float> scratch4 = stackalloc float[4];

		//Walked as a scene rather than as a list of meshes: a mesh is placed by the node that references it, and the same mesh can be placed more than once.
		for ( var n = 0; n < file.Nodes.Length; ++n )
		{
			var node = file.Nodes[n];
			if ( node.Mesh < 0 || node.Mesh >= file.Meshes.Length ) continue;

			if ( node.Skin >= 0 )
			{
				//A skinned mesh is not placed by its node - the joints carry the placement, and applying both would move it twice.
				GltfMath.Identity( world );
			}
			else
			{
				file.WorldMatrix( n, world );
			}

			//The bone an unskinned mesh in a rigged file hangs off: the nearest joint above its node - a part
			//parented to a hand. None is none, and the mesh stays still; see ModelBuilder.BindStillVertices.
			var carrier = CoreAssets.ModelBuilder.NoBone;

			if ( skin is not null && node.Skin < 0 )
			{
				for ( var up = n; up >= 0 && carrier < 0; up = file.Nodes[up].Parent )
				{
					var j = Array.IndexOf( skin.Joints, up );
					if ( j >= 0 ) carrier = jointBone[j];
				}
			}

			foreach ( var primitive in file.Meshes[node.Mesh].Primitives )
			{
				//Only triangles. Strips and fans are rare in exports and silently reading them as triangles would produce a mangled mesh.
				if ( primitive.Mode != 4 ) continue;

				if ( !primitive.Attributes.TryGetValue( "POSITION", out var positionAccessor ) ) continue;

				var count = file.AccessorAt( positionAccessor )?.Count ?? 0;
				if ( count == 0 ) continue;

				var hasNormals = primitive.Attributes.TryGetValue( "NORMAL", out var normalAccessor );
				var hasTangents = primitive.Attributes.TryGetValue( "TANGENT", out var tangentAccessor );
				var hasUv0 = primitive.Attributes.TryGetValue( "TEXCOORD_0", out var uv0Accessor );
				var hasUv1 = primitive.Attributes.TryGetValue( "TEXCOORD_1", out var uv1Accessor );
				var hasColors = primitive.Attributes.TryGetValue( "COLOR_0", out var colorAccessor );

				var vertices = new Vertex[count];

				for ( var v = 0; v < count; ++v )
				{
					ref var vertex = ref vertices[v];

					file.ReadFloat( positionAccessor, v, scratch3 );
					GltfMath.TransformPoint( world, scratch3, placed );

					vertex.Position = GltfMath.Point( placed ) * unitScale;

					if ( hasNormals )
					{
						file.ReadFloat( normalAccessor, v, scratch3 );
						GltfMath.TransformDirection( world, scratch3, rotated );
						GltfMath.Normalise( rotated );

						vertex.Normal = GltfMath.Point( rotated );
					}

					if ( hasTangents )
					{
						file.ReadFloat( tangentAccessor, v, scratch4 );
						GltfMath.TransformDirection( world, scratch4, rotated );
						GltfMath.Normalise( rotated );

						var tangent = GltfMath.Point( rotated );

						//The fourth component is which way the bitangent goes, not a direction - so it is copied, not rotated.
						vertex.Tangent = new Vector4( tangent.x, tangent.y, tangent.z, scratch4[3] );
					}

					if ( hasUv0 )
					{
						file.ReadFloat( uv0Accessor, v, scratch3 );
						vertex.TexCoord0 = new Vector4( scratch3[0], scratch3[1], 0.0f, 0.0f );
					}

					if ( hasUv1 )
					{
						file.ReadFloat( uv1Accessor, v, scratch3 );
						vertex.TexCoord1 = new Vector4( scratch3[0], scratch3[1], 0.0f, 0.0f );
					}

					if ( hasColors )
					{
						file.ReadFloat( colorAccessor, v, scratch4 );

						vertex.Color = new Color32(
							ToByte( scratch4[0] ), ToByte( scratch4[1] ),
							ToByte( scratch4[2] ), ToByte( scratch4[3] ) );
					}
					else
					{
						vertex.Color = Color32.White;
					}
				}

				uint[] indices;

				if ( primitive.Indices >= 0 )
				{
					var indexCount = file.AccessorAt( primitive.Indices )?.Count ?? 0;
					indices = new uint[indexCount];

					for ( var i = 0; i < indexCount; ++i )
						indices[i] = file.ReadIndex( primitive.Indices, i );
				}
				else
				{
					//No index buffer means the vertices are already in order.
					indices = new uint[count];
					for ( var i = 0; i < count; ++i ) indices[i] = (uint)i;
				}

				int firstVertex, firstIndex;

				fixed ( Vertex* data = vertices )
				{
					firstVertex = builder.AddVertices( data, vertices.Length );
				}

				//Skin goes after vertices, one entry per vertex even without joints, so arrays stay index-aligned
				if ( skin is not null )
				{
					var hasJoints = primitive.Attributes.TryGetValue( "JOINTS_0", out var jointsAccessor );
					var hasWeights = primitive.Attributes.TryGetValue( "WEIGHTS_0", out var weightsAccessor );

					var skinBones = new byte[count * 4];
					var skinWeights = new byte[count * 4];

					for ( var v = 0; v < count; ++v )
					{
						if ( !hasJoints || !hasWeights )
						{
							if ( carrier >= 0 )
							{
								skinBones[v * 4] = (byte)( carrier & 0xFF );
								skinWeights[v * 4] = 255;
							}

							continue;
						}

						file.ReadFloat( jointsAccessor, v, scratch4 );

						Span<float> weightValues = stackalloc float[4];
						file.ReadFloat( weightsAccessor, v, weightValues );

						//Weights come as floats summing to one; bytes summing to 255 is the same thing at
						//a quarter the size. The largest takes the rounding error so the sum lands exactly.
						var total = 0;
						var largest = 0;

						for ( var i = 0; i < 4; ++i )
						{
							var quantised = (int)( ( weightValues[i] * 255.0f ) + 0.5f );

							var joint = (int)scratch4[i];
							var bone = joint >= 0 && joint < jointBone.Length ? jointBone[joint] : 0;

							skinBones[( v * 4 ) + i] = (byte)Math.Clamp( bone, 0, 255 );
							skinWeights[( v * 4 ) + i] = (byte)Math.Clamp( quantised, 0, 255 );

							total += skinWeights[( v * 4 ) + i];
							if ( skinWeights[( v * 4 ) + i] > skinWeights[( v * 4 ) + largest] ) largest = i;
						}

						if ( total == 0 )
						{
							//An unweighted vertex would vanish; bind it rigidly to whichever bone it names first.
							skinWeights[v * 4] = 255;
						}
						else
						{
							var corrected = skinWeights[( v * 4 ) + largest] + ( 255 - total );
							skinWeights[( v * 4 ) + largest] = (byte)Math.Clamp( corrected, 0, 255 );
						}
					}

					fixed ( byte* bones = skinBones )
					fixed ( byte* weights = skinWeights )
					{
						builder.AddSkin( bones, weights, count );
					}
				}

				fixed ( uint* data = indices )
				{
					firstIndex = builder.AddIndices( data, indices.Length );
				}

				if ( firstVertex < 0 || firstIndex < 0 ) continue;

				//Indices are relative to the primitive; the builder's buffer is shared. The mesh records where its vertices start so they stay relative.
				var material = primitive.Material >= 0 && primitive.Material < file.MaterialNames.Length
					? file.MaterialNames[primitive.Material]
					: null;

				var name = file.Meshes[node.Mesh].Name ?? node.Name;

				var added = builder.AddMesh( name ?? "mesh", material ?? "", firstIndex, indices.Length, firstVertex, vertices.Length );

				if ( primitive.Material >= 0 && primitive.Material < file.MaterialSurfaces.Length )
					builder.SetMeshSurface( added, file.MaterialSurfaces[primitive.Material] );

				++meshes;
			}
		}

		return meshes;
	}

	//The joints of a skin with every parent before its children. A bone is added to the builder with its parent's place in the builder, so
	//the parent has to be in already. glTF lists joints in whatever order the exporter wrote them, which is usually but not always that one.
	private static int[] ParentsFirst( int[] parents )
	{
		var order = new List<int>( parents.Length );
		var placed = new bool[parents.Length];

		var progress = true;

		while ( order.Count < parents.Length && progress )
		{
			progress = false;

			for ( var joint = 0; joint < parents.Length; ++joint )
			{
				if ( placed[joint] ) continue;

				var parent = parents[joint];
				if ( parent >= 0 && parent < parents.Length && !placed[parent] ) continue;

				order.Add( joint );
				placed[joint] = true;
				progress = true;
			}
		}

		//A cycle, or a parent that is not there. Whatever is left goes in as a root rather than
		//being dropped - a joint missing from the skeleton is a limb collapsed at the origin.
		for ( var joint = 0; joint < parents.Length; ++joint )
		{
			if ( !placed[joint] ) order.Add( joint );
		}

		return order.ToArray();
	}

	private static byte ToByte( float value )
		=> (byte)( ( Math.Clamp( value, 0.0f, 1.0f ) * 255.0f ) + 0.5f );

	//-- clips --------------------------------------------------------------------

	//What each node is animated by, if anything.
	private struct Animated
	{
		public GltfFile.AnimationSampler Translation;
		public GltfFile.AnimationSampler Rotation;
		public GltfFile.AnimationSampler Scale;

		public readonly bool Any => Translation is not null || Rotation is not null || Scale is not null;
	}

	//Which animation a clip is asking for: the one it names, or the one it numbers.
	private static int Named( GltfFile file, string takeName, int index )
	{
		if ( !string.IsNullOrWhiteSpace( takeName ) )
		{
			for ( var i = 0; i < file.Animations.Length; i++ )
			{
				if ( string.Equals( file.Animations[i].Name, takeName, StringComparison.OrdinalIgnoreCase ) )
					return i;
			}
		}

		return index > 0 ? index : 0;
	}

	//A name it did not bother to write comes back empty rather than made up: the caller is listing takes to pick one by hand, and an invented name is one nothing in the file would ever answer to.
	/// <summary>The animations a .gltf or .glb carries, in the order it holds them.</summary>
	public static List<string> ListTakes( string path )
	{
		var names = new List<string>();

		var file = GltfFile.Load( path );
		if ( file is null ) return names;

		foreach ( var animation in file.Animations )
			names.Add( animation.Name ?? "" );

		return names;
	}

	/// <summary>Reads one glTF animation resampled to an even frame rate; unknown take names fall back to index</summary>
	public static int ImportClip( CoreAnimation.AnimClipBuilder builder, string path, float scale, int index,
		string takeName = null )
	{
		var file = GltfFile.Load( path );
		if ( file is null ) return -1;

		var wanted = Named( file, takeName, index );

		if ( file.Animations.Length == 0 || wanted >= file.Animations.Length ) return 0;

		var animation = file.Animations[wanted];

		//A channel per path per node is what glTF allows, and the paths a node does not have keep whatever it rests at.
		var animated = new Animated[file.Nodes.Length];

		var begin = 0.0f;
		var end = 0.0f;
		var timed = false;

		foreach ( var channel in animation.Channels )
		{
			if ( channel.TargetNode < 0 || channel.TargetNode >= file.Nodes.Length ) continue;
			if ( channel.Sampler < 0 || channel.Sampler >= animation.Samplers.Length ) continue;

			var sampler = animation.Samplers[channel.Sampler];

			switch ( channel.TargetPath )
			{
				case "translation": animated[channel.TargetNode].Translation = sampler; break;
				case "rotation": animated[channel.TargetNode].Rotation = sampler; break;
				case "scale": animated[channel.TargetNode].Scale = sampler; break;

				//Weights drive morph targets, which our format does not carry yet.
				default: continue;
			}

			var input = file.AccessorAt( sampler.Input );
			if ( input?.Floats is null || input.Count == 0 ) continue;

			var first = input.Floats[0];
			var final = input.Floats[input.Count - 1];

			begin = timed ? MathF.Min( first, begin ) : first;
			end = timed ? MathF.Max( final, end ) : final;
			timed = true;
		}

		if ( !timed ) return 0;

		//The skeleton the clip is written against. A file with a skin says which nodes are joints;
		//one without - an animation exported on its own - is described by what it animates.
		var keep = new bool[file.Nodes.Length];

		if ( file.Skins.Length > 0 )
		{
			foreach ( var joint in file.Skins[0].Joints )
			{
				if ( joint >= 0 && joint < file.Nodes.Length ) keep[joint] = true;
			}
		}
		else
		{
			for ( var n = 0; n < file.Nodes.Length; ++n )
			{
				if ( animated[n].Any ) keep[n] = true;
			}
		}

		//Ancestors come too, or a bone's parent's motion would end up inside it.
		for ( var n = 0; n < file.Nodes.Length; ++n )
		{
			if ( !keep[n] ) continue;

			for ( var up = file.Nodes[n].Parent; up >= 0; up = file.Nodes[up].Parent ) keep[up] = true;
		}

		//How far a node hangs from the root, so parents can be added first whatever order the file listed them in.
		int DepthOf( int node )
		{
			var depth = 0;
			for ( var up = file.Nodes[node].Parent; up >= 0; up = file.Nodes[up].Parent ) ++depth;

			return depth;
		}

		var bones = new List<int>();

		for ( var depth = 0; ; ++depth )
		{
			var added = 0;

			for ( var n = 0; n < file.Nodes.Length; ++n )
			{
				if ( !keep[n] || DepthOf( n ) != depth ) continue;

				bones.Add( n );
				++added;
			}

			if ( added == 0 ) break;
		}

		if ( bones.Count == 0 ) return 0;

		//Where each bone's parent sits in the list above, or -1 for a root.
		var boneOf = new int[file.Nodes.Length];
		Array.Fill( boneOf, -1 );

		for ( var b = 0; b < bones.Count; ++b ) boneOf[bones[b]] = b;

		var parents = new int[bones.Count];
		Array.Fill( parents, -1 );

		for ( var b = 0; b < bones.Count; ++b )
		{
			for ( var up = file.Nodes[bones[b]].Parent; up >= 0; up = file.Nodes[up].Parent )
			{
				var bone = boneOf[up];
				if ( bone < 0 ) continue;

				parents[b] = bone;
				break;
			}

			var name = file.Nodes[bones[b]].Name;

			if ( builder.AddBone( name ?? "bone", parents[b] ) < 0 )
				return -1;
		}

		//glTF keys carry their own times and no frame rate, so the clip is resampled onto one. Thirty is what
		//most exporters key at, which makes this a copy rather than an interpolation for the usual file.
		const float rate = 30.0f;

		var unitScale = scale > 0.0f ? scale : 1.0f;
		var length = end - begin;

		var counted = Math.Floor( ( (double)length * rate ) + 0.5 ) + 1.0;
		counted = Math.Clamp( counted, 1.0, 100000.0 );

		var frameCount = (int)counted;

		builder.SetFrameRate( rate );

		var tracks = new float[bones.Count][];
		for ( var b = 0; b < bones.Count; ++b ) tracks[b] = new float[frameCount * 10];

		var local = new float[file.Nodes.Length * 16];
		var world = new float[file.Nodes.Length * 16];
		var computed = new bool[file.Nodes.Length];
		var pending = new List<int>();

		Span<float> relative = stackalloc float[16];
		Span<float> inverse = stackalloc float[16];
		Span<float> quat = stackalloc float[4];
		Span<float> scaled = stackalloc float[3];

		//Where a node stands this frame, working up to the root and remembering what it finds. The node list is not promised to hold parents before children.
		void WorldOf( int node )
		{
			pending.Clear();

			for ( var walk = node; walk >= 0 && !computed[walk]; walk = file.Nodes[walk].Parent )
				pending.Add( walk );

			for ( var i = pending.Count; i > 0; --i )
			{
				var current = pending[i - 1];
				var above = file.Nodes[current].Parent;

				if ( above >= 0 )
				{
					GltfMath.Multiply(
						world.AsSpan( above * 16, 16 ),
						local.AsSpan( current * 16, 16 ),
						world.AsSpan( current * 16, 16 ) );
				}
				else
				{
					local.AsSpan( current * 16, 16 ).CopyTo( world.AsSpan( current * 16, 16 ) );
				}

				computed[current] = true;
			}
		}

		for ( var frame = 0; frame < frameCount; ++frame )
		{
			var time = begin + ( frame / rate );

			for ( var n = 0; n < file.Nodes.Length; ++n )
			{
				var into = local.AsSpan( n * 16, 16 );

				if ( !animated[n].Any )
				{
					//Not animated at all: whatever the node rests at, matrix or TRS.
					file.LocalMatrix( n, into );
					continue;
				}

				//A node glTF animates is a TRS node by the spec, so its own values are the starting point for whichever paths have no channel.
				var trs = new GltfMath.Trs();
				var node = file.Nodes[n];

				node.Translation?.CopyTo( trs.Translation, 0 );
				node.Rotation?.CopyTo( trs.Rotation, 0 );
				node.Scale?.CopyTo( trs.Scale, 0 );

				if ( animated[n].Translation is not null ) SampleSampler( file, animated[n].Translation, time, trs.Translation );
				if ( animated[n].Rotation is not null ) SampleSampler( file, animated[n].Rotation, time, trs.Rotation );
				if ( animated[n].Scale is not null ) SampleSampler( file, animated[n].Scale, time, trs.Scale );

				GltfMath.Compose( trs, into );
			}

			Array.Clear( computed );

			for ( var b = 0; b < bones.Count; ++b )
			{
				WorldOf( bones[b] );

				world.AsSpan( bones[b] * 16, 16 ).CopyTo( relative );

				if ( parents[b] >= 0 )
				{
					WorldOf( bones[parents[b]] );

					if ( GltfMath.Invert( world.AsSpan( bones[parents[b]] * 16, 16 ), inverse ) )
					{
						GltfMath.Multiply( inverse, world.AsSpan( bones[b] * 16, 16 ), relative );
					}
				}

				var trs = GltfMath.Decompose( relative );

				var track = tracks[b].AsSpan( frame * 10, 10 );

				var position = GltfMath.Point( trs.Translation );
				track[0] = position.x * unitScale;
				track[1] = position.y * unitScale;
				track[2] = position.z * unitScale;

				GltfMath.Quat( trs.Rotation, quat );
				quat.CopyTo( track[3..] );

				GltfMath.Scale( trs.Scale, scaled );
				scaled.CopyTo( track[7..] );
			}
		}

		for ( var b = 0; b < bones.Count; ++b ) builder.SetFrames( b, tracks[b], frameCount );

		return frameCount;
	}

	//Samples one sampler at a time; cubic splines are linear between keys, tangents dropped
	private static void SampleSampler( GltfFile file, GltfFile.AnimationSampler sampler, float time, float[] into )
	{
		var input = file.AccessorAt( sampler.Input );
		var output = file.AccessorAt( sampler.Output );

		if ( input?.Floats is null || output?.Floats is null || input.Count == 0 ) return;

		var components = into.Length;

		var cubic = sampler.Interpolation == "CUBICSPLINE";
		var stride = cubic ? 3 : 1;
		var offset = cubic ? 1 : 0;

		void Read( int key, Span<float> value )
			=> file.ReadFloat( sampler.Output, ( key * stride ) + offset, value );

		var last = input.Count - 1;

		var first = input.Floats[0];
		var final = input.Floats[last];

		if ( time <= first || last == 0 )
		{
			Read( 0, into );
			return;
		}

		if ( time >= final )
		{
			Read( last, into );
			return;
		}

		//Linear search from the start. A clip resampled front to back walks this in step with itself, and animation keys number in the hundreds.
		var key = 0;
		var before = first;
		var after = final;

		for ( var i = 0; i + 1 < input.Count; ++i )
		{
			var a = input.Floats[i];
			var b = input.Floats[i + 1];

			if ( time < a || time > b ) continue;

			key = i;
			before = a;
			after = b;
			break;
		}

		Span<float> valueA = stackalloc float[4];
		Span<float> valueB = stackalloc float[4];

		Read( key, valueA );
		Read( key + 1, valueB );

		if ( sampler.Interpolation == "STEP" )
		{
			for ( var i = 0; i < components; ++i ) into[i] = valueA[i];
			return;
		}

		var span = after - before;
		var alpha = span > 1e-9f ? ( time - before ) / span : 0.0f;

		if ( components == 4 )
		{
			//A rotation: the short way round, and back onto the unit sphere.
			var dot = 0.0f;
			for ( var i = 0; i < 4; ++i ) dot += valueA[i] * valueB[i];

			var sign = dot < 0.0f ? -1.0f : 1.0f;

			var length = 0.0f;

			for ( var i = 0; i < 4; ++i )
			{
				into[i] = valueA[i] + ( ( ( valueB[i] * sign ) - valueA[i] ) * alpha );
				length += into[i] * into[i];
			}

			length = MathF.Sqrt( length );

			if ( length < 1e-8f )
			{
				into[0] = into[1] = into[2] = 0.0f;
				into[3] = 1.0f;
				return;
			}

			for ( var i = 0; i < 4; ++i ) into[i] /= length;
			return;
		}

		for ( var i = 0; i < components; ++i ) into[i] = valueA[i] + ( ( valueB[i] - valueA[i] ) * alpha );
	}
}
