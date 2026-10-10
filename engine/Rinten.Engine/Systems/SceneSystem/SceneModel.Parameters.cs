namespace Rinten;

/// <summary>A model scene object that supports animations and can be rendered within a <see cref="SceneWorld"/>.</summary>
public sealed partial class SceneModel : SceneObject
{
	//Counts up every time the graph underneath is built again - which is every time the model changes, and a character changes model the moment it
	//draws a weapon. A graph built again starts at its defaults, so whoever set a parameter on the old one has to say it again. Read this to notice.
	internal int GraphGeneration => Animation.GraphGeneration;

	/// <summary>Sets a boolean animation graph parameter by name.</summary>
	public void SetAnimParameter( string name, bool value ) => Animation.SetParameter( name, value );

	/// <summary>Sets a float animation graph parameter by name.</summary>
	public void SetAnimParameter( string name, float value ) => Animation.SetParameter( name, value );

	/// <summary>Sets an enum animation graph parameter by option name (e.g. "pistol" on "holdtype").</summary>
	public bool SetAnimParameter( string name, string option ) => Animation.SetParameter( name, option );

	/// <summary>Sets a vector animation graph parameter by name.</summary>
	public void SetAnimParameter( string name, Vector3 value ) => Animation.SetParameter( name, value );

	/// <summary>Sets a integer animation graph parameter by name.</summary>
	public void SetAnimParameter( string name, int value ) => Animation.SetParameter( name, value );

	/// <summary>Sets a rotation animation graph parameter by name.</summary>
	public void SetAnimParameter( string name, Rotation value ) => Animation.SetParameter( name, value );

	/// <summary>Reset all animgraph parameters to their default values. Somewhere for the graph to reach for, in the world.</summary>
	public void SetAnimParameter( string name, Transform value ) => Animation.SetParameter( name, value );

	/// <summary>The same, following one of the model's own bones.</summary>
	public void SetAnimTargetBone( string name, string bone ) => Animation.SetTargetBone( name, bone );

	/// <summary>Nothing to reach for.</summary>
	public void ClearAnimTarget( string name ) => Animation.ClearTarget( name );

	public void ResetAnimParameters() => Animation.ResetParameters();

	/// <summary>One parameter back to the graph's own default.</summary>
	public void ResetAnimParameter( string name ) => Animation.ResetParameter( name );

	/// <summary>An id parameter, as the number the game knows it by.</summary>
	public int GetInt( string name ) => Animation.GetIntParameter( name );
}
