using System;
using System.Globalization;
using System.Linq;

namespace Editor
{
	/// <summary>Split frame, allows dragging to resize panels</summary>
	public partial class Splitter : Frame
	{

		public Splitter( Widget parent ) : base( parent )
		{
			SetSizeMode( SizeMode.Flexible, SizeMode.Flexible );
		}

		public void AddWidget( Widget w )
		{
			if ( w is null ) return;

			children.Add( w );
			shares.Add( 0.0f );

			w.SetPanelParent( this );

			MarkNeedsArrange();
		}

		//For a splitter whose contents are put back in a particular order after something has borrowed one of them. Widgets are only unparented, not destroyed - whoever handed them over still owns them.
		/// <summary>Takes every widget out, leaving them alive.</summary>
		public void Clear()
		{
			foreach ( var child in children.ToArray() )
			{
				if ( child.IsValid() ) child.SetPanelParent( null );
			}

			children.Clear();
			shares.Clear();

			MarkNeedsArrange();
		}

		public bool IsHorizontal
		{
			get => !vertical;
			set
			{
				vertical = !value;
				MarkNeedsArrange();
			}
		}

		public bool IsVertical
		{
			get => vertical;
			set
			{
				vertical = value;
				MarkNeedsArrange();
			}
		}

		public bool OpaqueResize
		{
			get => true;
			set
			{
				//Ours always moves as it is dragged; there is no outline to draw instead of the thing itself.
				return;
			}
		}

		public int HandleWidth
		{
			get => (int)handleWidth;
			set
			{
				handleWidth = MathF.Max( value, 1 );
				MarkNeedsArrange();
			}
		}

		public string SaveState()
		{
			return string.Join( ' ', shares.Select( x => x.ToString( CultureInfo.InvariantCulture ) ) );
		}

		public void RestoreState( string state )
		{
			if ( string.IsNullOrWhiteSpace( state ) ) return;

			var parts = state.Split( ' ' );

			shares.Clear();

			foreach ( var part in parts )
			{
				if ( float.TryParse( part, NumberStyles.Float, CultureInfo.InvariantCulture, out var share ) )
					shares.Add( share );
			}

			MarkNeedsArrange();
		}

		public void SetStretch( int cell, int stretch )
		{
			//A stretch factor here is the share of the splitter this one starts with, against everything else that asked for one.
			if ( cell < 0 || cell >= shares.Count ) return;

			shares[cell] = MathF.Max( stretch, 0.0f );

			MarkNeedsArrange();
		}

		public void SetCollapsible( int index, bool collapsible )
		{
			//Ours never collapse a child to nothing - the smallest a share can be is a fiftieth - so there is nothing to turn off.
			return;
		}
	}
}
