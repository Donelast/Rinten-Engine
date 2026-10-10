namespace Rinten;

//One draw from project-filled buffers, covering every feed combination; unused fields are 0
internal struct CoreBufferDraw
{
	/// <summary>The pipeline, baked for the target the draw lands in.</summary>
	public ulong Pipeline;

	/// <summary>Bound as the vertex buffer - the engine's own vertex, which is the one layout a pipeline here is baked to read. Nought where the shader pulls what it needs out of set 2 instead.</summary>
	public ulong Vertices;

	/// <summary>The indices, or nought for a draw of vertices in order.</summary>
	public ulong Indices;

	/// <summary>Whether an index is four bytes rather than two.</summary>
	public bool WideIndices;

	/// <summary>A pair of buffers a mesh that reads its own vertices keeps at set 2 - the set kept with the buffers rather than made per draw. See VkBuffers.ObjectSetGet.</summary>
	public ulong Geometry;
	public ulong Data;

	/// <summary>The first index or vertex, and how many - read where there are no arguments.</summary>
	public int First;
	public int Count;

	/// <summary>What is added to every index before it names a vertex.</summary>
	public int BaseVertex;

	/// <summary>How many copies. Nought is one.</summary>
	public int Instances;

	/// <summary>A buffer of draw arguments, where the counts come from the card: four words a draw, or five with indices, <see cref="Stride"/> bytes apart.</summary>
	public ulong Arguments;
	public long ArgumentsOffset;
	public int DrawCount;
	public int Stride;

	/// <summary>The material - the set a surface shader reads its pictures and numbers from - or nought.</summary>
	public ulong Texture;
}
