using Facepunch.ActionGraphs;
using System;
using System.Reflection;

namespace Editor;

/// <summary>A control widget is used to edit the value of a single SerializedProperty.</summary>
public abstract partial class ControlWidget : Widget
{
	static Logger log = new Logger( "ControlWidget" );

	public static Color ControlHighlightPrimary = "#77BBFF";
	public static Color ControlHighlightSecondary = "#8DB53E";

	public SerializedProperty SerializedProperty { get; private set; }

	/// <summary>If none, when in a grid, the control will fill the entire cell</summary>
	public virtual TextFlag CellAlignment => TextFlag.None;

	public ControlWidget( SerializedProperty property ) : this()
	{
		ArgumentNullException.ThrowIfNull( property, "SerializedProperty" );

		SerializedProperty = property;
		ToolTip = property.Description ?? property.DisplayName;
		MinimumWidth = 60;

		//Questionable rule: should probably be readonly only with an explicit [ReadOnly]
		if ( !property.IsEditable && property.PropertyType.IsValueType || property.HasAttribute<ReadOnlyAttribute>() || !property.ShouldEnable() )
			base.ReadOnly = true;

		if ( property.TryGetAttribute<TintAttribute>( out var tintAttribute ) )
		{
			Tint = Theme.GetTint( tintAttribute.Tint );
		}
	}

	internal ControlWidget() : base( null )
	{
		HorizontalSizeMode = SizeMode.CanShrink;
		VerticalSizeMode = SizeMode.CanShrink;
	}

	/// <summary>Selects this widget and starts editing. Used when we want to focus on the widget in the inspector, like when double-clicking on something in a graph editor that maps to this widget.</summary>
	public virtual void StartEditing()
	{
		Focus();
	}

	/// <summary>If true we prefer to be full inspector width with the label above us</summary>
	public virtual bool IsWideMode => false;

	/// <summary>If true (default) we'll include a label next to the control</summary>
	public virtual bool IncludeLabel => true;
	public virtual bool IsControlActive => IsFocused;
	public virtual bool IsControlHovered => IsUnderMouse && Enabled && !ReadOnly;
	public virtual bool IsControlDisabled => !Enabled || ReadOnly;
	public virtual bool IsControlButton => false;
	public Color Tint { get; set; } = Color.White;
	public virtual bool SupportsMultiEdit => false;
	public bool PaintBackground = true;

	protected override Vector2 MinimumSizeHint()
	{
		return Theme.RowHeight;
	}

	//Controls without a layout are one sheet row tall; ones with a layout are measured by it
	protected virtual bool IsOneRow => Layout is null;

	protected override Vector2 SizeHint()
	{
		var size = base.SizeHint();
		size.x = 1000;
		if ( size.y <= 0 && IsOneRow ) size.y = Theme.RowHeight;
		return size;
	}

	protected override void OnPaint()
	{
		if ( !Paint.Snapshot ) return;

		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		PaintUnder();
		PaintControl();
		PaintOver();
	}

	//Where the field's well is drawn: the whole control, unless part of it is something else -
	//a slider beside a number, which stands on nothing, the way the inspector's own slider does.
	protected virtual Rect FieldRect => LocalRect;

	protected virtual void PaintUnder()
	{
		if ( !PaintBackground )
			return;

		var field = FieldRect;

		bool active = IsControlActive;
		bool hovered = IsControlHovered;
		bool read = IsControlDisabled;

		Paint.ClearPen();

		//Button-style controls (pickers, dropdowns) get the same field plate as every other row
		if ( IsControlButton )
		{
			Paint.SetBrush( hovered ? Theme.Tile : Theme.Sunken );
			Paint.DrawRect( field, Theme.ControlRadius );

			Paint.ClearBrush();
			Paint.SetPen( hovered ? Theme.EdgeLit : Theme.Edge );
			Paint.DrawRect( field, Theme.ControlRadius );
			return;
		}

		//Everything else is a field: a hole in the row, with a hairline round it.
		Paint.SetBrush( active ? Theme.Base : Theme.Sunken );
		Paint.DrawRect( field, Theme.ControlRadius );

		//And the hairline says what is going on: white while it is being typed into, brighter under the pointer, faint the rest of the time.
		Paint.ClearBrush();
		Paint.SetPen( read ? Theme.Edge.WithAlphaMultiplied( 0.5f )
			: active ? Theme.EdgeFocus
			: hovered ? Theme.EdgeLit
			: Theme.Edge );

		Paint.DrawRect( field, Theme.ControlRadius );
	}

	protected virtual void PaintControl()
	{

	}

	protected virtual void PaintOver()
	{
		bool active = IsControlActive;
		bool hovered = IsControlHovered;

		//Drop highlight; no hover check since native hover doesn't track during a drag
		if ( IsBeingDroppedOn )
		{
			Paint.SetPen( Theme.Good, 1 );
			Paint.SetBrush( Theme.Good.WithAlpha( 0.12f ) );
			Paint.DrawRect( LocalRect.Shrink( 1 ), Theme.ControlRadius );
			return;
		}
	}

	[EditorEvent.Hotload]
	static void FlushCache()
	{
		editorAttributes = null;
		candidates = null;
	}

	static (TypeDescription Type, CustomEditorAttribute Attribute)[] editorAttributes;

	//Scratch for scoring, so choosing an editor allocates nothing.
	[System.ThreadStatic]
	static List<(float Score, TypeDescription Type)> candidates;

	public static ControlWidget Create( SerializedProperty property )
	{
		if ( property is null )
		{
			return new InvalidPropertyControlWidget();
		}

		ArgumentNullException.ThrowIfNull( property );

		var type = property.PropertyType;

		editorAttributes ??= EditorTypeLibrary.GetTypesWithAttribute<CustomEditorAttribute>( false )
					.Where( x => x.Type.TargetType.IsAssignableTo( typeof( ControlWidget ) ) )
					.ToArray();

		//Scored in one pass into a reused list; this runs for every property in the inspector
		var scored = candidates ??= new List<(float Score, TypeDescription Type)>();

		scored.Clear();

		foreach ( var (editor, attribute) in editorAttributes )
		{
			var score = attribute.GetEditorScore( property );

			if ( score <= 0 ) continue;

			scored.Add( (score, editor) );
		}

		scored.Sort( static ( a, b ) => b.Score.CompareTo( a.Score ) );


		//Use the first editor we can successfully create
		foreach ( var entry in scored )
		{
			var c = entry.Type.Create<ControlWidget>( new[] { property } );
			if ( c is not null )
			{
				if ( property.IsMultipleValues && !c.SupportsMultiEdit )
				{
					c.Destroy();
					return new MultiEditNotSupported( property );
				}

				c.Prime();
				return c;
			}
		}

		//Nope - sorry, nothing for you
		if ( property.IsMethod )
			return null;

		//The editor may have moved to a panel and left no widget behind, which is where every migrated control
		//ends up. Asked before the generic editor so a real panel editor beats a foldout of raw fields.
		var panel = TryCreatePanelControlWidget( property );
		if ( panel is not null ) return panel;

		var w = TryCreateGenericObjectControlWidget( property );
		if ( w is not null ) return w;

		return new MissingSerializedPropertyWidget( property );
	}

	//Found by name, the way the generic editor is: the host lives in the tools addon, which is built on top of this.
	/// <summary>A widget wrapping the panel editor for this property, if there is one.</summary>
	public static ControlWidget TryCreatePanelControlWidget( SerializedProperty property )
	{
		if ( property is null || property.IsMethod )
			return null;

		var foundType = EditorTypeLibrary.GetType<ControlWidget>( "PanelControlWidget" );
		if ( foundType is null ) return null;

		var widget = foundType.Create<ControlWidget>( new[] { property } );

		if ( widget is IPanelControlHost { HasPanelControl: true } )
		{
			if ( property.IsMultipleValues && !widget.SupportsMultiEdit )
			{
				widget.Destroy();
				return new MultiEditNotSupported( property );
			}

			widget.Prime();
			return widget;
		}

		widget?.Destroy();
		return null;
	}

	public static ControlWidget TryCreateGenericObjectControlWidget( SerializedProperty property )
	{
		if ( property.IsMethod )
			return null;

		//Is this appropriate for the GenericControlWidget?

		//primitive, nope
		if ( property.PropertyType.IsPrimitive ) return null;

		//readonly struct
		if ( property.PropertyType.IsValueType && property.PropertyType.GetCustomAttribute<System.Runtime.CompilerServices.IsReadOnlyAttribute>() is not null ) return null;


		var foundType = EditorTypeLibrary.GetType<ControlWidget>( "GenericControlWidget" );
		if ( foundType is null ) return default;

		var w = foundType.Create<ControlWidget>( new[] { property } );

		return w;
	}

	protected virtual int ValueHash => HashCode.Combine( this, SerializedProperty?.GetValue<object>() );

	[EditorEvent.Frame]
	public virtual void Think()
	{
		if ( !Visible )
			return;

		if ( SetContentHash( ValueHash, 0.1f ) )
		{
			OnValueChanged();
			CheckDifferentState();
		}
	}

	/// <summary>Should get called right after creation</summary>
	public void Prime()
	{
		SetContentHash( ValueHash, -1000.0f );
		OnValueChanged();
		UpdateDifferentState();
	}

	protected void PropertyStartEdit()
	{
		SerializedProperty.NoteStartEdit( SerializedProperty );
	}

	protected void PropertyFinishEdit()
	{
		SerializedProperty.NoteFinishEdit( SerializedProperty );
	}

	protected virtual void OnValueChanged()
	{

	}

	protected override void OnContextMenu( ContextMenuEvent e )
	{
		e.Accepted = true;
	}

	/// <summary>Called when right clicking a label in a ControlSheet for this widget. This allows you to add advanced menu items for this widget at the top of the menu, before the default ones.</summary>
	public virtual void OnLabelContextMenu( ContextMenu menu )
	{

	}

	//ActionGraph serializer needs hints about the GameObject that contains this property.
	private IDisposable PushSerializationOptions()
	{
		if ( SerializedProperty.GetContainingGameObject() is { } gameObject )
		{
			return ActionGraph.PushTarget( InputDefinition.Target( typeof( GameObject ), gameObject ) );
		}

		return null;
	}

	public virtual string ToClipboardString()
	{
		using var optionScope = PushSerializationOptions();

		string str = Json.Serialize( SerializedProperty.GetValue<object>() );
		if ( str?.StartsWith( '"' ) ?? false ) str = str.Substring( 1, str.Length - 2 );
		return str;
	}

	public virtual void FromClipboardString( string clipboard )
	{
		clipboard = clipboard.Trim();

		if ( !clipboard.StartsWith( '{' ) && !clipboard.StartsWith( '[' ) && !clipboard.StartsWith( '"' ) )
			clipboard = $"\"{clipboard}\"";

		using var optionScope = PushSerializationOptions();
		using var uniqueActionGuidScope = ActionGraph.PushMakeGuidsUnique( true );

		if ( Json.TryDeserialize( clipboard, SerializedProperty.PropertyType, out var jsonValue ) && jsonValue is not null )
		{
			SerializedProperty.Parent.NoteStartEdit( SerializedProperty );
			SerializedProperty.SetValue( jsonValue );
			SerializedProperty.Parent.NoteFinishEdit( SerializedProperty );
		}
	}

	bool _multipleDifferent;

	void CheckDifferentState()
	{
		if ( SerializedProperty is null ) //only possible in InvalidPropertyControlWidget
			return;

		if ( _multipleDifferent == SerializedProperty.IsMultipleDifferentValues )
			return;

		UpdateDifferentState();
	}

	void UpdateDifferentState()
	{
		_multipleDifferent = SerializedProperty.IsMultipleDifferentValues;
		OnMultipleDifferentValues( _multipleDifferent );
	}

	protected virtual void OnMultipleDifferentValues( bool state )
	{

	}
}

//Used when there's no defined ControlWidget
file class MissingSerializedPropertyWidget : ControlWidget
{
	private Label valueLabel;

	public MissingSerializedPropertyWidget( SerializedProperty property ) : base( property )
	{
		ReadOnly = true;

		valueLabel = new Label( "Missing Value" );
		valueLabel.SetStyles( "background-color: transparent;" );
		valueLabel.ContentMargins = new Rinten.UI.Margin( 8, 0 );
		valueLabel.FixedHeight = Theme.RowHeight;
		Layout = Layout.Column();
		Layout.Add( valueLabel );
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		if ( valueLabel.IsValid() )
			valueLabel.Text = SerializedProperty.GetValue( "Missing Value" )?.ToString() ?? "Missing Value";

		UpdateGeometry();
	}

	protected override Vector2 SizeHint()
	{
		var text = SerializedProperty.GetValue( "Missing Value" );
		var sh = base.SizeHint();
		sh.x -= 16;
		var rect = Paint.MeasureText( new Rect( 0, sh ), text, TextFlag.LeftTop );

		if ( rect.Height < Theme.RowHeight )
			rect.Height = Theme.RowHeight;

		return rect.Size;
	}
}

//Used when there's no defined ControlWidget
file class InvalidPropertyControlWidget : ControlWidget
{
	Label Label;

	public InvalidPropertyControlWidget() : base()
	{
		ReadOnly = true;
		HorizontalSizeMode = SizeMode.Flexible;

		Label = new Label( "Null Property" );
		Label.WordWrap = true;
		Label.SetStyles( "background-color: transparent;" );
		Label.ContentMargins = new Rinten.UI.Margin( 6, 0 );
		Label.FixedHeight = Theme.RowHeight;

		Layout = Layout.Column();
		Layout.Add( Label );
	}

	protected override Vector2 SizeHint() => new Vector2( 22, Theme.RowHeight );

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		Label.Text = "Null Property";
	}
}


//Used when there's no defined ControlWidget
file class MultiEditNotSupported : ControlWidget
{
	Label Label;

	public MultiEditNotSupported( SerializedProperty property ) : base( property )
	{
		ReadOnly = true;
		HorizontalSizeMode = SizeMode.Flexible;

		Label = new Label( "Multiedit not supported", this );
		Label.WordWrap = true;
		Label.SetStyles( "background-color: transparent;" );
		Label.ContentMargins = new Rinten.UI.Margin( 6, 0 );
		Label.MaximumSize = new Vector2( 4096, Theme.RowHeight );

		Layout = Layout.Column();
		Layout.Add( Label );
	}

	protected override Vector2 SizeHint() => new Vector2( 22, Theme.RowHeight );
}
