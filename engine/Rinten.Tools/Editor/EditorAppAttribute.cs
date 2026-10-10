using System;

namespace Editor
{
	[AttributeUsage( AttributeTargets.Class )]
	public class EditorAppAttribute : Attribute, Rinten.ITypeAttribute
	{
		public string Title { get; set; }
		public string Icon { get; set; }
		public string Description { get; set; }
		public Type TargetType { get; set; }

		/// <summary>Whether this takes over the editor's workspace instead of opening as its own window.</summary>
		public bool Workspace { get; set; } = true;

		/// <summary>Extension(s) this app needs a file of, e.g. "razor,cs"; asked for when opened from the Apps menu</summary>
		public string Opens { get; set; }

		public EditorAppAttribute( string title, string icon, string description )
		{
			Title = title;
			Icon = icon;
			Description = description;
		}

		//The extensions this app needs one of before it has anything to show, or none.
		string[] NeededExtensions()
		{
			var opens = Opens;

			if ( string.IsNullOrWhiteSpace( opens ) )
			{
				opens = string.Join( ",", TargetType.GetCustomAttributes( typeof( EditorForAssetTypeAttribute ), true )
					.OfType<EditorForAssetTypeAttribute>()
					.Select( x => x.Extension ) );
			}

			return opens.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries )
				.Select( x => x.TrimStart( '.' ).ToLowerInvariant() )
				.Distinct()
				.ToArray();
		}

		//Code lives under Code; everything else an editor opens is an asset.
		static bool IsCodeExtension( string extension ) => extension is "razor" or "cs" or "scss";

		/// <summary>What picking the app from the Apps menu does: an app that needs a file and is not open yet asks for one first, on a modal with a button that opens the file box in the folder such files live in - Assets, or Code for a razor panel. Already open, it is only brought up.</summary>
		public void OpenFromMenu()
		{
			var extensions = NeededExtensions();

			if ( extensions.Length == 0 || IsOpen() )
			{
				Open();
				return;
			}

			var kind = string.Join( " or ", extensions.Select( x => $".{x}" ) );

			var modal = EditorModal.Choose( null, Title,
				$"The {Title} works on a {kind} file. Choose one to open.", "Cancel",
				new[] { new EditorModal.Choice( "Choose file…", () => PickAndOpen( extensions ) ) } );

			if ( modal is not null )
				modal.Icon = Icon;
		}

		//Whether this app already has a tab or a window up.
		bool IsOpen()
		{
			if ( Workspace && WorkspaceArea.Current.IsValid() )
				return WorkspaceArea.Current.FindEditor( TargetType ) is not null;

			return false;
		}

		/// <summary>What an app asks for when it needs a file: the editor's asset picker answers it - see the tools addon's <c>"editor.app.pickfile"</c> handler - opened on the folder such files live in.</summary>
		public sealed class FileRequest
		{
			/// <summary>What the picker's window says.</summary>
			public string Title { get; init; }

			/// <summary>The extensions it takes, lower case, without the dot.</summary>
			public string[] Extensions { get; init; }

			/// <summary>Where the picker opens: Assets, or Code for a razor panel.</summary>
			public string Folder { get; init; }

			/// <summary>Called with the absolute path of what was picked.</summary>
			public Action<string> Picked { get; init; }

			/// <summary>Set by whoever showed a picker, so the platform's file box is not opened as well.</summary>
			public bool Handled { get; set; }
		}

		/// <summary>Asks for the file this app works on and opens it here - what File > Open does while the app's tab is in front. False for an app that needs none.</summary>
		public bool AskForFile()
		{
			var extensions = NeededExtensions();

			if ( extensions.Length == 0 ) return false;

			PickAndOpen( extensions );
			return true;
		}

		void PickAndOpen( string[] extensions ) => PickFile( $"Open in {Title}", extensions, OpenFile );

		/// <summary>The asset picker showing only files of these extensions, opened on the folder they live in - Assets, or Code for a razor panel - and <paramref name="picked"/> told the absolute path of what was chosen. The platform's own file box only where no picker answered: the tools addon not loaded.</summary>
		public static void PickFile( string title, string[] extensions, Action<string> picked )
		{
			var project = Project.Current;

			var folder = extensions.All( IsCodeExtension ) ? project?.GetCodePath() : project?.GetAssetsPath();

			var request = new FileRequest { Title = title, Extensions = extensions, Folder = folder, Picked = picked };

			EditorEvent.Run( "editor.app.pickfile", request );

			if ( request.Handled ) return;

			var fd = new FileDialog( null );
			fd.Title = title;
			fd.SetFindExistingFile();
			fd.SetModeOpen();

			if ( !string.IsNullOrEmpty( folder ) && System.IO.Directory.Exists( folder ) )
				fd.Directory = folder;

			fd.SetNameFilter( $"{title} ({string.Join( " ", extensions.Select( x => $"*.{x}" ) )})" );

			if ( !fd.Execute() || string.IsNullOrEmpty( fd.SelectedFile ) ) return;

			picked?.Invoke( fd.SelectedFile );
		}

		/// <summary>Opens a file in this app. An asset this app is the registered editor for goes the double-click way - a document tab of its own; any other asset is handed to the app (the animation graph opens a model's graph); a file that is not an asset goes to an <see cref="IFileEditor"/>.</summary>
		public void OpenFile( string path )
		{
			var extension = System.IO.Path.GetExtension( path ).TrimStart( '.' ).ToLowerInvariant();

			var registered = TargetType.GetCustomAttributes( typeof( EditorForAssetTypeAttribute ), true )
				.OfType<EditorForAssetTypeAttribute>()
				.Any( x => string.Equals( x.Extension, extension, StringComparison.OrdinalIgnoreCase ) );

			var asset = IsCodeExtension( extension ) ? null : AssetSystem.FindByPath( path );

			if ( asset is not null && registered )
			{
				IAssetEditor.OpenInEditor( asset, out _ );
				return;
			}

			var editor = Open();

			if ( asset is not null && editor is IAssetEditor assetEditor )
			{
				assetEditor.AssetOpen( asset );
				return;
			}

			if ( editor is IFileEditor fileEditor )
			{
				fileEditor.OpenFile( path );
				return;
			}

			Log.Warning( $"{Title}: cannot open {path}" );
		}

		/// <summary>Opens the app - its tab, or a window - and gives back the editor, where there is one.</summary>
		public Widget Open()
		{
			//An editor laid out around the scene takes the scene area along, the way mapping does, rather than being a layout of its own with no scene in it.
			if ( TargetType.IsAssignableTo( typeof( SceneWorkspace ) ) && Rinten.Internal.GlobalToolsNamespace.EditorWindow.IsValid() )
			{
				Rinten.Internal.GlobalToolsNamespace.EditorWindow.OpenSceneWorkspace( Title, Icon, () => EditorTypeLibrary.Create<SceneWorkspace>( TargetType ) );
				return null;
			}

			if ( Workspace && WorkspaceArea.Current.IsValid() )
				return WorkspaceArea.Current.OpenEditor( TargetType, Title, Icon )?.Editor;

			var window = EditorTypeLibrary.Create<Widget>( TargetType );
			window.Show();

			return window;
		}

		//Names are matched loosely because they are written for a menu: "Material Editor" is two words and a capital nobody wants to type at a console.
		/// <summary>Opens one of the editor's apps by the name it goes by in the Tools menu - what picking it from that menu does, without the menu.</summary>
		[ConCmd( "editor_app" )]
		public static void OpenApp( string title )
		{
			var apps = EditorTypeLibrary.GetAttributes<EditorAppAttribute>().ToList();

			var found = apps.FirstOrDefault( x => Matches( x.Title, title ) );

			if ( found is null )
			{
				Log.Warning( $"editor_app: no app called '{title}' - there is " +
					string.Join( ", ", apps.Select( x => $"'{x.Title}'" ) ) );

				return;
			}

			found.Open();
		}

		//The same name, ignoring case and the spaces between the words.
		static bool Matches( string app, string asked )
		{
			if ( string.IsNullOrWhiteSpace( asked ) ) return false;

			return string.Equals( app.Replace( " ", "" ), asked.Replace( " ", "" ),
				StringComparison.OrdinalIgnoreCase );
		}
	}

	/// <summary>An editor app whose files are not assets - the UI editor's razor panels, which live under Code - opened by path. See <see cref="EditorAppAttribute.OpenFile"/>.</summary>
	public interface IFileEditor
	{
		void OpenFile( string path );
	}
}
