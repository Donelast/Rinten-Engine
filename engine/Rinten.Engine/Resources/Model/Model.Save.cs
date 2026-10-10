namespace Rinten;

public partial class Model
{
	//What comes back is the resource the engine loads, physics included, not a source document. Write it next to the assets as a compiled file - there is nothing here for the model compiler to chew on.
	/// <summary>Serializes this model as a compiled <c>.mdl_c</c>.</summary>
	public byte[] SaveToMdl()
	{
		if ( !core.IsValid )
			throw new InvalidOperationException( "Model is not valid" );

		return core.ToBytes()
			?? throw new InvalidOperationException( $"could not serialize {Name}" );
	}

	/// <summary>Legacy name. Compiled models are <c>.mdl_c</c> now.</summary>
	[Obsolete( "Use SaveToMdl." )]
	public byte[] SaveToVmdl() => SaveToMdl();
}
