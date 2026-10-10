using Rinten.Internal;
using System.Linq;
using System.Reflection;

namespace Rinten.UI;

public partial class Panel
{
	//Stylesheets from this panel's types, base class first, so subclass sheets add to their base's
	readonly List<string> loadedTemplateStylesheets = new();

	static Internal.ClassFileLocationAttribute FileLocation( TypeDescription type, Type runtime ) =>
		type?.GetAttributes<Internal.ClassFileLocationAttribute>().MinBy( x => x.Path.Length )
		?? runtime.GetCustomAttributes<Internal.ClassFileLocationAttribute>( inherit: false ).MinBy( x => x.Path.Length );

	static StyleSheetAttribute SheetAttribute( TypeDescription type, Type runtime ) =>
		type?.GetAttribute<StyleSheetAttribute>( false )
		?? runtime.GetCustomAttribute<StyleSheetAttribute>( inherit: false );

	internal void ReloadTemplateStyleSheetWhenParented()
	{
		if ( loadedTemplateStylesheets.Count == 0 )
			return;

		//Replayed in the order they were taken, so the derived sheet still lands after the base one and keeps winning where the two say the same thing.
		var paths = loadedTemplateStylesheets.ToArray();

		loadedTemplateStylesheets.Clear();

		foreach ( var path in paths )
			LoadStyleSheetFromPath( path, true );
	}

	private void LoadStyleSheet()
	{
		if ( LoadStyleSheetFromAttribute() )
			return;

		if ( LoadStyleSheetAuto() )
			return;
	}

	//Loads a stylesheet from one specified within a [StyleSheet] attribute. Every type this panel is, base class first, up to and including Panel itself.
	static IEnumerable<Type> StyledTypes( Type runtime )
	{
		var chain = new List<Type>();

		for ( var type = runtime; type is not null && typeof( Panel ).IsAssignableFrom( type ); type = type.BaseType )
			chain.Add( type );

		chain.Reverse();

		return chain;
	}

	//Stylesheets declared per panel type, base first, cached weakly per type so hotload gets fresh ones
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, string[]> declaredSheets = new();

	//The sheet named after each panel type's own file, or none - see declaredSheets.
	static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, AutoSheet> autoSheets = new();

	sealed record AutoSheet( string Path );

	static string[] SheetsDeclaredBy( Type runtime )
	{
		//Base first, so a derived sheet is loaded after the sheet it is adding to and wins where both describe the same thing.
		var wanted = new List<string>();

		foreach ( var type in StyledTypes( runtime ) )
		{
			//Asked of each type rather than of the instance: the question is what this class declared, not what it inherited.
			var described = TypeLibrary.Editor?.GetType( type ) ?? Game.TypeLibrary?.GetType( type );
			var attr = SheetAttribute( described, type );

			if ( attr is null ) continue;

			//The sheet sits beside the file that declared it, which is the base class's file for a base class's sheet - not the concrete type's.
			var location = FileLocation( described, type );

			if ( attr.Name is null && location is null )
			{
				Log.Warning( $"{runtime} has [StyleSheet] but ClassFileLocation wasn't generated!" );
				continue;
			}

			wanted.Add( BaseFileSystem.NormalizeFilename( GetFullPath( attr.Name, location ) ) );
		}

		return wanted.ToArray();
	}

	private bool LoadStyleSheetFromAttribute()
	{
		var wanted = declaredSheets.GetValue( GetType(), SheetsDeclaredBy );

		//Whatever we were wearing and no longer want - a hotload can change which types declare what.
		for ( var at = loadedTemplateStylesheets.Count - 1; at >= 0; --at )
		{
			var path = loadedTemplateStylesheets[at];

			if ( System.Array.IndexOf( wanted, path ) >= 0 ) continue;

			StyleSheet.Remove( path );
			loadedTemplateStylesheets.RemoveAt( at );
		}

		foreach ( var path in wanted )
			LoadStyleSheetFromPath( path, false );

		return wanted.Length > 0;
	}

	//Loads a stylesheet from one based on the class name.
	private bool LoadStyleSheetAuto()
	{
		var found = autoSheets.GetValue( GetType(), static runtime =>
		{
			var type = TypeLibrary.Editor?.GetType( runtime ) ?? Game.TypeLibrary?.GetType( runtime );
			var classFileLocation = FileLocation( type, runtime );

			//Couldn't find a stylesheet w/ the class name, but this isn't an error, fail silently.
			return new AutoSheet( classFileLocation is null ? null : GetFullPath( null, classFileLocation ) );
		} );

		if ( found.Path is null ) return false;

		return LoadStyleSheetFromPath( found.Path, true );
	}

	//Loads a stylesheet from the specified path. Takes one sheet, and says so. Adds rather
	//than replaces: a panel wears one sheet per type it is, and they are loaded in turn.
	private bool LoadStyleSheetFromPath( string path, bool failSilently )
	{
		path = BaseFileSystem.NormalizeFilename( path );

		//Already wearing it.
		if ( loadedTemplateStylesheets.Contains( path ) )
			return true;

		loadedTemplateStylesheets.Add( path );
		StyleSheet.Load( path, true, failSilently );

		return true;
	}

	private static string GetFullPath( string path, Internal.ClassFileLocationAttribute classFileLocation )
	{
		if ( string.IsNullOrWhiteSpace( path ) && classFileLocation != null )
		{
			return classFileLocation.Path + ".scss";
		}
		else if ( classFileLocation != null && (!path.StartsWith( '/' ) && !path.StartsWith( '\\' )) )
		{
			var newpath = System.IO.Path.GetDirectoryName( classFileLocation.Path );
			newpath = System.IO.Path.Combine( newpath, path );
			return newpath;
		}

		return path;
	}

	/// <summary>TODO: Obsolete this and instead maybe we have something like [PanelSlot( "slotname" )] that is applied on properties. Then when we find a slot="slotname" we chase up the heirachy and set the property.</summary>
	public virtual void OnTemplateSlot( Html.INode element, string slotName, Panel panel )
	{
		Parent?.OnTemplateSlot( element, slotName, panel );
	}
}
