using System;

namespace Editor
{
	/// <summary>Like a widget - but is drawn</summary>
	public class Frame : Widget
	{
		internal Frame() : base()
		{

		}

		public Frame( Widget parent ) : base( parent )
		{
		}

	}
}
