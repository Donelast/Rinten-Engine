namespace Rinten.Rendering;

/// <summary>Drawing the world as the edges of its triangles.</summary>
public static class Wireframe
{
	/// <summary>0 off, 1 visible edges, 2 every drawn edge with depth test off (through walls).</summary>
	[ConVar( "mat_wireframe", ConVarFlags.Protected,
		Help = "Draw the world as triangle edges: 0 off, 1 what can be seen, 2 through walls" )]
	public static int Mode
	{
		get => mode;
		set => mode = System.Math.Clamp( value, 0, 2 );
	}

	private static int mode;

	/// <summary>Paint the world by the clusters it was drawn from.</summary>
	[ConVar( "mat_clusters", ConVarFlags.Protected,
		Help = "Paint the world by cluster: 0 off, 1 a colour a cluster, 2 by level of detail, 3 a colour an object" )]
	public static int Clusters
	{
		get => clusters;
		set => clusters = System.Math.Clamp( value, 0, 3 );
	}

	private static int clusters;

	/// <summary>At wireframe mode 2, depth-test edges against each other so the nearer one stays.</summary>
	[ConVar( "mat_wireframe_near_wins", ConVarFlags.Protected,
		Help = "At mat_wireframe 2, depth-test the edges against each other so the near one stays" )]
	public static bool NearWins { get; set; } = true;

	/// <summary>What a camera should draw at, given what it asks for itself.</summary>
	public static int For( bool cameraAsked ) => System.Math.Max( mode, cameraAsked ? 1 : 0 );
}
