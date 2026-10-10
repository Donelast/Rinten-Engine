namespace Editor;

//Moves the scene camera on measured runs, by wall time, so camera-motion perf is repeatable
internal static class PerfCamera
{
	//Degrees of yaw a second. -perfcamera 90 . Zero is off.
	private static readonly float turn = Switch( "-perfcamera" );

	//Metres a second, forward and back over the same sweep.
	private static readonly float move = Switch( "-perfcameramove" );

	//A flat view looking straight down, this many metres tall - what the orientation gizmo's axis handle makes. -perfortho 60 . Zero is off.
	private static readonly float ortho = Switch( "-perfortho" );

	//Whether the flat view was entered; after that the camera is left to it.
	private static bool flat;

	//Takes over the camera on every measured run so A/B runs start from the same view
	private static readonly bool driving =
		Named( "-perfcamera" ) || Named( "-perfcameramove" ) || Named( "-perfreport" ) ||
		Named( "-perfframes" ) || PerfScenario.EyeOverride is not null;

	//Whether to start from the scene's anchor; -perfcamerakeep starts from the saved camera
	private static readonly bool anchored = !Named( "-perfcamerakeep" );

	private static bool Named( string name )
	{
		foreach ( var arg in System.Environment.GetCommandLineArgs() )
		{
			if ( string.Equals( arg, name, System.StringComparison.OrdinalIgnoreCase ) ) return true;
		}

		return false;
	}

	//Reads the number after a command-line switch, parsed with invariant culture
	private static float Switch( string name )
	{
		var args = System.Environment.GetCommandLineArgs();

		for ( var i = 0; i < args.Length - 1; i++ )
		{
			if ( !string.Equals( args[i], name, System.StringComparison.OrdinalIgnoreCase ) ) continue;

			return float.TryParse( args[i + 1], System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out var value ) ? value : 0.0f;
		}

		return 0.0f;
	}

	//Warm-up seconds before driving; the editor restores its camera and scene late
	private static readonly float still = Switch( "-perfwarmup" ) is var w && w > 0.0f ? w : 30.0f;

	//How far either side of where it started the sweep looks, in degrees.
	private const float Sweep = 30.0f;

	//Where a run should be looking from: the scene's own camera if it has one, otherwise far enough back to hold everything in it.
	private static void Anchor( Scene scene, out Vector3 from, out Rotation look )
	{
		from = Vector3.Zero;
		look = Rotation.Identity;

		//Said outright on the command line, which beats everything else: a scene's own camera is wherever the level put it and its bounds
		//move whenever anything is added to the map, so neither can be asked for twice with any confidence. See PerfScenario.EyeOverride.
		if ( PerfScenario.EyeOverride is { } named )
		{
			from = named.Position;
			look = named.Rotation;
			return;
		}

		if ( !scene.IsValid() ) return;

		var camera = scene.Camera;

		if ( camera.IsValid() )
		{
			from = camera.WorldPosition;
			look = camera.WorldRotation;
			return;
		}

		var bounds = scene.GetBounds();

		look = Rotation.From( 45, 45, 0 );
		from = bounds.Center + ( MathF.Max( bounds.Size.Length * 0.5f, 5.0f ) * look.Backward );
	}

	private static bool started;
	private static RealTimeSince since;
	private static Vector3 origin;
	private static Rotation aim;

	[EditorEvent.Frame]
	public static void Frame()
	{
		//Named at all is enough to take the camera over: a run given -perfcamera 0 is a still
		//run anchored the same way a moving one is, which is the only way the two are comparable.
		if ( !driving ) return;

		//When playing, drive the game's camera instead, since the editor camera isn't rendered
		if ( Game.IsPlaying )
		{
			DriveGame();
			return;
		}

		var view = SceneViewPanel.Current?.LastSelectedViewport;

		if ( view is null ) return;

		//Seconds since the editor started - RealTime counts from there.
		if ( !started && RealTime.Now < still ) return;

		if ( !started )
		{
			//Start from the scene anchor, not the saved camera, so runs don't drift
			started = true;
			since = 0;

			if ( anchored )
			{
				Anchor( SceneViewPanel.Current?.Session?.Scene, out origin, out aim );
			}

			if ( !anchored || ( origin == Vector3.Zero && aim == Rotation.Identity ) )
			{
				origin = view.State.CameraPosition;
				aim = view.State.CameraRotation;
			}
		}

		//Into the flat view once, the way the gizmo does it, and then left: the still run below would put the eye back where the perspective one was.
		if ( ortho > 0.0f )
		{
			if ( !flat && view.ViewCamera is { } viewCamera )
			{
				//From above unless -perforthoside: along the ground, the way the gizmo's side handles look.
				if ( Named( "-perforthoside" ) ) viewCamera.EnterOrtho( Vector3.Backward, Vector3.Up, origin, 30.0f );
				else viewCamera.EnterOrtho( Vector3.Up, Vector3.Forward, origin, 30.0f );
				view.State.CameraOrthoHeight = ortho;
				flat = true;
			}

			return;
		}

		var seconds = (float)since;

		//Anchored and left there: a run that named no motion still wants to be looking at the same thing twice.
		if ( turn == 0.0f && move == 0.0f )
		{
			view.State.CameraPosition = origin;
			view.State.CameraRotation = aim;
			return;
		}

		if ( turn != 0.0f )
		{
			//Yaw sweeps back and forth; yaw = A sin(t r/A) peaks at exactly r degrees per second
			var yaw = Sweep * MathF.Sin( seconds * turn / Sweep );

			view.State.CameraRotation = Rotation.FromYaw( yaw ) * aim;
		}

		if ( move != 0.0f )
		{
			//Short fast back-and-forth movement, so the camera stays in the scene being measured
			var along = MathF.Sin( seconds * 3.0f ) * move * 0.5f;

			view.State.CameraPosition = origin + ( aim.Forward * along );
		}
	}

	//What this last left in the game's camera, so a frame the game rewrote is told from one it did not.
	private static Vector3 gameSetPosition;
	private static Rotation gameSetRotation;
	private static Vector3 gameOffset;
	private static float gameYaw;
	private static bool gameHeld;

	//Slides the game camera along its forward by a wall-time offset, re-basing if the game moved it
	private static void DriveGame()
	{
		var camera = Game.ActiveScene?.Camera;

		if ( !camera.IsValid() ) return;

		if ( !started && RealTime.Now < still ) return;

		if ( !started )
		{
			started = true;
			since = 0;

			//Said once, because a run whose camera did not move after all reads exactly like one that was never asked to.
			Log.Info( $"-perfcamera: driving the game's camera - {move * 0.5f:0.##} m either way, " +
				$"{turn:0.##} deg/s of yaw, from {camera.WorldPosition}" );
		}

		//Pins the game camera to -perfeye each frame so shots are comparable between builds
		if ( PerfScenario.EyeOverride is { } stood )
		{
			camera.WorldPosition = stood.Position;
			camera.WorldRotation = stood.Rotation;
		}

		if ( turn == 0.0f && move == 0.0f ) return;

		var here = camera.WorldPosition;
		var facing = camera.WorldRotation;

		var untouched = gameHeld && here == gameSetPosition && facing == gameSetRotation;

		var basePosition = untouched ? here - gameOffset : here;
		var baseRotation = untouched ? Rotation.FromYaw( -gameYaw ) * facing : facing;

		var seconds = (float)since;

		//Back and forth through a sweep rather than round and round, and back and forth about where it started
		//rather than away - the reasons are the ones in Frame above, and the numbers are the same numbers.
		gameYaw = turn == 0.0f ? 0.0f : Sweep * MathF.Sin( seconds * turn / Sweep );

		var look = gameYaw == 0.0f ? baseRotation : Rotation.FromYaw( gameYaw ) * baseRotation;

		gameOffset = move == 0.0f
			? Vector3.Zero
			: look.Forward * ( MathF.Sin( seconds * 3.0f ) * move * 0.5f );

		gameSetPosition = basePosition + gameOffset;
		gameSetRotation = look;
		gameHeld = true;

		camera.WorldPosition = gameSetPosition;
		camera.WorldRotation = look;
	}
}
