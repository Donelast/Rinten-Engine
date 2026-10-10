namespace Editor.Inspectors;

/// <summary>Inspector button for <see cref="ButtonAttribute"/> methods.</summary>
[StyleSheet( "ButtonControl.cs.scss" )]
[CustomEditor( typeof( void ), ForMethod = true, WithAllAttributes = [ typeof( ButtonAttribute ) ] )]
public class ButtonControl : BaseControl
{
	Rinten.UI.Button _button;

	public override bool SupportsMultiEdit => true;
	public override bool IncludeLabel => false;

	public override void Rebuild()
	{
		DeleteChildren();
		_button = null;

		if ( Property is null || !Property.TryGetAttribute<ButtonAttribute>( out var attr ) )
			return;

		var label = string.IsNullOrEmpty( attr.Title ) ? Property.DisplayName : attr.Title;
		var icon = string.IsNullOrEmpty( attr.Icon ) ? null : attr.Icon;

		_button = new Rinten.UI.Button( label, icon, InvokeMethod );
		AddChild( _button );
		_button.Tooltip = Property.Description;
		_button.Disabled = IsReadOnly;
	}

	protected override void OnReadOnlyChanged()
	{
		if ( _button.IsValid() )
			_button.Disabled = IsReadOnly;
	}

	void InvokeMethod()
	{
		if ( Property is null || IsReadOnly )
			return;

		var session = SceneEditorSession.Resolve( Property.GetContainingGameObject() ) ?? SceneEditorSession.Active;
		using ( session.Scene.Push() )
		{
			if ( Property.IsMultipleValues )
			{
				foreach ( var prop in Property.MultipleProperties )
					InvokeAndLog( prop );
			}
			else
				InvokeAndLog( Property );
		}
	}

	static void InvokeAndLog( SerializedProperty property )
	{
		var result = property.Invoke();

		if ( property.PropertyType != typeof( void ) )
			Log.Info( $"Method {property.DisplayName} returned {result}" );
	}
}
