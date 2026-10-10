namespace Editor;

internal static class ProjectThumb
{
	public const string Filter = "Images (*.png *.jpg *.jpeg *.webp *.tga)";

	public static bool Pick( Project project, Widget parent = null )
	{
		if ( project is null )
			return false;

		var previous = project.GetThumbPath();

		var fd = new FileDialog( parent )
		{
			Title = "Project Image"
		};

		fd.Directory = project.GetRootPath();
		fd.SetFindExistingFile();
		fd.SetModeOpen();
		fd.SetNameFilter( Filter );

		if ( !fd.Execute() )
			return false;

		if ( !project.SetThumbFromFile( fd.SelectedFile ) )
			return false;

		if ( !string.IsNullOrEmpty( previous ) )
			Paint.ForgetImage( previous );

		var path = project.GetThumbPath();
		if ( !string.IsNullOrEmpty( path ) )
			Paint.ForgetImage( path );

		return true;
	}

	public static void Draw( Rect rect, Project project, Pixmap placeholder = null )
	{
		var path = project?.GetThumbPath();

		if ( !string.IsNullOrEmpty( path ) )
		{
			Paint.SetPen( Color.White );
			Paint.Draw( rect, path, borderRadius: 4 );
			return;
		}

		if ( placeholder is not null )
		{
			Paint.SetBrush( Color.White );
			Paint.Draw( rect, placeholder, borderRadius: 4 );
			return;
		}

		Paint.SetPen( Theme.Text );
		Paint.DrawIcon( rect, "image", 22, TextFlag.Center );
	}
}
