using System.Runtime.CompilerServices;

namespace Rinten.Layout;

//Tree-wide compatibility flags; Version stamps cached layouts so flag changes invalidate them
internal sealed class LayoutConfig
{
	/// <summary>Yoga's own default, and it is not "none": a flex item with no explicit minimum keeps an undefined one rather than the automatic minimum size CSS would give it.</summary>
	public const int ErrataMinSizeUndefinedInsteadOfAuto = 8;

	private bool useWebDefaults;
	private float pointScaleFactor = 1.0f;
	private int errata = ErrataMinSizeUndefinedInsteadOfAuto;

	/// <summary>Counted up whenever a change here would invalidate a layout.</summary>
	public uint Version { get; private set; }

	public bool UseWebDefaults
	{
		get => useWebDefaults;
		set => useWebDefaults = value;
	}

	public float PointScaleFactor
	{
		get => pointScaleFactor;
		set
		{
			if ( pointScaleFactor == value ) return;

			pointScaleFactor = value;
			Version++;
		}
	}

	public int Errata
	{
		get => errata;
		set
		{
			if ( errata == value ) return;

			errata = value;
			Version++;
		}
	}

	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public bool HasErrata( int which ) => ( errata & which ) != 0;

	/// <summary>Whether moving a node from one config to another should throw away what was computed under the old one.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static bool UpdateInvalidatesLayout( LayoutConfig from, LayoutConfig to )
		=> from.errata != to.errata
			|| from.pointScaleFactor != to.pointScaleFactor
			|| from.useWebDefaults != to.useWebDefaults;

	/// <summary>What a node made with no config of its own gets.</summary>
	public static readonly LayoutConfig Default = new();
}
