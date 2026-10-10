using Rinten.UI;
using System.Text;

namespace Editor.Gallery;

//The gallery never keeps an instance. Opening a page builds every example on it and leaving the page deletes them, so each visit is a clean constructor and a leak shows up at once.
/// <summary>One example in the gallery: what it is called, where it is listed, and how to build a fresh one.</summary>
public sealed class GalleryEntry
{
	readonly Func<Panel> factory;

	public GalleryEntry( string title, string icon, string group, string description, int order, Func<Panel> factory )
	{
		Title = string.IsNullOrEmpty( title ) ? "Untitled" : title;
		Icon = string.IsNullOrEmpty( icon ) ? "widgets" : icon;
		Group = string.IsNullOrEmpty( group ) ? "Examples" : group;
		Description = description ?? "";
		Order = order;
		Id = MakeId( Group, Title );

		this.factory = factory ?? throw new ArgumentNullException( nameof( factory ) );
	}

	/// <summary>How the console and the tests name this example: <c>page/title</c>, lower case, words joined by dashes.</summary>
	public string Id { get; }
	public string Title { get; }
	public string Icon { get; }
	public string Group { get; }
	public string Description { get; }
	public int Order { get; }

	/// <summary>Whether this is a widget standing in a panel, listed until its widget is gone.</summary>
	public bool IsLegacyWidget { get; init; }

	/// <summary>Whether building it needs a live window around it. See <see cref="PanelGalleryAttribute.RequiresWindow"/>.</summary>
	public bool RequiresWindow { get; init; }

	/// <summary>A new instance. Never the same one twice.</summary>
	public Panel Create() => factory();

	public static string MakeId( string group, string title ) => $"{Slug( group )}/{Slug( title )}";

	static string Slug( string text )
	{
		var sb = new StringBuilder( text.Length );
		var dash = false;

		foreach ( var c in text.Trim().ToLowerInvariant() )
		{
			if ( char.IsLetterOrDigit( c ) )
			{
				sb.Append( c );
				dash = false;
			}
			else if ( !dash && sb.Length > 0 )
			{
				sb.Append( '-' );
				dash = true;
			}
		}

		return sb.ToString().TrimEnd( '-' );
	}

	public override string ToString() => Id;
}
