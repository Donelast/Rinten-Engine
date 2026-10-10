using System.Reflection;

namespace Rinten.UI;

public partial class Panel
{
	//True when a bind has changed and OnParametersSet call is pending a call

	bool templateBindsChanged = true;
	Task parametersSetTask;

	internal void ParametersChanged( bool immediately )
	{
		templateBindsChanged = true;
		RequestTick();

		//task is still running
		if ( parametersSetTask != null && !parametersSetTask.IsCompleted )
			return;

		if ( immediately )
		{
			templateBindsChanged = false;

			parametersSetTask = OnParametersSetInternalAsync();
		}
	}

	internal async Task OnParametersSetInternalAsync()
	{
		try
		{
			await OnParametersSetAsync();
		}
		catch ( OperationCanceledException )
		{
			return;
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception in OnParametersSetAsync: {e.Message}" );
		}

		if ( !IsValid )
			return;

		try
		{
			OnParametersSet();
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Exception in OnParametersSet: {e.Message}" );
		}

		StateHasChanged();
	}

	/// <summary>Same as <see cref="SetProperty"/>, but first tries to set the property on the panel object, then process any special properties such as <c>class</c>.</summary>
	public virtual void SetPropertyObject( string name, object value )
	{
		var prop = GetType().GetProperty( name, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy );

		if ( prop != null && prop.PropertyType.IsAssignableFrom( value?.GetType() ) )
		{
			prop.SetValue( this, value );
			return;
		}

		SetProperty( name, Convert.ToString( value ) );
	}

	string previousPropertyClass;

	/// <summary>Set a property on the panel, such as special properties (<c>class</c>, <c>id</c>, <c>style</c> and <c>value</c>, etc.) and properties of the panel's C# class.</summary>
	public virtual void SetProperty( string name, string value )
	{
		if ( name == "id" )
		{
			Id = value;
			return;
		}

		if ( name == "value" )
		{
			StringValue = value;
			return;
		}

		if ( name == "class" )
		{
			if ( !string.IsNullOrEmpty( previousPropertyClass ) )
			{
				RemoveClass( previousPropertyClass );
			}

			previousPropertyClass = value;
			AddClass( value );
			return;
		}

		if ( name == "style" )
		{
			Style.Set( value );
			return;
		}

		//The panel's own property, set here rather than left to the type library: that is the game's, and knows none of the editor's
		//panels - a toolbar button's tooltip="..." was kept as an attribute nothing reads, and the scene view's buttons explained nothing.
		if ( string.Equals( name, "tooltip", StringComparison.OrdinalIgnoreCase ) )
		{
			Tooltip = value;
			return;
		}

		SetAttribute( name, value );

		//No library in a process with no game in it - the editor's own panels run there, and an attribute it cannot look up is one it just keeps.
		Game.TypeLibrary?.SetProperty( this, name, value );
	}

	Dictionary<string, string> _attributes;

	/// <summary>Used in templates, gets an attribute that was set in the template.</summary>
	public void SetAttribute( string k, string v )
	{
		if ( string.IsNullOrEmpty( k ) ) return;

		_attributes ??= [];
		_attributes[k] = v;
	}

	/// <summary>Used in templates, try to get the attribute that was set in creation.</summary>
	public string GetAttribute( string k, string defaultIfNotFound = default )
	{
		if ( _attributes == null ) return defaultIfNotFound;

		if ( _attributes.TryGetValue( k, out var v ) )
			return v;

		return defaultIfNotFound;
	}

	//Called after all templated panel binds have been set.
	protected virtual void OnParametersSet()
	{
		//Log.Info( $"{this} - OnParametersSet" );
	}

	//Called after all templated panel binds have been set.
	protected virtual Task OnParametersSetAsync()
	{
		return Task.CompletedTask;
	}

	/// <summary>Called by the templating system when an element has content between its tags.</summary>
	public virtual void SetContent( string value )
	{

	}
}
