using System.Runtime.InteropServices;

namespace Rinten;

/// <summary>Describes a per-vertex delta for a morph target (blend shape).</summary>
[StructLayout( LayoutKind.Sequential )]
public readonly record struct MorphDelta( int VertexIndex, Vector3 PositionDelta, Vector3 NormalDelta );

public partial class Mesh
{
	private Dictionary<string, MorphDelta[]> _morphTargets;

	internal IReadOnlyDictionary<string, MorphDelta[]> MorphTargets => _morphTargets;

	/// <summary>Add a morph target (blend shape) to this mesh. Each delta describes how a vertex should be displaced when the morph is fully active. Morph targets are applied when the mesh is created through <see cref="ModelBuilder"/>.</summary>
	public Mesh AddMorph( string name, ReadOnlySpan<MorphDelta> deltas )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		if ( deltas.Length == 0 )
			throw new ArgumentException( "Morph deltas cannot be empty", nameof( deltas ) );

		_morphTargets ??= new( StringComparer.OrdinalIgnoreCase );
		_morphTargets[name] = deltas.ToArray();

		//The builder reads the core mesh rather than this one, so the shapes have to be
		//on both - a shape only this side knew about was one the model was built without.
		if ( core is not null ) core.MorphTargets = _morphTargets;

		return this;
	}

	[StructLayout( LayoutKind.Sequential )]
	internal struct MorphNativeDesc
	{
		public int NameOffset;
		public int NameLength;
		public int StartDelta;
		public int NumDeltas;
	}
}
