using Rinten.UI;

namespace Editor.Gallery;

//A method returning a Widget is still listed, on the legacy page, wrapped in a LegacyWidgetHost. That page is meant to empty out: an example leaves it by returning a panel, or by being deleted with its widget.
/// <summary>The examples written by hand: every static method marked [PanelGallery].</summary>
public sealed class AttributeGallerySource : IGalleryEntrySource
{
	public const string LegacyGroup = "Legacy Widgets";

	//Legacy examples sort after everything written for panels.
	const int LegacyOrder = 10000;

	public IEnumerable<GalleryEntry> Entries()
	{
		foreach ( var (method, attribute) in EditorTypeLibrary.GetMethodsWithAttribute<PanelGalleryAttribute>() )
		{
			if ( method.Parameters.Length != 0 )
			{
				Log.Warning( $"Panel Gallery: {method.TypeDescription?.Name}.{method.Name} takes parameters and cannot be an example" );
				continue;
			}

			var entry = Describe( method, attribute );

			if ( entry is not null )
				yield return entry;
		}
	}

	static GalleryEntry Describe( MethodDescription method, PanelGalleryAttribute attribute )
	{
		var title = string.IsNullOrEmpty( method.Title ) ? method.Name : method.Title;
		var returns = method.ReturnType;

		if ( typeof( Panel ).IsAssignableFrom( returns ) )
		{
			return new GalleryEntry( title, method.Icon, method.Group, method.Description, method.Order,
				() => method.InvokeWithReturn<Panel>( null ) )
			{
				RequiresWindow = attribute.RequiresWindow,
			};
		}

		if ( typeof( Widget ).IsAssignableFrom( returns ) )
		{
			return new GalleryEntry( title, method.Icon, LegacyGroup, method.Description, LegacyOrder + method.Order,
				() => new LegacyWidgetHost( method.InvokeWithReturn<Widget>( null ) ) )
			{
				IsLegacyWidget = true,
				//A widget needs the editor's window system around it; there is none in a headless test.
				RequiresWindow = true,
			};
		}

		Log.Warning( $"Panel Gallery: {method.TypeDescription?.Name}.{method.Name} returns {returns?.Name}, which is neither a Panel nor a Widget" );
		return null;
	}
}
