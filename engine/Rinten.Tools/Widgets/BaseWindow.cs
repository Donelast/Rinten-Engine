using System;

namespace Editor;

/// <summary>A standalone editor window (preferences, export, etc.) that is destroyed on close and draws its icon</summary>
public partial class BaseWindow : Window
{
	public Action OnWindowClosed { get; set; }

	/// <summary>The icon, drawn: a rounded square in the accent colour with the named icon over it, rather than a file somebody has to have put somewhere.</summary>
	public override void SetWindowIcon( string name ) => SetWindowIcon( name, Theme.Primary );

	/// <summary>The same, in a colour of the window's choosing: a window that is up because something went wrong says so with a red one.</summary>
	public void SetWindowIcon( string name, Color ground )
	{
		var icon = new Pixmap( 128, 128 );
		icon.Clear( Color.Transparent );

		using ( Paint.ToPixmap( icon ) )
		{
			var r = new Rect( 0, 0, 128, 128 );

			Paint.ClearPen();
			Paint.SetBrush( ground );
			Paint.DrawRect( r, 16 );

			Paint.SetPen( Theme.Text );
			Paint.DrawIcon( r, name, 120 );
		}

		//The Pixmap overload, which is the one that also puts it on the title bar. Not base.SetWindowIcon( name ), which is this method again.
		base.SetWindowIcon( icon );
	}

	public BaseWindow() : base( null )
	{
		IsWindow = true;
		DeleteOnClose = true;
	}

	protected override void OnClosed()
	{
		base.OnClosed();

		OnWindowClosed?.Invoke();
	}
}
