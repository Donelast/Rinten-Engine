using System.IO;

namespace Rinten;

/// <summary>
/// What a controller looks like: a model picked by what the headset says is in
/// the hand.
/// </summary>
/// <remarks>
/// <para>
/// **Where they come from.** The models are the WebXR Input Profiles assets -
/// see models/vr/controllers/LICENSE.md in the base addon - MIT, and made for
/// exactly this: each one is authored in the controller's grip space, the same
/// place OpenXR's grip pose and WebXR's gripSpace put a hand, so it goes on the
/// hand as it is. A profile is named the way that project names it, and those
/// names are what a page reports; an OpenXR interaction profile is translated
/// into one here.
/// </para>
/// <para>
/// **What they are.** Ordinary models, one per hand, with a morph per moving part
/// baked from the profile's own pressed poses - see <see cref="Morphs"/> and
/// VRModelRenderer, which pushes them by what the hand is doing.
/// </para>
/// <para>
/// **What ships.** Not every profile: a page downloads everything a build carries,
/// so only the controllers a standalone headset or a browser holds travel - Meta's
/// and Pico's. Anything else asks for its fallbacks in the order the profile lists
/// give them, and ends at Quest's Touch Plus, so a hand always has a controller in
/// it. The desktop controllers are what a runtime can hand over itself.
/// </para>
/// </remarks>
internal static class XrControllerModels
{
	//Where the models are, in the base addon.
	internal const string Folder = "models/vr/controllers";

	//The morphs every controller model carries, by what moves them. A button is pushed from nought
	//to one; the stick's two axes are a morph each way, so a stick at rest is all four at nought.
	internal static class Morphs
	{
		internal const string Trigger = "trigger";
		internal const string Grip = "grip";
		internal const string StickPress = "stick_press";
		internal const string StickLeft = "stick_left";
		internal const string StickRight = "stick_right";
		internal const string StickUp = "stick_up";
		internal const string StickDown = "stick_down";
		internal const string ButtonA = "button_a";
		internal const string ButtonB = "button_b";
	}

	//What a hand is given when nothing closer is here.
	internal const string Fallback = "meta-quest-touch-plus";

	//Every profile a page can say, in the order of the number it says it with - see ProfileCodes
	//in webxr.js, which must list the same names at the same places. Nought is none.
	internal static readonly string[] WebProfiles =
	[
		null,
		"meta-quest-touch-plus",
		"meta-quest-touch-pro",
		"oculus-touch-v3",
		"oculus-touch-v2",
		"oculus-touch",
		"valve-index",
		"htc-vive",
		"htc-vive-cosmos",
		"htc-vive-focus-3",
		"microsoft-mixed-reality",
		"samsung-odyssey",
		"hp-mixed-reality",
		"pico-4",
		"pico-neo3",
		"generic-trigger",
		"generic-trigger-squeeze",
		"generic-trigger-squeeze-thumbstick",
		"generic-trigger-touchpad",
		"generic-hand-select",
		"generic-hand",
	];

	//An OpenXR interaction profile, as the profile the models are named by.
	private static readonly Dictionary<string, string> OpenXrProfiles = new( StringComparer.Ordinal )
	{
		["/interaction_profiles/meta/touch_controller_plus"] = "meta-quest-touch-plus",
		["/interaction_profiles/facebook/touch_controller_pro"] = "meta-quest-touch-pro",
		//Every Touch there has been says this one. The Quest 2's is the newest that is only ever this; a Quest 3 on a runtime with Touch Plus says that.
		["/interaction_profiles/oculus/touch_controller"] = "oculus-touch-v3",
		["/interaction_profiles/valve/index_controller"] = "valve-index",
		["/interaction_profiles/htc/vive_controller"] = "htc-vive",
		["/interaction_profiles/htc/vive_focus3_controller"] = "htc-vive-focus-3",
		["/interaction_profiles/microsoft/motion_controller"] = "microsoft-mixed-reality",
		["/interaction_profiles/hp/mixed_reality_controller"] = "hp-mixed-reality",
		["/interaction_profiles/bytedance/pico4_controller"] = "pico-4",
		["/interaction_profiles/khr/simple_controller"] = "generic-trigger",
	};

	//What stands in for a profile that does not ship, nearest first - the profile lists' own fallbacks, cut down to the ones that do.
	private static readonly Dictionary<string, string[]> Nearest = new( StringComparer.Ordinal )
	{
		["oculus-touch-v2"] = ["oculus-touch-v3"],
		["oculus-touch"] = ["oculus-touch-v3"],
		["pico-neo3"] = ["pico-4"],
	};

	//The profile an OpenXR interaction profile is, or null where it is none this knows.
	internal static string ForInteractionProfile( string path )
		=> path is not null && OpenXrProfiles.TryGetValue( path, out var profile ) ? profile : null;

	//The profile a page's number says, or null.
	internal static string ForWebCode( int code )
		=> code > 0 && code < WebProfiles.Length ? WebProfiles[code] : null;

	//The model for what is in a hand - nought left, one right. Null only when not even the fallback will load.
	internal static Model For( string profile, int hand )
	{
		foreach ( var candidate in Candidates( profile ) )
		{
			//On a page the controllers are not fetched before the game starts - a hand holds one of the eight - but when
			//the headset first says which: nothing this frame, the model on a later one. See WebContent.RequestUnder.
			if ( WebContent.RequestUnder( $"{Folder}/{candidate}" ) ) return null;

			var model = Model.Load( $"{Folder}/{candidate}/{Side( hand )}.mdl" );
			if ( model is { IsError: false } ) return model;
		}

		return null;
	}

	//The profile, the ones nearest it, and the fallback.
	private static IEnumerable<string> Candidates( string profile )
	{
		if ( !string.IsNullOrEmpty( profile ) )
		{
			yield return profile;

			if ( Nearest.TryGetValue( profile, out var near ) )
				foreach ( var other in near ) yield return other;
		}

		if ( profile != Fallback ) yield return Fallback;
	}

	private static string Side( int hand ) => hand == 0 ? "left" : "right";

	//The materials FromGlb made. A model keeps a material's name and finds it by that when it
	//is drawn, and the index it is found in holds it weakly - so these are what keeps them.
	private static readonly List<Material> kept = new();

	//A model out of a GLB in memory, textures and all: every triangle mesh where its node puts it, each material as the standard shader with
	//the file's colour picture. For the models a runtime hands over itself, which arrive as bytes while playing and have no .mdl to be.
	internal static Model FromGlb( byte[] bytes, string name )
	{
		var file = GltfFile.Load( bytes );
		if ( file is null ) return null;

		var materials = new Material[file.MaterialNames.Length];

		for ( var m = 0; m < materials.Length; m++ )
			materials[m] = MaterialOf( file, m, name );

		var builder = Model.Builder.WithName( name ).WithoutCollision();
		var any = false;

		Span<float> world = stackalloc float[16];
		Span<float> read = stackalloc float[4];
		Span<float> moved = stackalloc float[3];

		for ( var n = 0; n < file.Nodes.Length; n++ )
		{
			var node = file.Nodes[n];
			if ( node.Mesh < 0 || node.Mesh >= file.Meshes.Length ) continue;

			file.WorldMatrix( n, world );

			foreach ( var primitive in file.Meshes[node.Mesh].Primitives )
			{
				if ( primitive.Mode != 4 ) continue;
				if ( !primitive.Attributes.TryGetValue( "POSITION", out var positions ) ) continue;

				var count = file.AccessorAt( positions )?.Count ?? 0;
				if ( count == 0 ) continue;

				var hasNormals = primitive.Attributes.TryGetValue( "NORMAL", out var normals );
				var hasUv = primitive.Attributes.TryGetValue( "TEXCOORD_0", out var uvs );

				var vertices = new List<Vertex>( count );

				for ( var v = 0; v < count; v++ )
				{
					var vertex = new Vertex { Color = Color32.White, Tangent = new Vector4( 1, 0, 0, 1 ) };

					file.ReadFloat( positions, v, read );
					GltfMath.TransformPoint( world, read, moved );
					vertex.Position = GltfMath.Point( moved );

					if ( hasNormals )
					{
						file.ReadFloat( normals, v, read );
						GltfMath.TransformDirection( world, read, moved );
						GltfMath.Normalise( moved );
						vertex.Normal = GltfMath.Point( moved );
					}

					if ( hasUv )
					{
						file.ReadFloat( uvs, v, read );
						vertex.TexCoord0 = new Vector4( read[0], read[1], 0, 0 );
					}

					vertices.Add( vertex );
				}

				var indices = new List<int>();

				if ( primitive.Indices >= 0 )
				{
					var indexCount = file.AccessorAt( primitive.Indices )?.Count ?? 0;
					for ( var i = 0; i < indexCount; i++ ) indices.Add( (int)file.ReadIndex( primitive.Indices, i ) );
				}
				else
				{
					for ( var i = 0; i < count; i++ ) indices.Add( i );
				}

				if ( indices.Count < 3 ) continue;

				var material = primitive.Material >= 0 && primitive.Material < materials.Length
					? materials[primitive.Material]
					: null;

				var mesh = new Mesh( material );
				mesh.CreateVertexBuffer( vertices.Count, vertices );
				mesh.CreateIndexBuffer( indices.Count, indices );
				mesh.Bounds = BBox.FromPoints( vertices.Select( x => x.Position ) );

				builder.AddMesh( mesh );
				any = true;
			}
		}

		return any ? builder.Create() : null;
	}

	//A file's material as one of ours: its colour picture, its numbers, its sides.
	private static Material MaterialOf( GltfFile file, int index, string model )
	{
		//By name, where a model's mesh can find it - see kept.
		var material = Material.Create( $"{model}/{file.MaterialNames[index] ?? index.ToString()}", CoreSceneRenderer.StandardShader, anonymous: false );
		kept.Add( material );
		var surface = index < file.MaterialSurfaces.Length ? file.MaterialSurfaces[index] : default;

		material.Set( "Tint", surface.Colour );
		material.Set( "Roughness", surface.Says( ModelFormat.SurfaceSays.Roughness ) ? surface.roughness : 0.5f );
		material.Set( "Metalness", surface.Says( ModelFormat.SurfaceSays.Metalness ) ? surface.metalness : 0.0f );

		if ( index < file.MaterialTwoSided.Length && file.MaterialTwoSided[index] )
			material.Set( "F_BACKFACES", 1 );

		var picture = index < file.MaterialColourPictures.Length ? file.MaterialColourPictures[index] : null;

		if ( picture is not null )
		{
			using var stream = new MemoryStream( picture );
			var texture = TextureLoader.Image.Load( stream, $"{model}/colour" );
			if ( texture is not null ) material.Set( "Color", texture );
		}

		return material;
	}
}
