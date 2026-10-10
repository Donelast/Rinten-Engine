namespace Editor.Inspectors;

using Rinten.UI;
using Rinten.UI.Construct;

/// <summary>Inline object editor and key-property rows (panel path).</summary>
[StyleSheet( "GenericControl.cs.scss" )]
public class GenericControl : BaseControl
{
	public override bool SupportsMultiEdit => true;

	Panel body;
	ControlSheet inlineSheet;
	GenericOpener opener;

	public GenericControl()
	{
		AddClass( "no-background" );
	}

	public override void Rebuild()
	{
		if ( Property is null ) return;

		if ( Property.TryGetAttribute<InlineEditorAttribute>( out _ ) )
		{
			inlineSheet ??= AddChild<ControlSheet>();
			if ( Property.TryGetAsObject( out var so ) )
			{
				inlineSheet.Target = so;
				inlineSheet.Rebuild();
			}
			return;
		}

		body ??= AddChild<Panel>( "body" );
		body.DeleteChildren();
		opener = null;

		if ( Property.TryGetAsObject( out var serialized ) )
		{
			var keys = serialized.Where( x => x.HasAttribute<KeyPropertyAttribute>() ).ToArray();
			foreach ( var key in keys )
			{
				var c = PanelControlFactory.CreateFor( key );
				if ( c is null ) continue;
				body.AddChild( c );
			}

			if ( ShouldShowOpener( serialized ) )
				CreateOpener( keys.Length == 0 );
		}
		else if ( ShouldShowOpener( null ) )
			CreateOpener( true );
	}

	//Opener text updates only when the value changes, not every frame
	protected override void OnPropertyValueChanged()
	{
		UpdateOpener();
	}

	protected override void OnReadOnlyChanged()
	{
		if ( opener.IsValid() )
			opener.Disabled = IsReadOnly;
	}

	void CreateOpener( bool alone )
	{
		opener = body.AddChild( new GenericOpener( this ) );
		opener.SetClass( "alone", alone );
		//Beside a key property the opener is a button, not a second field: the value's text is the key's row and
		//more, and at its own width it wrapped under the key and ran out over the rows below. It goes in the tooltip.
		opener.SetClass( "beside", !alone );
		UpdateOpener();
		opener.Disabled = IsReadOnly;
	}

	void UpdateOpener()
	{
		if ( !opener.IsValid() ) return;

		var (text, icon) = GetDisplayTextAndIcon();
		//Beside a key property the text goes in the tooltip rather than on the button - Button
		//shows its text column whenever it has text, inline, over anything a stylesheet says.
		var beside = opener.HasClass( "beside" );
		opener.Text = beside ? null : text;
		opener.Icon = icon;
		opener.Tooltip = beside ? text : null;
	}

	static bool ShouldShowOpener( SerializedObject serialized )
	{
		if ( serialized is null ) return true;

		if ( !serialized.Any( x => x.HasAttribute<KeyPropertyAttribute>() ) )
			return true;

		return serialized.Any( x => x.IsEditable && x.ShouldShow() && !x.IsMethod && !x.HasAttribute<KeyPropertyAttribute>() );
	}

	(string text, string icon) GetDisplayTextAndIcon()
	{
		var type = Property.IsNullable ? Property.NullableType : Property.PropertyType;
		var value = Property.GetValue<object>();
		type = value?.GetType() ?? type;

		var shown = Shown.Of( type );
		var text = shown.OwnText ? value?.ToString() ?? shown.Name : shown.Name;

		return (text, shown.Icon);
	}

	//What a type is called and drawn as, and whether it says its own text - worked out once per type, since none of it changes while the
	//type exists. Held weakly by the type, so a type a hotload replaced takes its entry with it rather than keeping its assembly loaded.
	sealed record Shown( string Name, string Icon, bool OwnText )
	{
		static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, Shown> known = new();

		public static Shown Of( Type type ) => known.GetValue( type, static t =>
		{
			var info = DisplayInfo.ForType( t );

			return new Shown( info.Name, info.Icon ?? "edit_note",
				t.GetMethod( "ToString", Type.EmptyTypes )?.DeclaringType != typeof( object ) );
		} );
	}

	SerializedObject GetPopupTarget()
	{
		if ( Property is null ) return null;

		if ( Property.TryGetAsObject( out var so ) )
			return so;

		return Property.GetValue<object>()?.GetSerialized();
	}

	private sealed class GenericOpener : PopupButton
	{
		readonly GenericControl _owner;

		public GenericOpener( GenericControl owner )
		{
			_owner = owner;
			AddClass( "dropdown" );
			Add.Icon( "expand_more", "dropdown_indicator" );
		}

		public override void Open()
		{
			var target = _owner.GetPopupTarget();
			if ( target is null ) return;

			Popup = new Popup();
			//Under the whole row: a popup stretched to a button the width of an icon would be too narrow to edit anything in.
			Popup.SetPositioning( HasClass( "beside" ) ? _owner : this, Popup.PositionMode.BelowStretch, 2 );
			Popup.CloseWhenParentIsHidden = true;
			Popup.AddClass( "editor-popup" );
			Popup.AddClass( "generic-control-popup" );
			Popup.AddClass( "inspector" );

			var sheet = Popup.AddChild<ControlSheet>();
			sheet.Target = target;
			Popup.SyncAnchor();
		}
	}
}
