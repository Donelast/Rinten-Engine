namespace Editor.AssetPickers;

//Picks the file an editor app needs via the asset picker; razor panels use the Code browser
static class AppFilePicker
{
	[Event( "editor.app.pickfile" )]
	static void Pick( EditorAppAttribute.FileRequest request )
	{
		if ( request is null || request.Handled ) return;

		var types = request.Extensions
			.Select( x => AssetType.FromExtension( x ) )
			.ToList();

		var parent = Rinten.Internal.GlobalToolsNamespace.EditorWindow;
		var title = request.Title;

		AssetPicker picker;

		if ( types.TrueForAll( x => x is not null ) )
		{
			picker = types.Count == 1
				? AssetPicker.Create( parent, types[0], new AssetPicker.PickerOptions() )
				: new GenericPicker( parent, types, new AssetPicker.PickerOptions() );

			picker.OnAssetPicked = assets =>
			{
				if ( assets.FirstOrDefault()?.AbsolutePath is { } path )
					request.Picked?.Invoke( path );
			};
		}
		else
		{
			var generic = new GenericPicker( parent, [], new AssetPicker.PickerOptions { FileExtensions = request.Extensions } );
			picker = generic;

			generic.Show();

			generic.Browser.OnFileSelected = path =>
			{
				var extension = System.IO.Path.GetExtension( path ).TrimStart( '.' ).ToLowerInvariant();

				if ( !request.Extensions.Contains( extension ) ) return;

				generic.Close();
				request.Picked?.Invoke( path );
			};
		}

		picker.Title = title;
		picker.Show();

		if ( !string.IsNullOrEmpty( request.Folder ) )
		{
			if ( picker is GenericPicker { Browser: { } browser } )
				browser.NavigateToFolder( request.Folder );
			else
				picker.SetSelection( (Asset)null );
		}

		request.Handled = true;
	}
}
