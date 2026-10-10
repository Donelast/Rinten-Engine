using System.Text.Json.Serialization;
namespace Rinten;

//Finds the module's component per player, since one definition may play in several places
static class ParticleRendererLookup
{
	public static T Renderer<T>( ParticleModuleContext context ) where T : Component
		=> context.Effect.IsValid() ? context.Effect.GetComponent<T>() : null;

	/// <summary>The renderer that is this module's: the k-th component of the type for the k-th enabled module of the type on the emitter, since every module adds one - so two ribbons on one emitter are two ribbons.</summary>
	public static T Renderer<T>( ParticleModuleContext context, ParticleModule module ) where T : Component
	{
		if ( !context.Effect.IsValid() ) return null;

		var components = context.Effect.GetComponents<T>().ToList();

		if ( components.Count <= 1 ) return components.FirstOrDefault();

		var data = context.Data;
		var index = 0;

		if ( data is not null )
		{
			foreach ( var each in data.RenderModules )
			{
				if ( ReferenceEquals( each, module ) ) break;
				if ( each.Enabled && each.GetType() == module.GetType() ) index++;
			}
		}

		return index < components.Count ? components[index] : components[^1];
	}
}

/// <summary>Draws every particle as a flat picture facing the camera.</summary>
[Title( "Sprite" ), Category( "Render" ), Icon( "image" )]
public sealed class SpriteRenderModule : ParticleModule, IParticleModuleComponents
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Render;

	//A fallback rather than a default value on the property: an effect that has never been given a sprite should still be visible - "I cannot see my particles" is the wrong first lesson - but it should not be carrying a reference to a stock asset it never asked for either.
	/// <summary>The picture drawn for every particle. Nothing set draws a soft round dot.</summary>
	[Property]
	public Sprite Sprite { get; set; }

	/// <summary>How big the picture is drawn, on top of the particle's own size.</summary>
	[Property, Range( 0.0f, 2.0f )]
	public float Scale { get; set; } = 1.0f;

	//A material on a sprite shader - one written against sprite/sprite.slang - and anything else is refused by the renderer. See SpriteRenderer.MaterialOverride, which is the same property on a sprite standing in the world.
	/// <summary>What the particles are drawn with. Nothing set is the engine's own sprite shader, which is what nearly every effect wants.</summary>
	[Property]
	public Material MaterialOverride { get; set; }

	[Property]
	public ParticleSpriteRenderer.BillboardAlignment Alignment { get; set; } =
		ParticleSpriteRenderer.BillboardAlignment.LookAtCamera;

	/// <summary>Turn the picture so its top points the way the particle is going.</summary>
	[Property]
	public bool FaceVelocity { get; set; }

	/// <summary>Add to what is behind rather than covering it - fire, sparks, glow.</summary>
	[Property]
	public bool Additive { get; set; }

	[Property]
	public bool Shadows { get; set; }

	[Property]
	public bool Lighting { get; set; }

	/// <summary>How far from a surface the sprite starts fading, so it does not cut into it.</summary>
	[Property, Range( 0.0f, 50.0f )]
	public float DepthFeather { get; set; }

	/// <summary>Closer to the camera than this, in metres, a sprite is not drawn at all.</summary>
	[Property, Range( 0.0f, 2.0f )]
	public float CameraFadeNear { get; set; }

	/// <summary>Further than this, in metres, a sprite is fully opaque; it fades in between. Zero turns the fade off.</summary>
	[Property, Range( 0.0f, 8.0f )]
	public float CameraFadeFar { get; set; }

	/// <summary>Whether the sprites are drawn far to near, so one in front blends over one behind.</summary>
	[Property]
	public ParticleSpriteRenderer.ParticleSortMode SortMode { get; set; }

	/// <summary>A sprite with no translucency at all, which need not be sorted.</summary>
	[Property]
	public bool Opaque { get; set; }

	/// <summary>How much of the scene's fog lies over the sprite.</summary>
	[Property, Range( 0.0f, 1.0f )]
	public float FogStrength { get; set; } = 1.0f;

	/// <summary>Point for a pixel-art sprite; bilinear for anything else.</summary>
	[Property]
	public Rendering.FilterMode TextureFilter { get; set; } = Rendering.FilterMode.Bilinear;

	/// <summary>Copies of a fast sprite strung out along the way it is going.</summary>
	[Property, ToggleGroup( "MotionBlur" )]
	public bool MotionBlur { get; set; }

	/// <summary>Whether the blur runs ahead of the sprite as well as behind it.</summary>
	[Property, ToggleGroup( "MotionBlur" )]
	public bool LeadingTrail { get; set; } = true;

	[Property, ToggleGroup( "MotionBlur" ), Range( 0.0f, 1.0f )]
	public float BlurAmount { get; set; } = 0.5f;

	[Property, ToggleGroup( "MotionBlur" ), Range( 0.0f, 1.0f )]
	public float BlurSpacing { get; set; } = 0.5f;

	[Property, ToggleGroup( "MotionBlur" ), Range( 0.0f, 1.0f )]
	public float BlurOpacity { get; set; } = 0.5f;

	public override void Apply( ParticleModuleContext context ) { }

	static Sprite _fallback;

	//The soft round dot an effect draws before anybody has chosen anything.
	static Sprite Fallback => _fallback ??= Sprite.FromTexture( Dot() );

	//Soft round dot generated in code, since there is no usable stock particle texture
	static Texture Dot()
	{
		const int size = 32;

		var pixels = new byte[size * size * 4];
		var middle = ( size - 1 ) * 0.5f;

		for ( var y = 0; y < size; y++ )
		{
			for ( var x = 0; x < size; x++ )
			{
				//One at the middle, nothing at the edge of the circle, and a square root either side of that so the falloff reads as round rather than as a diamond.
				var dx = x - middle;
				var dy = y - middle;
				var distance = MathF.Sqrt( dx * dx + dy * dy ) / middle;
				var alpha = ( 1.0f - distance ).Clamp( 0.0f, 1.0f );

				var at = ( y * size + x ) * 4;

				pixels[at + 0] = 255;
				pixels[at + 1] = 255;
				pixels[at + 2] = 255;
				pixels[at + 3] = (byte)( alpha * alpha * 255.0f );
			}
		}

		return Texture.Create( size, size ).WithData( pixels ).Finish();
	}

	public void CreateComponents( GameObject target )
	{
		var renderer = target.AddComponent<ParticleSpriteRenderer>();

		renderer.Sprite = Sprite ?? Fallback;
		renderer.Scale = Scale;
		renderer.MaterialOverride = MaterialOverride;
		renderer.Alignment = Alignment;
		renderer.FaceVelocity = FaceVelocity;
		renderer.Additive = Additive;
		renderer.Shadows = Shadows;
		renderer.Lighting = Lighting;
		renderer.DepthFeather = DepthFeather;
		renderer.CameraFadeNear = CameraFadeNear;
		renderer.CameraFadeFar = CameraFadeFar;
		renderer.SortMode = SortMode;
		renderer.Opaque = Opaque;
		renderer.FogStrength = FogStrength;
		renderer.TextureFilter = TextureFilter;
		renderer.MotionBlur = MotionBlur;
		renderer.LeadingTrail = LeadingTrail;
		renderer.BlurAmount = BlurAmount;
		renderer.BlurSpacing = BlurSpacing;
		renderer.BlurOpacity = BlurOpacity;
	}
}

//Some, not all: a light is expensive, and an effect of a thousand sparks that each lit the room would be a thousand lights. Ratio is what decides how many of them get one.
/// <summary>Draws some of the particles as lights.</summary>
[Title( "Light" ), Category( "Render" ), Icon( "lightbulb" )]
public sealed class LightRenderModule : ParticleModule, IParticleModuleComponents
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Render;

	[Property]
	public ParticleColorBinding Color { get; set; } = new( global::Color.White );

	[Property]
	public ParticleFloatBinding Brightness { get; set; } = 1.0f;

	[Property]
	public ParticleFloatBinding Radius { get; set; } = 1.0f;

	[Property]
	public ParticleFloatBinding Attenuation { get; set; } = 1.0f;

	/// <summary>How many lights this effect may have at once.</summary>
	[Property, Range( 0.0f, 64.0f )]
	public ParticleFloatBinding MaxLights { get; set; } = 10.0f;

	/// <summary>What share of the particles get a light at all.</summary>
	[Property, Range( 0.0f, 1.0f )]
	public ParticleFloatBinding Ratio { get; set; } = 1.0f;

	/// <summary>Take the light's colour from the particle rather than from here.</summary>
	[Property]
	public bool UseParticleColor { get; set; } = true;

	public override void Apply( ParticleModuleContext context )
	{
		if ( ParticleRendererLookup.Renderer<ParticleLightRenderer>( context, this ) is not { } renderer ) return;

		renderer.LightColor = Color?.Evaluate( context.Player ) ?? global::Color.White;
		renderer.Brightness = Brightness.Resolve( context.Player );
		renderer.Scale = Radius.Resolve( context.Player );
		renderer.Attenuation = Attenuation.Resolve( context.Player );
		renderer.MaximumLights = (int)MaxLights.Evaluate( context.Player );
		renderer.Ratio = Ratio.Evaluate( context.Player );
		renderer.UseParticleColor = UseParticleColor;
	}

	public void CreateComponents( GameObject target ) => target.AddComponent<ParticleLightRenderer>();
}

/// <summary>Draws every particle as a model, picked from a list.</summary>
[Title( "Model" ), Category( "Render" ), Icon( "view_in_ar" )]
public sealed class ModelRenderModule : ParticleModule, IParticleModuleComponents
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Render;

	/// <summary>The models a particle may be. One is picked per particle.</summary>
	[Property]
	public List<ParticleModelRenderer.ModelEntry> Models { get; set; } = new();

	[Property]
	public ParticleFloatBinding Scale { get; set; } = 1.0f;

	/// <summary>Whether a model turns with the object rather than with the particle.</summary>
	[Property]
	public bool RotateWithObject { get; set; }

	[Property]
	public bool CastShadows { get; set; } = true;

	public override void Apply( ParticleModuleContext context )
	{
		if ( ParticleRendererLookup.Renderer<ParticleModelRenderer>( context ) is not { } renderer ) return;

		renderer.Scale = Scale.Resolve( context.Player );
		renderer.RotateWithGameObject = RotateWithObject;
		renderer.CastShadows = CastShadows;
	}

	public void CreateComponents( GameObject target )
	{
		var renderer = target.AddComponent<ParticleModelRenderer>();

		if ( Models is { Count: > 0 } )
			renderer.Choices = Models;
	}
}

/// <summary>Draws a ribbon behind every particle.</summary>
[Title( "Trail" ), Category( "Render" ), Icon( "gesture" )]
public sealed class TrailRenderModule : ParticleModule, IParticleModuleComponents
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Render;

	[Property]
	public Material Material { get; set; }

	/// <summary>How wide the ribbon is, in metres. A curve here runs along the ribbon rather than over the particle's life: nought is at the particle, one is the far end - so a curve from one down to nought is a ribbon that tapers to a point.</summary>
	[Property]
	public ParticleFloatBinding Width { get; set; } = new ParticleFloat
	{
		Type = ParticleFloat.ValueType.Curve,
		Evaluation = ParticleFloat.EvaluationType.Life,
		CurveA = new Curve( new Curve.Frame( 0.0f, 1.0f ), new Curve.Frame( 1.0f, 0.0f ) ) { ValueRange = new Vector2( 0.0f, 0.05f ) },
		CurveB = new Curve( new Curve.Frame( 0.0f, 1.0f ), new Curve.Frame( 1.0f, 0.0f ) ) { ValueRange = new Vector2( 0.0f, 0.05f ) },
	};

	/// <summary>The ribbon's colour. A gradient runs along it, particle to far end, and its alpha with it - a gradient that goes clear at one is a ribbon that fades out behind the particle.</summary>
	[Property]
	public ParticleColorBinding Color { get; set; } = FadingRibbon();

	//White along the ribbon, clear at its end.
	static ParticleColorBinding FadingRibbon()
	{
		var gradient = new Gradient( new Gradient.ColorFrame( 0.0f, global::Color.White ), new Gradient.ColorFrame( 1.0f, global::Color.White ) );

		gradient.AddAlpha( 0.0f, 1.0f );
		gradient.AddAlpha( 1.0f, 0.0f );

		return new ParticleGradient
		{
			Type = ParticleGradient.ValueType.Gradient,
			Evaluation = ParticleGradient.EvaluationType.Life,
			GradientA = gradient,
			GradientB = gradient,
		};
	}

	/// <summary>How many units of trail one repeat of the texture covers.</summary>
	[Property]
	public float UnitsPerTexture { get; set; } = 0.5f;

	/// <summary>How fast the texture slides along the ribbon.</summary>
	[Property]
	public float Scroll { get; set; }

	/// <summary>How many points the ribbon is remembered as.</summary>
	[Property]
	public int MaxPoints { get; set; } = 32;

	/// <summary>How far the particle travels before another point is laid down.</summary>
	[Property]
	public float PointDistance { get; set; } = 0.1f;

	/// <summary>How long a point stays before the tail catches up with it.</summary>
	[Property]
	public float LifeTime { get; set; } = 2.0f;

	[Property]
	public bool Opaque { get; set; } = true;

	[Property, ShowIf( nameof( Opaque ), false )]
	public BlendMode BlendMode { get; set; } = BlendMode.Normal;

	/// <summary>Whether the ribbon is as wide as the particle is big, on top of its own width.</summary>
	[Property]
	public bool ScaleFromParticle { get; set; } = true;

	/// <summary>Whether the ribbon takes the particle's colour, on top of its own.</summary>
	[Property]
	public bool TintFromParticle { get; set; } = true;

	public override void Apply( ParticleModuleContext context )
	{
		if ( ParticleRendererLookup.Renderer<ParticleTrailRenderer>( context, this ) is not { } renderer ) return;

		var texturing = renderer.Texturing;

		texturing.Material = Material;
		texturing.UnitsPerTexture = UnitsPerTexture;
		texturing.Scroll = Scroll;

		renderer.Texturing = texturing;

		renderer.Width = WidthCurve( Width.Resolve( context.Player ) );
		renderer.Color = ColorGradient( Color?.Evaluate( context.Player ) ?? global::Color.White );

		renderer.Opaque = Opaque;
		renderer.BlendMode = BlendMode;
		renderer.MaxPoints = MaxPoints;
		renderer.PointDistance = PointDistance;
		renderer.LifeTime = LifeTime;
		renderer.ScaleFromParticle = ScaleFromParticle;
		renderer.TintFromParticle = TintFromParticle;
	}

	public void CreateComponents( GameObject target ) => target.AddComponent<ParticleTrailRenderer>();

	//The width as a curve along the ribbon. A trail is drawn from a Curve rather than a ParticleFloat, so the modes that
	//are not curves have to be said as ones: a constant is a flat line, a range is a line from one end to the other.
	static Curve WidthCurve( ParticleFloat value ) => value.Type switch
	{
		ParticleFloat.ValueType.Constant => value.ConstantValue,
		ParticleFloat.ValueType.Range => new Curve( new Curve.Frame( 0.0f, value.ConstantA ), new Curve.Frame( 1.0f, value.ConstantB ) ),
		_ => value.CurveA
	};

	//The colour as a gradient along the ribbon, on the same terms.
	static Gradient ColorGradient( ParticleGradient value ) => value.Type switch
	{
		ParticleGradient.ValueType.Constant => value.ConstantA,
		ParticleGradient.ValueType.Range => new Gradient( new Gradient.ColorFrame( 0.0f, value.ConstantA ), new Gradient.ColorFrame( 1.0f, value.ConstantB ) ),
		_ => value.GradientA
	};
}

//The effect is a file of its own, so it has its own emitters, modules and parameters, and can be opened and tuned on its own. It is played once and then taken away - see ParticlePlayer.PlayOnce. An effect that spawns itself would go on for ever; do not.
/// <summary>Plays another effect where each particle dies: a burst where a rocket ran out, a splash where a drop landed, a puff where an ember went out.</summary>
[Title( "Effect On Death" ), Category( "Render" ), Icon( "call_split" )]
public sealed class SpawnEffectOnDeathModule : ParticleModule, IParticleModuleComponents
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Render;

	/// <summary>The effect played where a particle dies.</summary>
	[Property]
	public ParticleDefinition Effect { get; set; }

	/// <summary>How likely each death is to play it, nought to one.</summary>
	[Property, Range( 0.0f, 1.0f )]
	public ParticleFloatBinding Chance { get; set; } = 1.0f;

	/// <summary>How big the spawned effect is, on top of its own scale.</summary>
	[Property, Range( 0.0f, 5.0f )]
	public ParticleFloatBinding Scale { get; set; } = 1.0f;

	/// <summary>Whether the spawned effect faces the way the particle was going.</summary>
	[Property]
	public bool AlignToVelocity { get; set; }

	/// <summary>Whether a particle killed by hitting something plays it.</summary>
	[Property]
	public bool OnCollision { get; set; } = true;

	/// <summary>Whether a particle that lived its life out plays it.</summary>
	[Property]
	public bool OnExpiry { get; set; } = true;

	/// <summary>The most alive at once, so a thousand sparks cannot become a thousand effects.</summary>
	[Property, Range( 1, 200 )]
	public int MaxAlive { get; set; } = 16;

	public override void Apply( ParticleModuleContext context )
	{
		if ( context.Effect?.GetComponent<ParticleEffectSpawner>() is not { } spawner ) return;

		spawner.Effect = Effect;
		spawner.Chance = Chance.Evaluate( context.Player );
		spawner.Scale = Scale.Evaluate( context.Player );
		spawner.AlignToVelocity = AlignToVelocity;
		spawner.OnCollision = OnCollision;
		spawner.OnExpiry = OnExpiry;
		spawner.MaxAlive = MaxAlive;
	}

	public void CreateComponents( GameObject target )
	{
		var spawner = target.AddComponent<ParticleEffectSpawner>();

		spawner.Effect = Effect;
	}
}

//Morph and shader rows are synced from the model and shader; eight shader numbers per draw
/// <summary>Draws each particle as a mesh, with the model's blend shapes and the material's shader numbers driven per particle - a stylised fire on a flame mesh, a shield that breathes, a shard that dissolves.</summary>
[Title( "Mesh" ), Category( "Render" ), Icon( "view_in_ar" )]
public sealed class MeshRenderModule : ParticleModule, IParticleModuleComponents
{
	public override ParticleModuleStage Stage => ParticleModuleStage.Render;

	/// <summary>A blend shape and what drives it.</summary>
	[Expose]
	public sealed class MorphRow
	{
		[Property] public bool Enabled { get; set; }
		[Property, ReadOnly] public string Name { get; set; }
		[Property, Range( 0.0f, 1.0f )] public ParticleFloatBinding Weight { get; set; } = 0.0f;
	}

	/// <summary>A number or colour the shader declared, and what drives it.</summary>
	[Expose]
	public sealed class ShaderRow
	{
		[Property] public bool Enabled { get; set; }
		[Property, ReadOnly] public string Name { get; set; }

		/// <summary>How many of the eight slots this takes: one for a number, four for a colour.</summary>
		[Hide] public int Width { get; set; } = 1;

		[Property, ShowIf( nameof( IsColor ), false )] public ParticleFloatBinding Value { get; set; } = 0.0f;
		[Property, ShowIf( nameof( IsColor ), true )] public ParticleColorBinding Color { get; set; } = global::Color.White;

		/// <summary>A three-wide attribute read as a vector - a position, a direction - rather than a colour, when this is on.</summary>
		[Property, ShowIf( nameof( IsColor ), true ), Title( "As Vector" )] public bool AsVector { get; set; }

		[Property, ShowIf( nameof( AsVector ), true )] public ParticleVectorBinding Vector { get; set; } = Vector3.Zero;

		//The range the shader declared, for the slider.
		[Hide] public float Min { get; set; }
		[Hide] public float Max { get; set; } = 1.0f;

		[Hide, JsonIgnore] public bool IsColor => Width >= 3;
	}

	[Property]
	public Model Model { get; set; }

	/// <summary>The material the mesh draws with. Its shader is where the Shader rows come from.</summary>
	[Property]
	public Material Material { get; set; }

	[Property, Range( 0.0f, 10.0f )]
	public ParticleFloatBinding Scale { get; set; } = 1.0f;

	/// <summary>Whether the mesh turns with the effect's object as well as with the particle.</summary>
	[Property]
	public bool RotateWithObject { get; set; } = true;

	[Property]
	public bool CastShadows { get; set; } = true;

	/// <summary>The model's blend shapes. Filled in from the model; tick the ones to drive.</summary>
	[Property, InlineEditor]
	public List<MorphRow> Morphs { get; set; } = new();

	/// <summary>The shader's own numbers. Filled in from the material's shader; tick the ones to drive.</summary>
	[Property, InlineEditor]
	public List<ShaderRow> Shader { get; set; } = new();

	/// <summary>Whether the draw also carries ParticleAge and ParticleRandom for the shader.</summary>
	[Property]
	public bool ParticleAttributes { get; set; } = true;

	/// <summary>Draw only the piece of the model each particle stands for - what a particle born on a Model shape by Parts carries - turned about that piece's own middle. Off draws the whole model for every particle.</summary>
	[Property, Title( "Piece From Particle" )]
	public bool PartFromParticle { get; set; }

	/// <summary>How many of the eight per-draw slots the enabled rows take (plus two for the particle attributes).</summary>
	[Hide, JsonIgnore]
	public int SlotsUsed => Shader.Where( r => r.Enabled ).Sum( r => r.Width ) + ( ParticleAttributes ? 2 : 0 );

	/// <summary>Reads the model and the material again and brings the rows into line: a shape or a number that is there gets a row (kept where it already had one), one that has gone loses it.</summary>
	public void Sync()
	{
		var shapes = Model?.Morphs?.Names ?? [];

		Morphs.RemoveAll( row => Array.IndexOf( shapes, row.Name ) < 0 );

		foreach ( var name in shapes )
		{
			if ( Morphs.Any( row => row.Name == name ) ) continue;
			Morphs.Add( new MorphRow { Name = name } );
		}

		//Through the renderer's own lookup, which finds the file the same way the stages do; Of() wants a path on disk.
		var declared = Material is { ShaderName: { Length: > 0 } shader }
			? CoreResources.Declared( shader )?.Where( p => p.IsAttribute && !p.IsTexture ).ToArray() ?? []
			: [];

		Shader.RemoveAll( row => declared.All( p => p.Attribute != row.Name ) );

		foreach ( var parameter in declared )
		{
			if ( Shader.Any( row => row.Name == parameter.Attribute ) ) continue;

			var row = new ShaderRow
			{
				Name = parameter.Attribute,
				Width = parameter.Width,
				Min = parameter.Least,
				Max = parameter.Most > parameter.Least ? parameter.Most : 1.0f,
			};

			if ( row.IsColor )
				row.Color = new Color( parameter.Default.x, parameter.Default.y, parameter.Default.z, parameter.Default.w );
			else
				row.Value = parameter.Default.x;

			Shader.Add( row );
		}
	}

	public override void Apply( ParticleModuleContext context )
	{
		if ( context.Effect?.GetComponent<ParticleModelRenderer>() is not { } renderer ) return;

		renderer.Choices = Model is not null ? new List<ParticleModelRenderer.ModelEntry> { Model } : new();
		renderer.MaterialOverride = Material;
		renderer.Scale = Scale.Resolve( context.Player );
		renderer.RotateWithGameObject = RotateWithObject;
		renderer.CastShadows = CastShadows;
		renderer.WriteParticleAttributes = ParticleAttributes;
		renderer.PartFromParticle = PartFromParticle;

		renderer.Morphs = Morphs
			.Where( row => row.Enabled && !string.IsNullOrEmpty( row.Name ) )
			.Select( row => new ParticleModelRenderer.MorphDriver( row.Name, row.Weight.Resolve( context.Player ) ) )
			.ToList();

		renderer.FloatAttributes = Shader
			.Where( row => row.Enabled && !row.IsColor && !string.IsNullOrEmpty( row.Name ) )
			.Select( row => new ParticleModelRenderer.FloatDriver( row.Name, row.Value.Resolve( context.Player ) ) )
			.ToList();

		renderer.ColorAttributes = Shader
			.Where( row => row.Enabled && row.IsColor && !row.AsVector && !string.IsNullOrEmpty( row.Name ) )
			.Select( row => new ParticleModelRenderer.ColorDriver( row.Name, row.Color.Evaluate( context.Player ) ) )
			.ToList();

		//Vectors are read live, per particle, against the player: what they point at - an anchor, a parameter - moves.
		renderer.Player = context.Player;
		renderer.VectorAttributes = Shader
			.Where( row => row.Enabled && row.IsColor && row.AsVector && !string.IsNullOrEmpty( row.Name ) )
			.Select( row => new ParticleModelRenderer.VectorDriver( row.Name, row.Vector ) )
			.ToList();
	}

	public void CreateComponents( GameObject target )
	{
		var renderer = target.AddComponent<ParticleModelRenderer>();

		renderer.Choices = Model is not null ? new List<ParticleModelRenderer.ModelEntry> { Model } : new();
		renderer.MaterialOverride = Material;
	}
}
