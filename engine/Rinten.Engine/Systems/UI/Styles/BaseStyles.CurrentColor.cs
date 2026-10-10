namespace Rinten.UI;

public abstract partial class BaseStyles
{
	//Set when a value used currentColor, resolved after FillDefaults once the font colour is known
	internal bool HasCurrentColor;

	//Replaces any colour field holding the Color.CurrentColor sentinel with the element's computed font colour. color: currentColor itself
	//means the inherited colour, so the font colour resolves against the parent. Must run after FillDefaults so the font colour is final.
	internal void ResolveCurrentColor( BaseStyles parent )
	{
		//'color: currentColor' resolves to the inherited colour (the parent's, or the default).
		if ( _fontcolor.HasValue && Color.IsCurrentColor( _fontcolor.Value ) )
			_fontcolor = parent != null ? parent._fontcolor : InitialValues._fontcolor;

		var current = _fontcolor ?? Color.Black;

		ResolveCurrentColor( ref _backgroundcolor, current );
		ResolveCurrentColor( ref _borderleftcolor, current );
		ResolveCurrentColor( ref _bordertopcolor, current );
		ResolveCurrentColor( ref _borderrightcolor, current );
		ResolveCurrentColor( ref _borderbottomcolor, current );
		ResolveCurrentColor( ref _caretcolor, current );
		ResolveCurrentColor( ref _textdecorationcolor, current );
		ResolveCurrentColor( ref _textstrokecolor, current );
		ResolveCurrentColor( ref _outlinecolor, current );
		ResolveCurrentColor( ref _filtertint, current );
		ResolveCurrentColor( ref _filterbordercolor, current );
		ResolveCurrentColor( ref _backgroundtint, current );
		ResolveCurrentColor( ref _borderimagetint, current );
		ResolveCurrentColor( ref _fill, current );
		ResolveCurrentColor( ref _stroke, current );
	}

	//Swaps a single colour field from the currentColor sentinel to the resolved colour.
	static void ResolveCurrentColor( ref Color? field, Color current )
	{
		if ( field.HasValue && Color.IsCurrentColor( field.Value ) )
			field = current;
	}
}
