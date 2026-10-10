namespace Rinten.Engine;

/// <summary>Action binds per slot, saved via BindSaveConfig; falls back to a Base collection (common binds)</summary>
public class BindCollection
{
	/// <summary>The base collection. Game binds have this set to the common binds.</summary>
	public BindCollection Base { get; set; }

	/// <summary>Will be either "common" or the ident of the current game.</summary>
	public string CollectionName { get; set; }

	/// <summary>The location of the config file to load from in EngineFileSystem.Config</summary>
	public string ConfigPath { get; set; }

	/// <summary>The actual collection of binds.</summary>
	public CaseInsensitiveDictionary<ActionBind> Actions = new CaseInsensitiveDictionary<ActionBind>();

	/// <summary>Creates a collection and tries to load it from disk.</summary>
	public BindCollection( string name )
	{
		CollectionName = name;
		ConfigPath = $"/input/{CollectionName}.json";

		FillDefaultCommonInputs();

		BindSaveConfig.Load( ConfigPath, this );
	}

	//If we're the common collection and have no binds (because we haven't been able to load a
	//customized config from disk) then we'll fill in the defaults based on Input.CommonInputs.
	void FillDefaultCommonInputs()
	{
		if ( CollectionName != "common" ) return;
		if ( Actions.Count != 0 ) return;

		foreach ( var input in Input.CommonInputs )
		{
			if ( !string.IsNullOrWhiteSpace( input.KeyboardCode ) )
			{
				var bind = GetBind( input.Name );
				bind.Set( 0, input.KeyboardCode );
				bind.Default = input.KeyboardCode;
			}
		}
	}

	/// <summary>Get the bind, create if it doesn't exist</summary>
	public ActionBind GetBind( string actionName, bool create = true )
	{
		if ( !Actions.TryGetValue( actionName, out var bind ) )
		{
			if ( create )
			{
				bind = new ActionBind() { Name = actionName };
				Actions[actionName] = bind;
			}
		}

		return bind;
	}

	/// <summary>Set the bind value for this action. This will overwrite what's in this slot.</summary>
	public ActionBind Set( string actionName, int slot, string buttonName )
	{
		var bind = GetBind( actionName );
		bind.Set( slot, buttonName );
		return bind;
	}

	/// <summary>Get the bind value at this slot</summary>
	public string Get( string actionName, int slot )
	{
		Actions.TryGetValue( actionName, out var bind );
		if ( bind == null ) return null;

		var entry = bind.Get( slot );
		return entry.FullString;
	}

	/// <summary>Save the collection to disk</summary>
	public void SaveToDisk()
	{
		BindSaveConfig.Save( ConfigPath, this );
	}

	//Reset the collection to the default values.
	internal void ResetToDefaults()
	{
		if ( CollectionName == "common" )
		{
			Actions.Clear();
			FillDefaultCommonInputs();
		}

		foreach ( var a in Actions )
		{
			a.Value.Set( 0, a.Value.Default );
			a.Value.Set( 1, null );
		}
	}

	//Enumerate all actions that contain this button
	internal IEnumerable<ActionBind> EnumerateWithButton( string button )
	{
		//No actions set (older project) fall back to use common binds
		if ( Actions.Count == 0 )
		{
			if ( Base == null )
				yield break;

			foreach ( var action in Base.EnumerateWithButton( button ) )
			{
				yield return action;
			}

			yield break;
		}

		foreach ( var action in Actions )
		{
			//If this is a common action then check for the action set in the common collection instead. I'm
			//commenting this out for now since it's not letting people override the default binds - Carson
			//if ( Base != null && action.Value.IsCommon && Base.Actions.TryGetValue( action.Key, out var common ) )
			//{
			//if ( common.HasButton( button ) )
			//	yield return common;

			//continue;
			//}

			if ( !action.Value.HasButton( button ) ) continue;

			yield return action.Value;
		}
	}

	//The action list has changed, we just got the config from the server. Here we'll clear the
	//actions, add all of the defaults from the new config and then load the user config if it exists.
	internal void UpdateActions( List<InputAction> inputActions )
	{
		Actions.Clear();

		foreach ( var action in inputActions )
		{
			//if this doesn't have a default bind, skip it
			if ( string.IsNullOrEmpty( action.KeyboardCode ) ) continue;

			var bind = GetBind( action.Name );
			bind.Set( 0, action.KeyboardCode );
			bind.Default = action.KeyboardCode;
			bind.IsCommon = Base?.Actions.ContainsKey( action.Name ) ?? false;
		}

		BindSaveConfig.Load( ConfigPath, this );
	}

	public class ActionBind
	{
		public string Name { get; set; }
		public BindEntry[] Slots = new BindEntry[2];

		/// <summary>If this is set then we want to read the value from the base collection</summary>
		public bool IsCommon { get; set; }

		public string Default { get; set; }

		internal BindEntry Get( int slot )
		{
			return Slots[slot];
		}

		internal void Set( int slot, string buttonString )
		{
			Slots[slot].Set( buttonString );
			IsCommon = false;
		}

		internal bool HasButton( string button )
		{
			return Slots[0].HasButton( button ) || Slots[1].HasButton( button );
		}

		internal bool Test( string button, HashSet<string> activeButtons )
		{
			return Slots[0].Test( button, activeButtons ) || Slots[1].Test( button, activeButtons );
		}
	}

	public struct BindEntry
	{
		public string FullString { get; set; }

		HashSet<string> ButtonList;

		internal bool HasButton( string button )
		{
			return ButtonList?.Contains( button ) ?? false;
		}

		internal void Set( string buttonString )
		{
			buttonString = buttonString ?? "";
			FullString = buttonString;
			ButtonList = FullString.Split( new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		}

		internal bool Test( string button, HashSet<string> activeButtons )
		{
			if ( ButtonList == null ) return false;
			if ( !HasButton( button ) ) return false;
			return ButtonList.IsSubsetOf( activeButtons );
		}
	}

}
