namespace Rinten.Navigation.Generation;

[SkipHotload]
class NavMeshGenerator : IDisposable
{
	internal readonly List<Vector3> LinkVertices = new();
	internal readonly List<float> LinkRadii = new();
	internal readonly List<int> LinkAreas = new();
	internal readonly List<bool> LinkBidirectional = new();
	internal readonly List<object> LinkUserData = new();

	//Created in init disposed of after generate
	private CompactHeightfield chfWorkingCopy;

	private Config cfg;

	public void Init( Config config, CompactHeightfield inputChf )
	{
		cfg = config;
		LinkVertices.Clear();
		LinkRadii.Clear();
		LinkAreas.Clear();
		LinkBidirectional.Clear();
		LinkUserData.Clear();
		if ( chfWorkingCopy == null )
		{
			chfWorkingCopy = inputChf.Copy();
		}
		else
		{
			//Reuse memory
			inputChf.CopyTo( chfWorkingCopy );
		}
	}

	public void Dispose()
	{
		if ( chfWorkingCopy != null )
		{
			chfWorkingCopy.Dispose();
			chfWorkingCopy = null;
		}
	}

	public void MarkArea( NavMeshAreaData area, int areaId )
	{
		var areaTransform = area.Transform;
		var areaBounds = area.LocalBounds.Transform( areaTransform );

		if ( area.Volume.Type == Volumes.SceneVolume.VolumeTypes.Box )
		{
			var box = area.Volume.Box;
			chfWorkingCopy.MarkBoxArea( box, areaTransform, areaBounds, areaId );
		}
		else if ( area.Volume.Type == Volumes.SceneVolume.VolumeTypes.Capsule )
		{
			var capsule = area.Volume.Capsule;
			chfWorkingCopy.MarkCapsuleArea( capsule, areaTransform, areaBounds, areaId );
		}
		else if ( area.Volume.Type == Volumes.SceneVolume.VolumeTypes.Sphere )
		{
			var sphere = area.Volume.Sphere;
			chfWorkingCopy.MarkSphereArea( sphere, areaTransform, areaBounds, areaId );
		}
		else if ( area.Volume.Type == Volumes.SceneVolume.VolumeTypes.Infinite )
		{
			var infiniteBounds = new BBox( new Vector3( float.MinValue ), new Vector3( float.MaxValue ) );
			chfWorkingCopy.MarkBoxArea( infiniteBounds, Transform.Zero, infiniteBounds, areaId );
		}
	}

	private List<int> prevCache = new( 512 );

	//Cached pools for reuse across Generate() calls
	private PolyMeshBuilder.PolyMeshBuilderContext polyMeshBuilderContext = new();
	private RegionBuilder.RegionBuilderContext regionBuilderContext = new();
	private ContourBuilder.ContourBuilderContext contourBuilderContext = new();

	public PolyMesh Generate()
	{
		//According to recast docs good for tiles
		if ( !RegionBuilder.BuildLayerRegions( chfWorkingCopy, cfg.BorderSize, cfg.MinRegionArea, prevCache, regionBuilderContext ) )
		{
			//Log.Warning( "buildNavigation: Could not build layer regions.\n" );
			return null;
		}

		//Step 5. Trace and simplify region contours.

		//Create contours.
		ContourBuilder.BuildContours( chfWorkingCopy, cfg.MaxSimplificationError, cfg.MaxEdgeLen, contourBuilderContext );

		if ( contourBuilderContext.ContourSet.Contours.Count == 0 )
		{
			//Log.Warning( "buildNavigation: No contours could be build for regions.\n" );

			return null;
		}

		//Step 6. Build polygons mesh from contours.

		//Build polygon navmesh from the contours.
		return PolyMeshBuilder.BuildPolyMesh( contourBuilderContext.ContourSet, cfg.MaxVertsPerPoly, polyMeshBuilderContext );
	}
}
