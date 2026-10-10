namespace Editor.Inspectors;

using UILabel = Rinten.UI.Label;

//A label or an icon in the column at the left of a field, saying what the field is. Given something to move it also becomes the grip the old widgets had: press it and drag sideways and the value follows the pointer.
/// <summary>Shared prefix handle for inspector value controls.</summary>
public class ControlHandle : Panel
{
	/// <summary>The class the handle wears while there is something to drag.</summary>
	public const string DraggableClass = "draggable";

	//Made the first time a handle is given something to drag, and never for one that is not.
	DragAdjustGesture _gesture;

	/// <summary>The value a sideways drag over this handle moves. Null - the default - for a handle that only says what its field is.</summary>
	public IDragAdjustable Adjustable
	{
		get => _gesture?.Adjustable;
		set
		{
			if ( value is null && _gesture is null )
				return;

			_gesture ??= new DragAdjustGesture( this );
			_gesture.Adjustable = value;

			SetClass( DraggableClass, value is not null );
		}
	}

	public static ControlHandle AddTo( Panel parent, string text = null, string icon = null, Color? color = null, string tooltip = null )
	{
		var prefix = parent.AddChild<ControlHandle>( "prefix" );

		if ( !string.IsNullOrEmpty( tooltip ) )
			prefix.Tooltip = tooltip;

		var label = prefix.AddChild<UILabel>( "label" );
		label.Tokenize = false;
		label.Style.FontColor = color ?? Theme.Green;

		if ( !string.IsNullOrEmpty( icon ) )
		{
			label.AddClass( "material-icon" );
			label.Text = icon;
		}
		else
		{
			label.Text = text ?? string.Empty;
		}

		return prefix;
	}
}
