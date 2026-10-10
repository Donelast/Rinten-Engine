using System;
using System.Runtime;

namespace Rinten;

/// <summary>The engine, as a test host runs it: our own core, no window, no game.</summary>
public class TestAppSystem : CoreAppSystem
{
	protected override bool Dedicated => true;

	public override void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;

		base.Init();
	}
}
