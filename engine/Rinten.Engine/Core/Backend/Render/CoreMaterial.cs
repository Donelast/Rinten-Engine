namespace Rinten;

//Material as plain values (shader, textures, numbers); the renderer builds its own at draw time
internal sealed class CoreMaterial
{
	//What it is called - a path for one loaded from disk.
	internal string Name;

	//Numbers, by the name a shader binds them to. Everything that is not a texture is four floats,
	//which is how their attributes work too: a float is the first of four, a colour is all of them.
	internal readonly Dictionary<string, Vector4> Values = new( StringComparer.OrdinalIgnoreCase );

	//Which of Values the material's file declared under its Numbers - so filling it again from the file can take the last lot out first. See Material.Fill.
	internal readonly HashSet<string> Declared = new( StringComparer.OrdinalIgnoreCase );

	//Textures, by the name a shader binds them to.
	internal readonly Dictionary<string, Texture> Textures = new( StringComparer.OrdinalIgnoreCase );

	//Whether anything has been set on it since it was made - what tells an editor the material no longer matches the file it came from.
	internal bool Edited;

	//Bumped whenever what the GPU material is made of changes - its maps, or its first edit, which is when the scene
	//starts drawing it live - so that one is rebuilt. Not for a number set on a material already live: see ValuesRevision.
	internal int Revision;

	//Bumped when a number changes on a live material, so it is rewritten in place next frame
	internal int ValuesRevision;

	//Which surface it behaves as, for physics. Below zero where it says nothing about it, which is every material made at runtime.
	internal int SurfaceIndex = -1;

	//The shader it draws with, by resource path.
	internal string Shader
	{
		get => shader;
		set => shader = string.IsNullOrEmpty( value ) ? "invalid" : value;
	}

	private string shader = "invalid";

	internal CoreMaterial( string name, string shader )
	{
		Name = name;
		Shader = shader;
	}

	//A material with the same values, which is all a copy is.
	internal CoreMaterial Copy( string name )
	{
		var made = new CoreMaterial( name ?? Name, Shader )
		{
			Edited = Edited,
			SurfaceIndex = SurfaceIndex,
		};

		foreach ( var (key, value) in Values ) made.Values[key] = value;
		foreach ( var (key, value) in Textures ) made.Textures[key] = value;
		made.Declared.UnionWith( Declared );

		return made;
	}

	internal Vector4 Value( string name )
		=> Values.TryGetValue( name, out var value ) ? value : default;

	//A number, or the fallback if never set; missing must not read as zero (MaterialDefinition defaults)
	internal float Value( string name, float fallback )
		=> Values.TryGetValue( name, out var value ) ? value.x : fallback;

	internal Texture Texture( string name )
		=> Textures.TryGetValue( name, out var texture ) ? texture : null;

	//The colour map if named as our shaders name it, otherwise the first texture
	internal Texture FirstTexture
	{
		get
		{
			foreach ( var name in new[] { "g_tColor", "Color", "g_tAlbedo" } )
			{
				if ( Textures.TryGetValue( name, out var found ) && found is not null ) return found;
			}

			foreach ( var texture in Textures.Values )
			{
				if ( texture is not null ) return texture;
			}

			return null;
		}
	}
}
