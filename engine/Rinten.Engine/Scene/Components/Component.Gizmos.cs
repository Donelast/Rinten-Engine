using System.Reflection;

namespace Rinten;

public abstract partial class Component
{
	//Called in the editor to draw things like bounding boxes etc
	protected virtual void DrawGizmos() { }

	static readonly ReflectionCache<Type, bool> _typeOverridesDrawGizmos = new(
		t => t.GetMethod( nameof( DrawGizmos ), BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null )?.DeclaringType != typeof( Component ) );

	internal bool OverridesDrawGizmos => _typeOverridesDrawGizmos[GetType()];

	internal void DrawGizmosInternal()
	{
		//Profiled per component type; editor calls this per viewport for many components
		using var _ = Rinten.Diagnostics.FrameProfiler.PushComponent( this );

		try { DrawGizmos(); }
		catch ( System.Exception e ) { Log.Error( e, $"Exception when calling 'DrawGizmos' on {this}" ); }
	}
}
