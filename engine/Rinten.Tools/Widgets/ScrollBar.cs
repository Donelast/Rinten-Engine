using System;

namespace Editor
{
	/// <summary>A scroll bar: where we are in something taller than the room for it.</summary>
	public class ScrollBar : Widget
	{
		private int minimum;
		private int maximum;
		private int value;
		private int pageStep = 1;
		private int singleStep = 1;

		/// <summary>The smallest value the bar can be scrolled to.</summary>
		public int Minimum
		{
			get => minimum;
			set
			{
				minimum = value;

				Clamp();
			}
		}

		public int Maximum
		{
			get => maximum;
			set
			{
				maximum = value;
				Clamp();
			}
		}

		public int Value
		{
			get => value;
			set
			{
				this.value = System.Math.Clamp( value, minimum, System.Math.Max( maximum, minimum ) );
			}
		}

		public int SliderPosition
		{
			get => value;
			set
			{
				Value = value;
			}
		}

		public int PageStep
		{
			get => pageStep;
			set
			{
				pageStep = value;
			}
		}

		public int SingleStep
		{
			get => singleStep;
			set
			{
				singleStep = value;
			}
		}

		//Keeps where we are inside where we may be.
		private void Clamp()
		{
			value = System.Math.Clamp( value, minimum, System.Math.Max( maximum, minimum ) );
		}

		//One that is only numbers, for a widget of ours.
		internal ScrollBar() : base()
		{
		}

	}
}
