namespace Rinten;

partial class ModelDefinition
{
	/// <summary>Body group bit layout computed from the .mdl, as the compiler assigns it; mask holds 64 bits</summary>
	public sealed class BodyGroupLayout
	{
		//The mask is a ulong and a choice is a bit of it, so sixty-four is the whole budget for the model - not per group. The number is kept because the mask is written into every scene and every compiled model that already exists.
		/// <summary>How many choices a model can have in total, across every group.</summary>
		public const int MaxChoices = 64;

		/// <summary>One option of a group, and the bit it was given.</summary>
		public sealed class Choice
		{
			public string Name { get; init; } = "";

			/// <summary>Where it sits in its group, which is what code setting a body group passes.</summary>
			public int Index { get; init; }

			public ulong Mask { get; init; }

			public IReadOnlyList<string> Meshes { get; init; } = Array.Empty<string>();

			/// <summary>A choice that turns no mesh on, which is how a part comes off.</summary>
			public bool Hides => Meshes is not { Count: > 0 };
		}

		/// <summary>One group of choices, of which one shows at a time.</summary>
		public sealed class Part
		{
			public string Name { get; init; } = "";
			public int Index { get; init; }

			/// <summary>Every bit this group owns, which is every one of its choices.</summary>
			public ulong Mask { get; init; }

			public IReadOnlyList<Choice> Choices { get; init; } = Array.Empty<Choice>();

			//The same bits again, flat, for BodyMask.
			internal ulong[] Bits { get; init; } = Array.Empty<ulong>();

			/// <summary>The last choice that turns nothing on, or -1 where none does.</summary>
			public int Hidden { get; init; } = -1;

			/// <summary>Whether the hiding choice is last, which clothing assumes when it hides a body part</summary>
			public bool HidesLast => Hidden >= 0 && Hidden == Choices.Count - 1;

			public Choice Get( int choice )
				=> choice >= 0 && choice < Choices.Count ? Choices[choice] : null;
		}

		/// <summary>The groups that were given bits, in file order.</summary>
		public IReadOnlyList<Part> Parts { get; private init; } = Array.Empty<Part>();

		/// <summary>What the model shows when nobody has said otherwise: the first choice of every group.</summary>
		public ulong DefaultMask { get; private init; }

		/// <summary>How many of the sixty-four bits this model spends.</summary>
		public int Bits { get; private init; }

		/// <summary>Choices there was no bit left for. Anything above zero is a model that will not compile the way it reads.</summary>
		public int Dropped { get; private init; }

		public bool Any => Parts.Count > 0;

		/// <summary>How many bits are still free.</summary>
		public int Spare => MaxChoices - Bits;

		private ulong[] _partMasks = Array.Empty<ulong>();

		/// <summary>Read the groups off a definition and hand out the bits, the way the compiler does.</summary>
		public static BodyGroupLayout Of( IEnumerable<BodyGroup> groups )
		{
			var parts = new List<Part>();
			var masks = new List<ulong>();
			var bit = 0;
			var dropped = 0;
			var defaultMask = 0ul;

			foreach ( var group in groups ?? Enumerable.Empty<BodyGroup>() )
			{
				if ( group is null || !group.Enabled || group.Choices is not { Count: > 0 } ) continue;

				var choices = new List<Choice>( group.Choices.Count );
				var bits = new List<ulong>( group.Choices.Count );
				var part = 0ul;
				var hidden = -1;

				foreach ( var choice in group.Choices )
				{
					//Past the budget the choice is still listed - the editor has to be able to show
					//what does not fit - but it gets no bit, so nothing claims it and nothing draws it.
					if ( bit >= MaxChoices )
					{
						dropped++;
						continue;
					}

					var mask = 1ul << bit++;
					var meshes = Named( choice?.Meshes );

					if ( meshes.Count == 0 ) hidden = choices.Count;

					choices.Add( new Choice
					{
						Name = choice?.Name ?? "",
						Index = choices.Count,
						Mask = mask,
						Meshes = meshes,
					} );

					bits.Add( mask );
					part |= mask;
				}

				if ( choices.Count == 0 ) continue;

				//The first of each group, which is what an artist means by putting it first - and
				//what the compiled model works out for itself when nobody writes a default down.
				defaultMask |= choices[0].Mask;

				parts.Add( new Part
				{
					Name = group.Name ?? "",
					Index = parts.Count,
					Mask = part,
					Choices = choices,
					Bits = bits.ToArray(),
					Hidden = hidden,
				} );

				masks.Add( part );
			}

			return new BodyGroupLayout
			{
				Parts = parts,
				DefaultMask = defaultMask,
				Bits = bit,
				Dropped = dropped,
				_partMasks = masks.ToArray(),
			};
		}

		//The mesh names a choice actually names, blanks dropped.
		static List<string> Named( List<string> meshes )
		{
			var named = new List<string>();

			foreach ( var mesh in meshes ?? new List<string>() )
			{
				if ( !string.IsNullOrWhiteSpace( mesh ) ) named.Add( mesh );
			}

			return named;
		}

		public Part Get( string name )
		{
			if ( string.IsNullOrEmpty( name ) ) return null;

			foreach ( var part in Parts )
			{
				if ( string.Equals( part.Name, name, StringComparison.OrdinalIgnoreCase ) ) return part;
			}

			return null;
		}

		public Part Get( int index )
			=> index >= 0 && index < Parts.Count ? Parts[index] : null;

		/// <summary>What a mask means for this model - see <see cref="BodyMask.Resolve"/>.</summary>
		public ulong Resolve( ulong mask ) => BodyMask.Resolve( mask, DefaultMask, _partMasks );

		/// <summary>The same mask with one group switched to one of its choices.</summary>
		public ulong WithChoice( ulong mask, string part, int choice )
			=> WithChoice( mask, Get( part ), choice );

		/// <summary>The same, by the index of the group rather than its name.</summary>
		public ulong WithChoice( ulong mask, int part, int choice )
			=> WithChoice( mask, Get( part ), choice );

		ulong WithChoice( ulong mask, Part found, int choice )
			=> found is null ? mask : BodyMask.WithChoice( mask, found.Mask, found.Bits, choice );

		/// <summary>Which choice of a group a mask is showing.</summary>
		public int ChoiceOf( ulong mask, string part ) => ChoiceOf( mask, Get( part ) );

		/// <summary>The same, by the index of the group rather than its name.</summary>
		public int ChoiceOf( ulong mask, int part ) => ChoiceOf( mask, Get( part ) );

		int ChoiceOf( ulong mask, Part found )
			=> found is null ? 0 : BodyMask.ChoiceOf( mask, found.Bits );

		//Nothing claimed is the ordinary case and means always drawn. Claimed twice within one group is nearly always a mistake - the mesh then shows under either choice, so the group cannot swap it away.
		/// <summary>Every choice, anywhere in the model, that names this mesh.</summary>
		public ulong ClaimOf( string mesh )
		{
			var claim = 0ul;
			if ( string.IsNullOrWhiteSpace( mesh ) ) return claim;

			foreach ( var part in Parts )
			{
				foreach ( var choice in part.Choices )
				{
					foreach ( var named in choice.Meshes )
					{
						if ( string.Equals( named, mesh, StringComparison.OrdinalIgnoreCase ) )
						{
							claim |= choice.Mask;
							break;
						}
					}
				}
			}

			return claim;
		}
	}

	/// <summary>The bits this definition's body groups compile to. Worked out each time, because the definition is being edited.</summary>
	public BodyGroupLayout BodyGroupBits() => BodyGroupLayout.Of( BodyGroups );
}
