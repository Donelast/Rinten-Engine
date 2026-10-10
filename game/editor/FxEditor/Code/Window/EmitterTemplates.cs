namespace Editor.FxEditor;

//Ready-made emitters for the Add menu, built in code so they never go stale against the modules
internal static class EmitterTemplates
{
	/// <summary>What the menu offers, with an icon each.</summary>
	public static readonly (string Name, string Icon)[] All =
	[
		( "Flames", "local_fire_department" ),
		( "Smoke", "cloud" ),
		( "Sparks", "auto_awesome" ),
		( "Burst", "flare" ),
		( "Ring", "radio_button_unchecked" ),
		( "Rain", "water_drop" ),
		( "Trail Sparks", "timeline" ),
		( "Debris", "grain" ),
	];

	public static ParticleDefinition.Emitter Build( string name ) => name switch
	{
		"Flames" => Flames(),
		"Smoke" => Smoke(),
		"Sparks" => Sparks(),
		"Burst" => Burst(),
		"Ring" => Ring(),
		"Rain" => Rain(),
		"Trail Sparks" => TrailSparks(),
		"Debris" => Debris(),
		_ => null
	};

	static ParticleDefinition.Emitter Emitter( string name, params ParticleModule[] modules )
	{
		var emitter = new ParticleDefinition.Emitter { Name = name };

		foreach ( var module in modules )
			emitter.Add( module );

		return emitter;
	}

	static ParticleFloat Range( float a, float b ) => new( a, b );

	static ParticleFloat Curve( params (float t, float v)[] points )
	{
		var frames = points.Select( p => new Curve.Frame( p.t, p.v ) { Mode = Rinten.Curve.HandleMode.Flat } ).ToList();

		return new ParticleFloat
		{
			Type = ParticleFloat.ValueType.Curve,
			Evaluation = ParticleFloat.EvaluationType.Life,
			CurveA = new Curve( frames ),
			CurveB = new Curve( frames ),
		};
	}

	static ParticleGradient Gradient( params (float t, Color c)[] stops )
	{
		var gradient = new Gradient( stops.Select( s => new Gradient.ColorFrame( s.t, s.c ) ).ToArray() );

		return new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Gradient,
			Evaluation = ParticleGradient.EvaluationType.Life,
			GradientA = gradient,
			GradientB = gradient,
		};
	}

	static ParticleDefinition.Emitter Flames() => Emitter( "Flames",
		new SpawnRateModule { Rate = 18.0f },
		new InitializePositionModule { Shape = InitializePositionModule.SpawnShape.Circle, Radius = 0.2f },
		new InitializeVelocityModule { Velocity = new ParticleVector3 { X = Range( -0.3f, 0.3f ), Y = Range( 1.4f, 2.4f ), Z = Range( -0.3f, 0.3f ) } },
		new InitializeLifetimeModule { Lifetime = Range( 0.5f, 0.9f ) },
		new InitializeSizeModule { Size = Curve( (0, 0.4f), (0.35f, 0.8f), (1, 0.2f) ) },
		new InitializeColorModule
		{
			Color = Gradient( (0, Color.Parse( "#fff0b0" ).Value), (0.15f, Color.Parse( "#ffa020" ).Value), (0.5f, Color.Parse( "#ff5a0a" ).Value), (1, Color.Parse( "#200000" ).Value) ),
			Alpha = Curve( (0, 0), (0.15f, 0.7f), (0.7f, 0.5f), (1, 0) ),
		},
		new CurlNoiseModule { Strength = 0.7f, Scale = 1.6f, TimeScale = 1.5f },
		new DragModule { Damping = 0.6f },
		new SpriteRenderModule { Additive = true } );

	static ParticleDefinition.Emitter Smoke() => Emitter( "Smoke",
		new SpawnRateModule { Rate = 8.0f },
		new InitializePositionModule { Shape = InitializePositionModule.SpawnShape.Circle, Radius = 0.2f },
		new InitializeVelocityModule { Velocity = new ParticleVector3 { X = Range( -0.2f, 0.2f ), Y = Range( 0.9f, 1.5f ), Z = Range( -0.2f, 0.2f ) } },
		new InitializeLifetimeModule { Lifetime = Range( 2.5f, 4.0f ) },
		new InitializeSizeModule { Size = Curve( (0, 0.2f), (1, 1.0f) ) },
		new InitializeColorModule
		{
			Color = Gradient( (0, Color.Parse( "#5a5a5a" ).Value), (1, Color.Parse( "#222222" ).Value) ),
			Alpha = Curve( (0, 0), (0.15f, 0.4f), (1, 0) ),
		},
		new InitializeRotationModule { Rotation = new ParticleVector3 { Z = Range( 0, 360 ) } },
		new SpinModule { Speed = new ParticleVector3 { Z = Range( -20, 20 ) } },
		new CurlNoiseModule { Strength = 0.4f, Scale = 1.0f, TimeScale = 0.8f },
		new DragModule { Damping = 0.3f },
		new SpriteRenderModule { Additive = false, Lighting = true, SortMode = ParticleSpriteRenderer.ParticleSortMode.ByDistance } );

	static ParticleDefinition.Emitter Sparks() => Emitter( "Sparks",
		new SpawnRateModule { Rate = 30.0f },
		new InitializePositionModule { Shape = InitializePositionModule.SpawnShape.Point },
		new InitializeVelocityModule { Velocity = new ParticleVector3 { Y = Range( 2.0f, 4.0f ) }, Scatter = 1.5f },
		new InitializeLifetimeModule { Lifetime = Range( 0.5f, 1.2f ) },
		new InitializeSizeModule { Size = Range( 0.02f, 0.04f ) },
		new InitializeColorModule { Color = Color.Parse( "#ffd080" ).Value, Alpha = Curve( (0, 1), (0.7f, 1), (1, 0) ), Brightness = 1.5f },
		new InitializeStretchModule { Stretch = 2.5f },
		new GravityModule(),
		new DragModule { Damping = 0.5f },
		new SpriteRenderModule { Additive = true, FaceVelocity = true, MotionBlur = true, BlurAmount = 0.4f, BlurSpacing = 0.3f, BlurOpacity = 0.4f } );

	static ParticleDefinition.Emitter Burst() => Emitter( "Burst",
		new SpawnBurstModule { Count = 60.0f },
		new InitializePositionModule { Shape = InitializePositionModule.SpawnShape.Sphere, Radius = 0.2f },
		new InitializeVelocityModule { Scatter = 4.0f },
		new InitializeLifetimeModule { Lifetime = Range( 0.6f, 1.2f ) },
		new InitializeSizeModule { Size = Curve( (0, 0.1f), (0.3f, 0.3f), (1, 0.05f) ) },
		new InitializeColorModule { Color = Color.Parse( "#ffb060" ).Value, Alpha = Curve( (0, 1), (0.6f, 1), (1, 0) ), Brightness = 1.2f },
		new DragModule { Damping = 1.0f },
		new SpriteRenderModule { Additive = true } );

	static ParticleDefinition.Emitter Ring() => Emitter( "Ring",
		new SpawnRateModule { Rate = 60.0f },
		new InitializePositionModule { Shape = InitializePositionModule.SpawnShape.Circle, Radius = 1.0f, OnShell = true },
		new InitializeLifetimeModule { Lifetime = Range( 1.0f, 1.6f ) },
		new InitializeSizeModule { Size = Range( 0.03f, 0.06f ) },
		new InitializeColorModule { Color = Color.Parse( "#a0c0ff" ).Value, Alpha = Curve( (0, 0), (0.2f, 0.8f), (0.8f, 0.8f), (1, 0) ), Brightness = 1.2f },
		new OrbitModule { Speed = 90.0f },
		new SpriteRenderModule { Additive = true } );

	static ParticleDefinition.Emitter Rain() => Emitter( "Rain",
		new SpawnRateModule { Rate = 400.0f },
		new InitializePositionModule { Shape = InitializePositionModule.SpawnShape.Box, BoxSize = new Vector3( 5, 0.2f, 5 ), Offset = Vector3.Up * 6.0f },
		new InitializeVelocityModule { Velocity = new ParticleVector3 { Y = -11.0f } },
		new InitializeLifetimeModule { Lifetime = 0.6f },
		new InitializeSizeModule { Size = Range( 0.012f, 0.02f ) },
		new InitializeColorModule { Color = Color.Parse( "#c8dcf0" ).Value, Alpha = 0.55f },
		new InitializeStretchModule { Stretch = 9.0f },
		new SpriteRenderModule { Additive = true, FaceVelocity = true } );

	static ParticleDefinition.Emitter TrailSparks() => Emitter( "Trail Sparks",
		new SpawnRateModule { Rate = 12.0f },
		new InitializePositionModule { Shape = InitializePositionModule.SpawnShape.Point },
		new InitializeVelocityModule { Velocity = new ParticleVector3 { Y = Range( 3.0f, 5.0f ) }, Scatter = 2.0f },
		new InitializeLifetimeModule { Lifetime = Range( 1.0f, 1.8f ) },
		new InitializeSizeModule { Size = 0.03f },
		new InitializeColorModule { Color = Color.Parse( "#ffe0a0" ).Value, Brightness = 1.5f },
		new GravityModule(),
		new SpriteRenderModule { Additive = true },
		new TrailRenderModule { Width = 0.04f, LifeTime = 0.5f, MaxPoints = 24, PointDistance = 0.05f, Opaque = false } );

	static ParticleDefinition.Emitter Debris() => Emitter( "Debris",
		new SpawnBurstModule { Count = 20.0f },
		new InitializePositionModule { Shape = InitializePositionModule.SpawnShape.Point },
		new InitializeVelocityModule { Velocity = new ParticleVector3 { Y = 3.0f }, Scatter = 3.0f },
		new InitializeLifetimeModule { Lifetime = Range( 2.0f, 3.5f ) },
		new InitializeSizeModule { Size = Range( 0.05f, 0.12f ) },
		new InitializeColorModule { Color = Color.Parse( "#5a4a3a" ).Value },
		new InitializeRotationModule { Rotation = new ParticleVector3 { X = Range( 0, 360 ), Y = Range( 0, 360 ), Z = Range( 0, 360 ) } },
		new SpinModule { Speed = new ParticleVector3 { X = Range( -300, 300 ), Y = Range( -300, 300 ), Z = Range( -300, 300 ) } },
		new ParticleCollisionModule { Radius = 0.05f, Bounce = 0.4f, Friction = 0.8f },
		new GravityModule(),
		new ModelRenderModule { Models = { new ParticleModelRenderer.ModelEntry { Model = Model.Cube } }, Scale = 1.0f } );
}
