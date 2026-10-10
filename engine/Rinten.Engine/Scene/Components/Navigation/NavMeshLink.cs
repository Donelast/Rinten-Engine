using Rinten.Engine.Resources;
using Rinten.Navigation;
using System;

namespace Rinten;

/// <summary>NavigationLinks connect navigation mesh polygons for pathfinding and enable shortcuts like ladders, jumps, or teleports.</summary>
[Expose]
[Title( "NavMesh - Link" )]
[Category( "Navigation" )]
[Icon( "link" )]
[EditorHandle( "materials/gizmo/navmeshagent.png" )]
[Alias( "NavAgent" )]
public class NavMeshLink : Component, Component.ExecuteInEditor
{
	/// <summary>Start position relative to the game object's position.</summary>
	[Property]
	public Vector3 LocalStartPosition
	{
		get => _start;
		set
		{
			_start = value;
			CreateOrUpdateLink();
		}
	}

	private Vector3 _start;

	/// <summary>End position relative to the game object's position.</summary>
	[Property]
	public Vector3 LocalEndPosition
	{
		get => _end;
		set
		{
			_end = value;
			CreateOrUpdateLink();
		}
	}

	/// <summary>Start position in world space snapped to the navmesh.</summary>
	public Vector3? WorldStartPositionOnNavmesh
	{
		get => linkData.IsStartConnected ? linkData.StartPositionOnNavMesh : null;
	}

	/// <summary>End position in world space snapped to the navmesh.</summary>
	public Vector3? WorldEndPositionOnNavmesh
	{
		get => linkData.IsEndConnected ? linkData.EndPositionOnNavMesh : null;
	}

	private Vector3 _end;

	/// <summary>Whether this link can be traverse bi-directional or only start towards end.</summary>
	[Property]
	public bool IsBiDirectional = true;

	/// <summary>Radius that will be searched at the start and end positions for a connection to the navmesh.</summary>
	[Property]
	public float ConnectionRadius = 0.4f;

	/// <summary>The NavMesh area definition to apply to this link.</summary>
	[Property]
	public NavMeshAreaDefinition Area
	{
		get => _area;
		set
		{
			_area = value;
			CreateOrUpdateLink();
		}
	}
	private NavMeshAreaDefinition _area;

	/// <summary>Emitted when an agent enters the link.</summary>
	public Action<NavMeshAgent> LinkEntered { get; set; }

	/// <summary>Emitted when an agent exits the link.</summary>
	public Action<NavMeshAgent> LinkExited { get; set; }

	private NavMeshLinkData linkData;

	/// <summary>Start position in world space.</summary>
	public Vector3 WorldStartPosition
	{
		get => WorldTransform.PointToWorld( LocalStartPosition );
		set
		{
			LocalStartPosition = WorldTransform.PointToLocal( value );
		}
	}

	/// <summary>End position in world space.</summary>
	public Vector3 WorldEndPosition
	{
		get => WorldTransform.PointToWorld( LocalEndPosition );
		set
		{
			LocalEndPosition = WorldTransform.PointToLocal( value );
		}
	}

	//Called when an agent enters the link.
	protected virtual void OnLinkEntered( NavMeshAgent agent )
	{
	}

	//Called when an agent exits the link.
	protected virtual void OnLinkExited( NavMeshAgent agent )
	{
	}

	internal void TriggetEntered( NavMeshAgent agent )
	{
		if ( !Active )
			return;

		LinkEntered?.Invoke( agent );
		OnLinkEntered( agent );
	}

	internal void TriggetExited( NavMeshAgent agent )
	{
		if ( !Active )
			return;

		LinkExited?.Invoke( agent );
		OnLinkExited( agent );
	}

	internal override void OnEnabledInternal()
	{
		CreateOrUpdateLink();
		Transform.OnTransformChanged += TransformChanged;
		base.OnEnabledInternal();
	}

	internal override void OnDisabledInternal()
	{
		ClearLink();
		Transform.OnTransformChanged -= TransformChanged;
		base.OnDisabledInternal();
	}

	private void CreateOrUpdateLink()
	{
		if ( !Active )
			return;

		if ( linkData == null )
		{
			linkData = new NavMeshLinkData();
			linkData.UserData = this;
			Scene.NavMesh.AddSpatiaData( linkData );
		}

		linkData.StartPosition = WorldStartPosition;
		linkData.EndPosition = WorldEndPosition;

		linkData.IsBiDirectional = IsBiDirectional;
		linkData.ConnectionRadius = ConnectionRadius;

		linkData.AreaDefinition = Area;

		linkData.HasChanged = true;
	}

	private void ClearLink()
	{
		if ( linkData != null )
		{
			//tile cache will take care of removal
			linkData.IsPendingRemoval = true;
			linkData = null;
		}
	}

	protected override void DrawGizmos()
	{
		if ( linkData == null )
			return;

		Gizmo.Draw.Color = linkData.IsStartConnected ? NavMesh.debugInnerLineColor.WithAlpha( 1 ) : Color.Red;
		Gizmo.Draw.LineSphere( LocalStartPosition, ConnectionRadius );
		Gizmo.Draw.Color = linkData.IsEndConnected ? NavMesh.debugInnerLineColor.WithAlpha( 1 ) : Color.Red;
		Gizmo.Draw.LineSphere( LocalEndPosition, ConnectionRadius );

		var drawStartPosition = LocalStartPosition;
		if ( linkData.IsStartConnected )
		{
			drawStartPosition = WorldTransform.PointToLocal( linkData.StartPositionOnNavMesh ) + NavMesh.debugDrawGroundOffset + new Vector3( 0, 0.01f, 0 );
			Gizmo.Draw.Color = NavMesh.debugInnerLineColor.WithAlpha( 1 );
			Gizmo.Draw.SolidSphere( drawStartPosition, 0.2f );
		}

		var drawEndPosition = LocalEndPosition;
		if ( linkData.IsEndConnected )
		{
			drawEndPosition = WorldTransform.PointToLocal( linkData.EndPositionOnNavMesh ) + NavMesh.debugDrawGroundOffset + new Vector3( 0, 0.01f, 0 );
			Gizmo.Draw.Color = NavMesh.debugInnerLineColor.WithAlpha( 1 );
			Gizmo.Draw.SolidSphere( drawEndPosition, 0.2f );
		}

		Gizmo.Draw.LineThickness = 2f;
		Gizmo.Draw.Color = NavMesh.debugTileBorderColor.WithAlpha( 1 );

		//Draw arc between start end
		int segments = 8;
		int endToStartArrowSegment = segments / 4;
		int startToEndArrowSegment = segments - endToStartArrowSegment;
		Vector3 previousPoint = drawStartPosition;
		for ( int i = 1; i <= segments; i++ )
		{
			float t = (float)i / segments;
			Vector3 point = Vector3.Lerp( drawStartPosition, drawEndPosition, t );
			point.y += MathF.Sin( t * MathF.PI ) * MathF.Abs( drawStartPosition.y - drawEndPosition.y ); //curve upwards
			if ( i == startToEndArrowSegment )
			{
				Gizmo.Draw.Arrow( previousPoint, point, 0.2f );
			}
			else if ( i == endToStartArrowSegment + 1 && IsBiDirectional )
			{
				Gizmo.Draw.Arrow( point, previousPoint, 0.2f );
			}
			else
			{
				Gizmo.Draw.Line( previousPoint, point );

			}
			previousPoint = point;
		}
	}

	private void TransformChanged()
	{
		CreateOrUpdateLink();
	}
}
