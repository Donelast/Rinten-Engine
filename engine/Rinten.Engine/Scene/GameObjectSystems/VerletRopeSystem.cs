using System.Collections.Concurrent;

namespace Rinten;

/// <summary>Simulates VerletRope components in parallel during PrePhysicsStep</summary>
internal sealed class VerletRopeGameSystem : GameObjectSystem
{
	private readonly List<VerletRope> _ropes = new();

	public VerletRopeGameSystem( Scene scene ) : base( scene )
	{
		//Listen to StartFixedUpdate to run before physics
		Listen( Stage.StartFixedUpdate, -100, UpdateRopes, "UpdateRopes" );
	}

	void UpdateRopes()
	{
		using var _ = PerformanceStats.Timings.Physics.Scope();

		_ropes.Clear();
		Scene.GetAll<VerletRope>( _ropes );
		if ( _ropes.Count == 0 ) return;

		var timeDelta = Time.Delta;

		if ( Rinten.Utility.Parallel.InOrder )
		{
			for ( var i = 0; i < _ropes.Count; i++ ) _ropes[i].Simulate( timeDelta );
			return;
		}

		System.Threading.Tasks.Parallel.ForEach( Partitioner.Create( _ropes, loadBalance: true ), rope => rope.Simulate( timeDelta ) );
	}
}
