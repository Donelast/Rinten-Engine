using NativeEngine;

namespace Rinten;

/// <summary>A material. Uses several <see cref="Texture"/>s and a shader with specific settings for more interesting visual effects.</summary>
public sealed partial class Material : Resource
{


	//The values, where this material is one of ours rather than one of theirs. Null for a material out of their material system.
	internal CoreMaterial core;

	public override bool IsValid => true;

	//Which engine 2D shape this stands for (UI box, line, text...), when it isn't a real shader
	internal EngineDraw Draws { get; private init; }

	//One of the engine's own 2D shapes, as a material - see Draws.
	internal enum EngineDraw
	{
		/// <summary>An ordinary material: a shader, and what is set on it.</summary>
		None = 0,

		/// <summary>A picture, tinted - what a quad with nothing else asked for draws.</summary>
		Picture,

		/// <summary>A rounded box with its border, as the interface lays one out.</summary>
		Box,

		/// <summary>A run of text, out of the atlas the text renderer filled.</summary>
		Text,

		/// <summary>A line from one point to another, as thick as it was asked.</summary>
		Line,

		//The panels are drawn by CoreUiRenderer out of the descriptors the layout built; this is the half-finished other way of doing it. Named so the batcher can tell that there is no pass behind it and stop before it uploads a frame's worth of buffers for a draw that lands nowhere - see UIBatcher.Draw.
		/// <summary>A run of boxes drawn from a buffer on the card - the interface's instanced path, which nothing draws yet.</summary>
		Batched,
	}

	//A material that stands for one of the engine's own shapes rather than for a shader - see Draws.
	internal static Material Engine( EngineDraw draws, string name )
		=> new( new CoreMaterial( name, null ), anonymous: true ) { Draws = draws };

	/// <summary>Name (or path) of the material.</summary>
	public string Name { get; internal set; }

	/// <summary>Access to all of the attributes of this material.</summary>
	public RenderAttributes Attributes { get; internal set; }

	//One of ours: a name, a shader, and the values set on it.
	internal Material( CoreMaterial core, bool anonymous = false )
	{
		this.core = core ?? throw new System.Exception( "Material cannot be null!" );
		this.Name = core.Name;

		if ( !anonymous )
		{
			RegisterWeakResourceId( core.Name );
			Remember( this );
		}

		Attributes = new RenderAttributes();
	}

	internal override void Destroy()
	{
		//kill the native pointer - it does with the native material we want to reduce the risk that someone is holding on to it.
		Attributes?.Clear();
		Attributes = null;

		core = null;
		//No-op: nothing to ask here.
	}

	/// <summary>Create a copy of this material</summary>
	public Material CreateCopy( string name = null )
	{
		return new Material( core.Copy( name ) );
	}
}
