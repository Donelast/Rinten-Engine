//Frozen phase-7 copy of Rinten.Layout (Yoga port, MIT) for differential tests; do not edit
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
namespace Rinten.Layout.Reference;

//Tree-wide flags from Yoga's Config; Version stamps cached layouts so flag changes invalidate them
internal sealed class LayoutConfig
{
	//Yoga's own default, and it is not "none": a flex item with no explicit minimum
	//keeps an undefined one rather than the automatic minimum size CSS would give it.
	public const int ErrataMinSizeUndefinedInsteadOfAuto = 8;

	private bool useWebDefaults;
	private float pointScaleFactor = 1.0f;
	private int errata = ErrataMinSizeUndefinedInsteadOfAuto;

	//Counted up whenever a change here would invalidate a layout.
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

	public bool HasErrata( int which ) => ( errata & which ) != 0;

	//Whether moving a node from one config to another should throw away what was computed under the old one.
	public static bool UpdateInvalidatesLayout( LayoutConfig from, LayoutConfig to )
		=> from.errata != to.errata
			|| from.pointScaleFactor != to.pointScaleFactor
			|| from.useWebDefaults != to.useWebDefaults;

	//What a node made with no config of its own gets.
	public static readonly LayoutConfig Default = new();
}
