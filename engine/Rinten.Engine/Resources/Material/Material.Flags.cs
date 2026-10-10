namespace Rinten;

public partial class Material
{
	/// <summary>Access flags on this material, which usually hint about the contents. These are generally added by the shader procedurally - but developers can add these in material editor too.</summary>
	public FlagsAccessor Flags => new FlagsAccessor( this );

	public readonly ref struct FlagsAccessor
	{
		private readonly Material material;

		public FlagsAccessor( Material material )
		{
			this.material = material;
		}

		public int GetInt( string name, int defaultValue = 0 )
		{
			return material.core.Values.TryGetValue( name, out var value ) ? value.x.CeilToInt() : defaultValue;
		}

		public float GetFloat( string name, float defaultValue = 0 )
		{
			return material.core.Values.TryGetValue( name, out var value ) ? value.x : defaultValue;
		}

		/// <summary>Sky if the shader is a sky shader, or the material has the ported "sky" flag</summary>
		public bool IsSky => GetInt( "sky" ) != 0 || CoreResources.IsSkyShader( material?.core?.Shader );
		public bool IsTranslucent => GetInt( "translucent" ) != 0;
		public bool IsAlphaTest => GetInt( "alphatest" ) != 0;

		[Obsolete( "Decal materials are obsolete" )]
		public bool IsDecal => GetInt( "decal" ) != 0;
	}
}
