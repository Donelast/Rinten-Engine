using NativeEngine;

namespace Rinten;

public partial class Model
{
	internal static readonly Dictionary<long, WeakReference<Model>> Loaded = new();

	/// <summary>Returns a static <see cref="ModelBuilder"/> instance, allowing for runtime model creation.</summary>
	public static ModelBuilder Builder => new();

	/// <summary>A cube model</summary>
	public static Model Cube => Builtin( ref cube, "models/dev/box.mdl" );

	/// <summary>A sphere model</summary>
	public static Model Sphere => Builtin( ref sphere, "models/dev/sphere.mdl" );

	/// <summary>A plane model</summary>
	public static Model Plane => Builtin( ref plane, "models/dev/plane.mdl" );

	/// <summary>An error model</summary>
	public static Model Error => Builtin( ref error, "models/dev/error.mdl" );

	private static Model cube, sphere, plane, error;

	//The built-ins are our own format now, and they are generated rather than shipped - so the first one
	//asked for builds all four. Loading them eagerly in a field initialiser would run before the core is up.
	private static Model Builtin( ref Model slot, string path )
	{
		if ( slot.IsValid() ) return slot;
		if ( builtinFailed ) return slot;

		//Straight from the core, never through Load: Load falls back to the error model when it cannot read something,
		//and the error model is one of these - so a built-in that will not load would ask itself for itself, forever.
		slot = FromCore( CoreModel.Load( path ) );

		if ( slot is null )
		{
			//Once. Whatever is wrong with them is not going to be different next frame, and something asks for the error model every frame.
			builtinFailed = true;
			Log.Warning( $"the built-in models will not load - {path} first" );
		}

		return slot;
	}

	//Whether building and loading them has already been tried and failed.
	private static bool builtinFailed;
}
