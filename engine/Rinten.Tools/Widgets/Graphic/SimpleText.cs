using Rinten;
using System;

namespace Editor
{

	namespace Graphic
	{
		/// <summary>A graphics item that is nothing but a word or two.</summary>
		public class SimpleText : GraphicsItem
		{
			private string text = string.Empty;

			public string Text
			{
				get => text;
				set
				{
					if ( text == value ) return;

					text = value ?? string.Empty;

					Size = Editor.Paint.MeasureText( text );

					Update();
				}
			}

			/// <summary>What colour the words are drawn in.</summary>
			public Color Color { get; set; } = Color.White;

			protected override void OnPaint()
			{
				if ( string.IsNullOrEmpty( text ) ) return;

				Editor.Paint.SetPen( Color );
				Editor.Paint.DrawText( LocalRect, text, TextFlag.LeftTop );
			}
		}
	}
}
