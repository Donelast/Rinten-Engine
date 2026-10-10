namespace Rinten;

public class ModelParts
{
	private readonly Model _model;
	private List<Model.BodyPart> _all;

	//The bits each group owns, in one array, so resolving a mask does not build one every time.
	private ulong[] _partMasks;

	/// <summary>All body parts in this model.</summary>
	public IReadOnlyList<Model.BodyPart> All
	{
		get
		{
			_all ??= BuildParts();
			return _all;
		}
	}

	/// <summary>How many body parts there are in this model.</summary>
	public int Count { get; private set; }

	/// <summary>Default body groups mask for this model.</summary>
	public ulong DefaultMask { get; private set; }

	private ModelParts() { }

	public ModelParts( Model model )
	{
		_model = model ?? throw new ArgumentNullException( nameof( model ) );

		//Body parts are groups of meshes with one choice showing; the core model answers for itself.
			Count = _model.core.PartCount;
			DefaultMask = _model.core.DefaultMask;
	}

	private List<Model.BodyPart> BuildParts()
	{
		return BuildOurParts();
	}

	//The bits each group owns, built with the parts and kept.
	private ulong[] PartMasks
	{
		get
		{
			if ( _partMasks is not null ) return _partMasks;

			var parts = All;
			var masks = new ulong[parts.Count];

			for ( var i = 0; i < parts.Count; i++ )
				masks[i] = parts[i].Mask;

			return _partMasks = masks;
		}
	}

	//The same, read out of our own model.
	private List<Model.BodyPart> BuildOurParts()
	{
		var core = _model.core;
		var list = new List<Model.BodyPart>( Count );

		for ( var i = 0; i < Count; i++ )
		{
			var choiceCount = core.PartChoiceCount( i );
			var choices = new List<Model.BodyPart.Choice>( choiceCount );

			for ( var c = 0; c < choiceCount; c++ )
			{
				choices.Add( new Model.BodyPart.Choice( core.PartChoiceName( i, c ), core.PartChoiceMask( i, c ) ) );
			}

			var name = core.PartName( i );

			var bits = new ulong[choices.Count];
			for ( var c = 0; c < choices.Count; c++ )
				bits[c] = choices[c].Mask;

			list.Add( new Model.BodyPart
			{
				Index = i,
				Name = string.IsNullOrWhiteSpace( name ) ? "empty" : name,
				Mask = core.PartMask( i ),
				Choices = choices.AsReadOnly(),
				ChoiceBits = bits,
			} );
		}

		return list;
	}

	internal void Dispose()
	{
		_all?.Clear();
		_all = null;
		_partMasks = null;
	}

	/// <summary>Get body part by name.</summary>
	public Model.BodyPart Get( string name )
	{
		return All.FirstOrDefault( x => x.Name == name );
	}

	/// <summary>Get body part by index, or null where there is no such part.</summary>
	public Model.BodyPart Get( int index )
	{
		return index >= 0 && index < All.Count ? All[index] : null;
	}

	/// <summary>Resolves Unset and unmentioned groups to defaults; see BodyMask.Resolve</summary>
	public ulong Resolve( ulong mask ) => BodyMask.Resolve( mask, DefaultMask, PartMasks );

	//A group owns a set of bits and each of its choices owns some of them, so choosing is clearing the group's bits and setting the choice's. Nothing else in the mask is touched - the other groups keep whatever they were showing.
	/// <summary>The same mask with one body group switched to one of its choices.</summary>
	public ulong WithChoice( ulong mask, string part, int choice )
	{
		return WithChoice( mask, Get( part ), choice );
	}

	/// <summary>The same, by the index of the group rather than its name.</summary>
	public ulong WithChoice( ulong mask, int part, int choice )
	{
		return WithChoice( mask, Get( part ), choice );
	}

	private ulong WithChoice( ulong mask, Model.BodyPart found, int choice )
		=> found is null ? mask : BodyMask.WithChoice( mask, found.Mask, found.ChoiceBits, choice );

	/// <summary>Which choice of a group a mask is showing, or zero where it is showing none of them - which is what the first choice means to a caller that never set one.</summary>
	public int ChoiceOf( ulong mask, string part )
	{
		return ChoiceOf( mask, Get( part ) );
	}

	/// <summary>The same, by the index of the group rather than its name.</summary>
	public int ChoiceOf( ulong mask, int part )
	{
		return ChoiceOf( mask, Get( part ) );
	}

	private int ChoiceOf( ulong mask, Model.BodyPart found )
		=> found is null ? 0 : BodyMask.ChoiceOf( mask, found.ChoiceBits );
}

partial class Model
{
	ModelParts _parts;

	/// <summary>Access to body parts of this model.</summary>
	public ModelParts Parts
	{
		get
		{
			_parts ??= new ModelParts( this );
			return _parts;
		}
	}

	[Obsolete( $"Use {nameof( Parts )}" )]
	public int BodyGroupCount => Parts.Count;

	[Obsolete( $"Use {nameof( Parts )}" )]
	public ulong DefaultBodyGroupMask => Parts.DefaultMask;

	[Obsolete( $"Use {nameof( Parts )}" )]
	public IEnumerable<BodyPart> BodyParts => Parts.All;

	public sealed class BodyPart
	{
		public int Index { get; internal set; }
		public string Name { get; internal set; }
		public ulong Mask { get; internal set; }
		public IReadOnlyList<Choice> Choices { get; internal set; }

		//The same bits as Choices, kept flat for BodyMask.
		internal ulong[] ChoiceBits { get; set; } = System.Array.Empty<ulong>();

		public sealed record Choice( string Name, ulong Mask );

		internal int GetChoiceIndex( string name )
		{
			for ( int i = 0; i < Choices.Count; i++ )
			{
				if ( Choices[i].Name == name ) return i;
			}

			return -1;
		}
	}

	/// <summary>Used to mark properties as a body group mask, so the correct editor can be used</summary>
	public sealed class BodyGroupMaskAttribute : System.Attribute
	{
		public string ModelParameter { get; set; } = "Model";
	}
}
