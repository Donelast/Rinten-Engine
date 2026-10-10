using Rinten.Engine;
using Rinten.Internal;
using System;

namespace Editor
{
	/// <summary>Used to tell the user what kind of action will happen during a drag and drop event on mouse release. In Windows, these actions will also display text near cursor to let the user know what will happen if they release their mouse button.</summary>
	public enum DropAction
	{
		/// <summary>The data will be copied.</summary>
		Copy = 0x1,

		/// <summary>The data will be moved.</summary>
		Move = 0x2,

		/// <summary>The data will be linked.</summary>
		Link = 0x4,

		/// <summary>Ignore this drop action.</summary>
		Ignore = 0x0
	}

	//Describing it is all this does. The carrying is EditorDrag, which follows the pointer, tells whoever is under it, and puts it down when the button comes up.
	/// <summary>A drag somebody started from a widget: what is being carried, and a picture of it.</summary>
	public class Drag : EditorObject
	{
		public Drag( Widget source )
		{
			Source = source;
		}

		public Drag( GraphicsMouseEvent e )
		{
			Source = WidgetSurface.Hovered;
		}

		/// <summary>Where it is being carried from.</summary>
		public Widget Source { get; }

		/// <summary>What is being dragged.</summary>
		public DragData Data { get; set; } = new();

		/// <summary>The picture that follows the pointer.</summary>
		public void SetImage( Pixmap image )
		{
			Image = image;
		}

		/// <summary>The picture set for this drag, if one was.</summary>
		public Pixmap Image { get; private set; }

		/// <summary>Starts the drag and answers what came of it - except that it cannot wait for the answer, because the drag runs on the editor's own frames and there is nothing to block on. Whoever needs the answer should read <see cref="EditorDrag.Action"/> when the drop happens.</summary>
		public DropAction ExecuteBlocking()
		{
			Execute();

			return DropAction.Ignore;
		}

		/// <summary>Picks it up.</summary>
		public void Execute()
		{
			EditorDrag.Begin( Source, Data, Image );
		}
	}

	/// <summary>Contains drag and drop data for tool widgets. See <see cref="Widget.DragEvent"/>.</summary>
	public partial class DragData : EditorObject
	{
		internal static DragData Current { get; set; }

		private string rawUrl;

		/// <summary>An object that can be used to pass drag and drop data</summary>
		public object Object { get; set; }

		/// <summary>Text data of the drag and drop event.</summary>
		public string Text { get; set; } = string.Empty;

		/// <summary>HTML data of the drag and drop event, if any.</summary>
		public string Html { get; set; }

		/// <summary>URL data of the drag and drop event, if any.</summary>
		public Uri Url
		{
			get
			{
				var url = rawUrl;
				if ( string.IsNullOrEmpty( url ) ) return default;

				if ( url.StartsWith( "file:///" ) )
				{
					//hash is a valid char in filenames encode it so it's not misinterpreted  as a fragment, which breaks the path
					url = url.Replace( "#", "%23" );
				}

				if ( Uri.TryCreate( url, new UriCreationOptions(), out var uri ) )
				{
					return uri;
				}

				return default;
			}
			set => rawUrl = value?.ToString();
		}

		/// <summary>Whether the drag data has at least 1 file or folder.</summary>
		public bool HasFileOrFolder => Text.StartsWith( "file:///" ) || (Url != null && Url.IsFile);

		/// <summary>The first file or folder in the drag data.</summary>
		public string FileOrFolder
		{
			get
			{
				var first = Text.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ).FirstOrDefault();

				if ( LocalPathOf( first ) is { } fromText ) return fromText;
				if ( LocalPathOf( rawUrl ) is { } fromUrl ) return fromUrl;

				return Url is { IsFile: true } url ? url.LocalPath : null;
			}
		}

		/// <summary>All files and folders in the drag data.</summary>
		public string[] Files
		{
			get
			{
				var lines = Text.Split( '\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

				//One line is one file, so just use FileOrFolder.
				if ( lines.Length < 2 )
				{
					return new string[] { FileOrFolder };
				}

				List<string> output = new();

				foreach ( var line in lines )
				{
					if ( LocalPathOf( line ) is { } path )
					{
						output.Add( path );
					}

					//A plain path rather than a URI. It used to be added for the URI lines as well, so every file of a drag arrived
					//twice - once as a path and once as "file:///..." - and the second one was a source that does not exist.
					else if ( HasFileOrFolder && line.IndexOf( '.' ) != -1 )
					{
						output.Add( line );
					}
				}

				return output.ToArray();
			}
		}

		//Local path from a file URI drag line, handling Windows, Linux and file://// forms; null otherwise
		internal static string LocalPathOf( string line )
		{
			if ( string.IsNullOrWhiteSpace( line ) ) return null;

			const string scheme = "file://";

			line = line.Trim();
			if ( !line.StartsWith( scheme, StringComparison.OrdinalIgnoreCase ) ) return null;

			var rest = line[scheme.Length..];

			if ( rest.StartsWith( "localhost/", StringComparison.OrdinalIgnoreCase ) )
				rest = rest["localhost".Length..];

			var written = Rooted( rest );
			if ( System.IO.File.Exists( written ) || System.IO.Directory.Exists( written ) ) return written;

			var decoded = Rooted( Uri.UnescapeDataString( rest ) );
			return System.IO.File.Exists( decoded ) || System.IO.Directory.Exists( decoded ) ? decoded : written;
		}

		//A drive letter as it stands; anything else rooted once, however many slashes it came with.
		static string Rooted( string rest )
		{
			var trimmed = rest.TrimStart( '/' );

			if ( trimmed.Length >= 2 && char.IsAsciiLetter( trimmed[0] ) && trimmed[1] == ':' )
				return trimmed;

			return "/" + trimmed;
		}

		/// <summary>Helper for finding instances of type <typeparamref name="T"/> in <see cref="Object"/>. Will find matches if <see cref="Object"/> is of type <typeparamref name="T"/>, is an <see cref="IEnumerable"/> with <typeparamref name="T"/> items, or a <see cref="SerializedObject"/> with <typeparamref name="T"/> targets.</summary>
		public IEnumerable<T> OfType<T>()
		{
			return Object switch
			{
				SerializedObject { Targets: { } targets } => targets.OfType<T>(),
				IEnumerable enumerable => typeof( T ) == typeof( object ) ? enumerable.Cast<T>() : enumerable.OfType<T>(),
				T value => new[] { value },
				_ => Enumerable.Empty<T>()
			};
		}

		/// <summary>Helper for finding instances of type <paramref name="type"/> in <see cref="Object"/>. Will find matches if <see cref="Object"/> is of type <paramref name="type"/>, is an <see cref="IEnumerable"/> with <paramref name="type"/> items, or a <see cref="SerializedObject"/> with <paramref name="type"/> targets.</summary>
		public IEnumerable<object> OfType( Type type )
		{
			return Object switch
			{
				SerializedObject { Targets: { } targets } => targets.Where( type.IsInstanceOfType ),
				IEnumerable enumerable => type == typeof( object ) ? enumerable.Cast<object>() : enumerable.OfType<object>().Where( type.IsInstanceOfType ),
				_ when type.IsInstanceOfType( Object ) => new[] { Object },
				_ => Enumerable.Empty<object>()
			};
		}
	}
}
