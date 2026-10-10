namespace Editor.Preferences;

//Everything the sidebar draws comes off the page's own attributes, so a page is added by writing one - not by editing a list somewhere else.
/// <summary>A preferences page as the navigation knows it: what it is called, and how to make one.</summary>
public sealed class PreferencesPageDescriptor
{
	/// <summary>Where a page that names no [Group] of its own is listed.</summary>
	public const string DefaultSection = "Editor";

	readonly TypeDescription type;

	internal PreferencesPageDescriptor( TypeDescription type )
	{
		this.type = type;

		Title = string.IsNullOrEmpty( type.Title ) ? type.Name : type.Title;
		Icon = string.IsNullOrEmpty( type.Icon ) ? "settings" : type.Icon;
		Section = string.IsNullOrEmpty( type.Group ) ? DefaultSection : type.Group;
		Description = type.Description ?? "";
		Order = type.Order;
	}

	public string Title { get; }
	public string Icon { get; }
	public string Section { get; }
	public string Description { get; }
	public int Order { get; }

	/// <summary>Whether this is the page some other part of the editor asked to open.</summary>
	public bool Is( Type pageType ) => type.TargetType == pageType;

	public bool Is<T>() where T : PreferencesPage => Is( typeof( T ) );

	/// <summary>A page of this kind. Null where the type refuses to make one.</summary>
	public PreferencesPage Create() => type.Create<PreferencesPage>();
}
