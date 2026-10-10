using System.Text.Json.Nodes;

namespace Rinten;

/// <summary>The parts of a graph that produce a pose.</summary>
public sealed partial class AnimGraphBuilder
{
	//-- the two poses that need nothing --------------------------------------

	/// <summary>Where the model rests. What a graph falls back to.</summary>
	public Node ReferencePose() => Add( NodeKind.Pose, "ReferencePose" );

	/// <summary>Nothing at all - every bone at zero. Only useful as the base of an additive layer, which is a difference and not a pose.</summary>
	public Node ZeroPose() => Add( NodeKind.Pose, "ZeroPose" );

	//-- clips ----------------------------------------------------------------

	/// <summary>A clip, playing at its own speed.</summary>
	public Node Clip( string clip, ClipSettings settings = null )
	{
		settings ??= new ClipSettings();

		return Add( NodeKind.Pose, "Clip", new JsonObject
		{
			["Clip"] = clip ?? "",
			["Looping"] = settings.Looping,
			["Speed"] = settings.Speed,
			["PlayInReverse"] = Ref( settings.PlayInReverse ),
			["ResetTime"] = Ref( settings.ResetTime ),
			["SyncEventOffset"] = settings.SyncEventOffset,
			["RootMotion"] = settings.SampleRootMotion,
			["Events"] = Text( settings.GraphEvents ),
		} );
	}

	public Node Clip( AnimationClip clip, ClipSettings settings = null )
		=> Clip( clip?.Name, settings );

	/// <summary>A clip whose time comes from the graph rather than from the clock - which is what makes a clip usable as a pose to be scrubbed.</summary>
	public Node TimeControlledClip( string clip, Node time, TimeControlledClipSettings settings = null )
	{
		settings ??= new TimeControlledClipSettings();

		return Add( NodeKind.Pose, "TimedClip", new JsonObject
		{
			["Clip"] = clip ?? "",
			["Time"] = Ref( time ),
			["PlayInReverse"] = Ref( settings.PlayInReverse ),
			["RootMotion"] = settings.SampleRootMotion,
			["Events"] = Text( settings.GraphEvents ),
		} );
	}

	public Node TimeControlledClip( AnimationClip clip, Node time, TimeControlledClipSettings settings = null )
		=> TimeControlledClip( clip?.Name, time, settings );

	/// <summary>One frame of a clip, chosen by a value. A lean, an aim, anything read off a number rather than played.</summary>
	public Node Pose( string clip, PoseSettings settings = null )
	{
		settings ??= new PoseSettings();

		return Add( NodeKind.Pose, "Pose", new JsonObject
		{
			["Clip"] = clip ?? "",
			["Time"] = Ref( settings.Time ),
			["RemapStart"] = settings.RemapRangeStart,
			["RemapEnd"] = settings.RemapRangeEnd,
			["FixedTime"] = settings.FixedTime,
			["Frames"] = settings.UseFramesAsInput,
		} );
	}

	public Node Pose( string clip, Node time ) => Pose( clip, new PoseSettings { Time = time } );

	public Node Pose( AnimationClip clip, PoseSettings settings = null ) => Pose( clip?.Name, settings );

	public Node Pose( AnimationClip clip, Node time ) => Pose( clip?.Name, new PoseSettings { Time = time } );

	//-- putting two together -------------------------------------------------

	public Node Blend( Node a, Node b, Node weight )
		=> Add( NodeKind.Pose, "Blend", new JsonObject
		{
			["A"] = Ref( a ),
			["B"] = Ref( b ),
			["Weight"] = Ref( weight ),
		} );

	public Node Blend( Node a, Node b, float weight ) => Blend( a, b, Constant( weight ) );

	/// <summary>A pose that does nothing but pass what is under it along. Useful as something to hang a name or a debugger on.</summary>
	public Node Passthrough( Node pose )
		=> Add( NodeKind.Pose, "Passthrough", new JsonObject { ["Pose"] = Ref( pose ) } );

	//-- speed ----------------------------------------------------------------

	public Node SpeedScale( Node pose, Node scale )
		=> Add( NodeKind.Pose, "SpeedScale", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Scale"] = Ref( scale ),
		} );

	public Node SpeedScale( Node pose, float scale ) => SpeedScale( pose, Constant( scale ) );

	/// <summary>Play whatever is under this in a given time, whatever its own length.</summary>
	public Node DurationScale( Node pose, Node duration, float fallback = 0.0f )
		=> Add( NodeKind.Pose, "DurationScale", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Duration"] = Ref( duration ),
			["Default"] = fallback,
		} );

	public Node DurationScale( Node pose, Node duration ) => DurationScale( pose, duration, 0.0f );

	public Node DurationScale( Node pose, float duration ) => DurationScale( pose, Constant( duration ), duration );

	/// <summary>Play at whatever speed makes the animation's own movement match a velocity - which is what stops a walk cycle sliding along the floor.</summary>
	public Node VelocityScale( Node pose, Node velocity, float fallback = 0.0f )
		=> Add( NodeKind.Pose, "VelocityScale", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Velocity"] = Ref( velocity ),
			["Default"] = fallback,
		} );

	public Node VelocityScale( string bone, Node velocity, bool worldSpace = false )
		=> Add( NodeKind.Pose, "VelocityScaleBone", new JsonObject
		{
			["Bone"] = bone ?? "",
			["Velocity"] = Ref( velocity ),
			["WorldSpace"] = worldSpace,
		} );

	/// <summary>Scales how far a pose moves its bones from where they rest, by weight and by mask. Turning an animation down rather than off.</summary>
	public Node Scale( Node pose, Node mask, Node enable )
		=> Add( NodeKind.Pose, "PoseScale", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Mask"] = Ref( mask ),
			["Enable"] = Ref( enable ),
		} );

	//-- bone masks -----------------------------------------------------------

	/// <summary>Turn a run of bones so a character points where it is told: a head at what it is watching, a weapon at what it is aiming at.</summary>
	public Node LookAt( Node pose, string chain, Node direction, LookAtSettings settings = null )
	{
		settings ??= new LookAtSettings();

		return Add( NodeKind.Pose, "LookAt", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Chain"] = chain ?? "",
			["Attachment"] = settings.Attachment ?? "",
			["Direction"] = Ref( direction ),
			["Weight"] = Ref( settings.Weight ),
			["Limit"] = settings.Limit,
			["YawLimit"] = settings.YawLimit,
			["PitchLimit"] = settings.PitchLimit,
		} );
	}

	/// <summary>A named mask from the model - what an artist painted.</summary>
	public Node BoneMask( string name )
		=> Add( NodeKind.BoneMask, "BoneMask", new JsonObject { ["Name"] = name ?? "" } );

	/// <summary>The same, with the weights carried along rather than looked up by name.</summary>
	public Node BoneMask( string name, IEnumerable<(string Bone, float Weight)> weights )
	{
		var listed = new JsonArray();

		foreach ( var (bone, weight) in weights ?? Array.Empty<(string, float)>() )
		{
			if ( string.IsNullOrWhiteSpace( bone ) ) continue;

			listed.Add( new JsonObject { ["Bone"] = bone, ["Weight"] = weight } );
		}

		return Add( NodeKind.BoneMask, "BoneMask", new JsonObject
		{
			["Name"] = name ?? "",
			["Weights"] = listed,
		} );
	}

	/// <summary>The same weight on every bone.</summary>
	public Node BoneMask( float weight )
		=> Add( NodeKind.BoneMask, "BoneMaskWeight", new JsonObject { ["Weight"] = weight } );

	public Node BoneMaskBlend( Node a, Node b, Node weight )
		=> Add( NodeKind.BoneMask, "BoneMaskBlend", new JsonObject
		{
			["A"] = Ref( a ),
			["B"] = Ref( b ),
			["Weight"] = Ref( weight ),
		} );

	public Node BoneMaskSwitch( Node condition, Node whenTrue, Node whenFalse, BoneMaskSelectionSettings settings = null )
	{
		settings ??= new BoneMaskSelectionSettings();

		return Add( NodeKind.BoneMask, "BoneMaskSwitch", new JsonObject
		{
			["Condition"] = Ref( condition ),
			["True"] = Ref( whenTrue ),
			["False"] = Ref( whenFalse ),
			["SwitchDynamically"] = settings.SwitchDynamically,
			["BlendTime"] = settings.BlendTime,
		} );
	}

	public Node BoneMaskSelector( Node id, Node fallback,
		IEnumerable<(string Id, Node Mask)> options, BoneMaskSelectionSettings settings = null )
	{
		settings ??= new BoneMaskSelectionSettings();

		var array = new JsonArray();

		foreach ( var (name, mask) in options ?? Array.Empty<(string, Node)>() )
		{
			array.Add( new JsonObject { ["Id"] = name ?? "", ["Mask"] = Ref( mask ) } );
		}

		return Add( NodeKind.BoneMask, "BoneMaskSelect", new JsonObject
		{
			["Id"] = Ref( id ),
			["Default"] = Ref( fallback ),
			["Options"] = array,
			["SwitchDynamically"] = settings.SwitchDynamically,
			["BlendTime"] = settings.BlendTime,
		} );
	}

	//-- choosing between many ------------------------------------------------

	/// <summary>The first option whose condition is true. Built up rather than passed in one go, because a selector is written option by option.</summary>
	public SelectorBuilder Selector()
	{
		var fields = new JsonObject { ["Options"] = new JsonArray() };
		var node = Add( NodeKind.Pose, "Select", fields );

		return new SelectorBuilder( this, node, fields );
	}

	/// <summary>The same, over clips - which can be swapped without the time they are at starting again.</summary>
	public Node ClipSelector( params (Node Option, Node Condition)[] cases )
	{
		var array = new JsonArray();

		foreach ( var (option, condition) in cases ?? Array.Empty<(Node, Node)>() )
		{
			array.Add( new JsonObject { ["Pose"] = Ref( option ), ["Condition"] = Ref( condition ) } );
		}

		return Add( NodeKind.Pose, "Select", new JsonObject
		{
			["Options"] = array,
			["ClipsOnly"] = true,
		} );
	}

	public sealed class SelectorBuilder
	{
		private readonly AnimGraphBuilder owner;
		private readonly Node node;
		private readonly JsonObject fields;

		internal SelectorBuilder( AnimGraphBuilder owner, Node node, JsonObject fields )
		{
			this.owner = owner;
			this.node = node;
			this.fields = fields;
		}

		public SelectorBuilder Add( Node option, Node condition )
		{
			fields["Options"].AsArray().Add( new JsonObject
			{
				["Pose"] = Ref( option ),
				["Condition"] = Ref( condition ),
			} );

			return this;
		}

		public Node Node => node;

		public static implicit operator Node( SelectorBuilder builder ) => builder.node;
	}

	/// <summary>By name: the option whose id matches.</summary>
	public Node IdBasedSelector( Node id, IEnumerable<(string Id, Node Pose)> options, IdBasedSelectorSettings settings = null )
	{
		settings ??= new IdBasedSelectorSettings();

		var array = new JsonArray();

		foreach ( var (name, pose) in options ?? Array.Empty<(string, Node)>() )
		{
			array.Add( new JsonObject { ["Id"] = name ?? "", ["Pose"] = Ref( pose ) } );
		}

		return Add( NodeKind.Pose, "IdSelect", new JsonObject
		{
			["Id"] = Ref( id ),
			["Options"] = array,
			["Fallback"] = Ref( settings.Fallback ),
			["IgnoreInvalid"] = settings.IgnoreInvalidOptions,
			["ClipsOnly"] = settings.ClipsOnly,
		} );
	}

	public Node IdBasedSelector( Node id, IEnumerable<(string Id, Node Pose)> options, Node fallback )
		=> IdBasedSelector( id, options, new IdBasedSelectorSettings { Fallback = fallback } );

	public Node IdBasedClipSelector( Node id, IEnumerable<(string Id, Node Pose)> options, Node fallback = default )
		=> IdBasedSelector( id, options, new IdBasedSelectorSettings { Fallback = fallback, ClipsOnly = true } );

	/// <summary>By a number: the options are spread across its range and the two either side of it are blended.</summary>
	public Node ParameterizedSelector( Node parameter, IEnumerable<Node> options, ParameterizedSelectorSettings settings = null )
	{
		settings ??= new ParameterizedSelectorSettings();

		return Add( NodeKind.Pose, "ParameterSelect", new JsonObject
		{
			["Parameter"] = Ref( parameter ),
			["Options"] = Refs( options ),
			["Weights"] = Numbers( settings.Weights?.Select( weight => (float)weight ) ),
			["HasWeights"] = settings.HasWeights,
			["IgnoreInvalid"] = settings.IgnoreInvalidOptions,
			["ClipsOnly"] = settings.ClipsOnly,
		} );
	}

	/// <summary>By how fast the character is going: each option carries its own speed, and the two either side of the wanted one are blended.</summary>
	public Node VelocityBlend( Node velocity, params Node[] sources )
		=> Add( NodeKind.Pose, "VelocityBlend", new JsonObject
		{
			["Velocity"] = Ref( velocity ),
			["Options"] = Refs( sources ),
		} );

	//-- blend spaces ---------------------------------------------------------

	//Synchronized is whether the poses it blends are put into one stride rather than each keeping its own time. What a locomotion space needs and a space of unrelated poses does not.
	/// <summary>Poses spread along a line, blended by where a value falls between them.</summary>
	public Node BlendSpace( Node parameter, IEnumerable<Node> sources, IEnumerable<float> positions,
		bool looping = false, bool synchronized = false )
		=> Add( NodeKind.Pose, "BlendSpace", new JsonObject
		{
			["Parameter"] = Ref( parameter ),
			["Sources"] = Refs( sources ),
			["Positions"] = Numbers( positions ),
			["Looping"] = looping,
			["Synchronized"] = synchronized,
		} );

	/// <summary>Blend space given as spans between pose pairs, as the editor bakes it</summary>
	public Node BlendSpace( Node parameter, IReadOnlyList<Node> sources,
		IReadOnlyList<(int Input0, int Input1, float Start, float End)> ranges, bool looping = false,
		bool synchronized = false )
	{
		var positions = new float[sources?.Count ?? 0];

		foreach ( var (first, second, start, end) in ranges ?? Array.Empty<(int, int, float, float)>() )
		{
			if ( first >= 0 && first < positions.Length ) positions[first] = start;
			if ( second >= 0 && second < positions.Length ) positions[second] = end;
		}

		return BlendSpace( parameter, sources, positions, looping, synchronized );
	}

	public BlendSpaceBuilder BlendSpace( Node parameter, bool looping = false )
	{
		var fields = new JsonObject
		{
			["Parameter"] = Ref( parameter ),
			["Sources"] = new JsonArray(),
			["Positions"] = new JsonArray(),
			["Looping"] = looping,
		};

		var node = Add( NodeKind.Pose, "BlendSpace", fields );

		return new BlendSpaceBuilder( node, fields );
	}

	public sealed class BlendSpaceBuilder
	{
		private readonly Node node;
		private readonly JsonObject fields;

		internal BlendSpaceBuilder( Node node, JsonObject fields )
		{
			this.node = node;
			this.fields = fields;
		}

		public BlendSpaceBuilder Add( float position, Node pose )
		{
			fields["Sources"].AsArray().Add( Ref( pose ) );
			fields["Positions"].AsArray().Add( position );

			return this;
		}

		public Node Node => node;

		public static implicit operator Node( BlendSpaceBuilder builder ) => builder.node;
	}

	/// <summary>The same across a plane: poses at points, blended by which triangle a pair of values lands in.</summary>
	public Node BlendSpace2D( Node x, Node y, BlendSpace2DSettings settings )
	{
		settings ??= new BlendSpace2DSettings();

		var positions = new JsonArray();

		foreach ( var point in settings.Positions ?? Array.Empty<Vector2>() )
		{
			positions.Add( new JsonArray { point.x, point.y } );
		}

		var indices = new JsonArray();
		foreach ( var index in settings.Indices ?? Array.Empty<int>() ) indices.Add( index );

		var hull = new JsonArray();
		foreach ( var index in settings.HullIndices ?? Array.Empty<int>() ) hull.Add( index );

		return Add( NodeKind.Pose, "BlendSpace2D", new JsonObject
		{
			["X"] = Ref( x ),
			["Y"] = Ref( y ),
			["Sources"] = Refs( settings.Sources ),
			["Positions"] = positions,
			["Indices"] = indices,
			["Hull"] = hull,
			["Looping"] = settings.Looping,
			["Synchronized"] = settings.Synchronized,
		} );
	}

	public BlendSpace2DBuilder BlendSpace2D( Node x, Node y, bool looping = false )
		=> new( this, x, y, looping );

	public sealed class BlendSpace2DBuilder
	{
		private readonly AnimGraphBuilder owner;
		private readonly Node x, y;
		private readonly bool looping;

		private readonly List<Node> sources = new();
		private readonly List<Vector2> positions = new();

		private Node built;

		internal BlendSpace2DBuilder( AnimGraphBuilder owner, Node x, Node y, bool looping )
		{
			this.owner = owner;
			this.x = x;
			this.y = y;
			this.looping = looping;
		}

		public BlendSpace2DBuilder Add( Vector2 position, Node pose )
		{
			sources.Add( pose );
			positions.Add( position );

			return this;
		}

		/// <summary>Built on the way out rather than as it goes, because the triangles between the points cannot be worked out until they are all in.</summary>
		public Node Node
		{
			get
			{
				if ( built is { IsValid: true } ) return built;

				TriangulateBlendSpace( positions, out var triangles, out var hull );

				built = owner.BlendSpace2D( x, y, new BlendSpace2DSettings
				{
					Sources = sources,
					Positions = positions,
					Indices = triangles,
					HullIndices = hull,
					Looping = looping,
				} );

				return built;
			}
		}

		public static implicit operator Node( BlendSpace2DBuilder builder ) => builder.Node;
	}

	//-- being hit ------------------------------------------------------------

	/// <summary>A shove where the character was hit, springing back to where the animation had it.</summary>
	public Node HitReact( Node pose, Node trigger, Node bone, Node offset, Node direction, Node strength,
		HitReactSettings settings = null )
	{
		settings ??= new HitReactSettings();

		return Add( NodeKind.Pose, "HitReact", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Trigger"] = Ref( trigger ),
			["HitBone"] = Ref( bone ),
			["HitOffset"] = Ref( offset ),
			["HitDirection"] = Ref( direction ),
			["HitStrength"] = Ref( strength ),
			["BoneCount"] = settings.BoneCount,
			["MinImpactForce"] = settings.MinImpactForce,
			["MaxImpactForce"] = settings.MaxImpactForce,
			["SpringStrength"] = settings.SpringStrength,
			["CounterRotationScale"] = settings.CounterRotationScale,
			["PropagationScale"] = settings.PropagationScale,
			["MinDelayBetweenHits"] = settings.MinDelayBetweenHits,
		} );
	}

	public sealed class HitReactSettings
	{
		/// <summary>How far up the chain the blow is felt, counting the bone hit.</summary>
		public int BoneCount { get; set; } = 3;

		public float MinImpactForce { get; set; } = 50.0f;
		public float MaxImpactForce { get; set; } = 250.0f;

		/// <summary>How hard it springs back.</summary>
		public float SpringStrength { get; set; } = 10.0f;

		/// <summary>How much the bone above turns the other way.</summary>
		public float CounterRotationScale { get; set; } = 0.6f;

		/// <summary>What is left of the blow one bone further up.</summary>
		public float PropagationScale { get; set; } = 1.0f;

		public float MinDelayBetweenHits { get; set; }
	}

	//-- variations -----------------------------------------------------------

	/// <summary>One of several poses, picked at random and weighted - the same idle played a different way each time.</summary>
	public VariationBuilder Variation( Node reset = default )
	{
		var fields = new JsonObject
		{
			["Reset"] = Ref( reset ),
			["Options"] = new JsonArray(),
		};

		var node = Add( NodeKind.Pose, "Variation", fields );

		return new VariationBuilder( node, fields );
	}

	public Node ClipVariation( Node reset, params (Node Pose, int Weight)[] options )
	{
		var builder = Variation( reset );

		foreach ( var (pose, weight) in options ?? Array.Empty<(Node, int)>() ) builder.Add( pose, weight );

		return builder;
	}

	public sealed class VariationBuilder
	{
		private readonly Node node;
		private readonly JsonObject fields;

		internal VariationBuilder( Node node, JsonObject fields )
		{
			this.node = node;
			this.fields = fields;
		}

		public VariationBuilder Add( Node pose, int weight )
		{
			fields["Options"].AsArray().Add( new JsonObject
			{
				["Pose"] = Ref( pose ),
				["Weight"] = weight,
			} );

			return this;
		}

		public Node Node => node;

		public static implicit operator Node( VariationBuilder builder ) => builder.node;
	}

	//-- layers ---------------------------------------------------------------

	/// <summary>A base pose with more put on top of it - an upper body over a walk, a flinch over both.</summary>
	public LayersBuilder Layers( Node basePose )
	{
		var fields = new JsonObject
		{
			["Base"] = Ref( basePose ),
			["Layers"] = new JsonArray(),
			["RootMotion"] = false,
		};

		var node = Add( NodeKind.Pose, "Layers", fields );

		return new LayersBuilder( node, fields );
	}

	public sealed class LayersBuilder
	{
		private readonly Node node;
		private readonly JsonObject fields;

		internal LayersBuilder( Node node, JsonObject fields )
		{
			this.node = node;
			this.fields = fields;
		}

		//Which layer this became, counting from the bottom.
		public int AddLayer( Node pose, LayerSettings settings = null )
		{
			settings ??= new LayerSettings();

			var layers = fields["Layers"].AsArray();

			layers.Add( new JsonObject
			{
				["Pose"] = Ref( pose ),
				["Weight"] = Ref( settings.Weight ),
				["Mask"] = Ref( settings.BoneMask ),
				["BlendMode"] = settings.BlendMode.ToString(),
				["Synchronized"] = settings.Synchronized,
				["RotationOnly"] = settings.RotationOnly,
				["ChannelsApart"] = settings.ChannelsApart,
				["ModelSpace"] = settings.ModelSpace,
				["IgnoreEvents"] = settings.IgnoreEvents,
				["RootMotionWeight"] = Ref( settings.RootMotionWeight ),
			} );

			return layers.Count - 1;
		}

		public int AddLayer( StateMachineBuilder machine, LayerSettings settings = null )
			=> AddLayer( machine.Node, settings );

		/// <summary>A layer whose weight and mask come from the states inside it rather than from here - which is what a state machine layer is for.</summary>
		public int AddStateMachineLayer( Node machine, LayerSettings settings = null )
		{
			var layer = AddLayer( machine, settings );

			fields["Layers"].AsArray()[layer].AsObject()["FromStates"] = true;

			return layer;
		}

		/// <summary>Whether the layers move the character as well as pose it. Off by default: an upper body layer that also walked would walk twice.</summary>
		public LayersBuilder IncludeLayerRootMotion( bool include = true )
		{
			fields["RootMotion"] = include;
			return this;
		}

		public Node Node => node;

		public static implicit operator Node( LayersBuilder builder ) => builder.node;
	}

	//-- graphs inside graphs -------------------------------------------------

	/// <summary>A pose the game puts in from outside, by slot name - a ragdoll, a recorded pose, anything the graph does not produce itself.</summary>
	public Node ExternalPose( string slot, bool sampleRootMotion = false )
		=> Add( NodeKind.Pose, "ExternalPose", new JsonObject
		{
			["Slot"] = slot ?? "",
			["RootMotion"] = sampleRootMotion,
		} );

	public Node IsExternalPoseSet( Node slot )
		=> Add( NodeKind.Bool, "ExternalPoseSet", new JsonObject { ["Slot"] = Ref( slot ) } );

	/// <summary>A whole graph the game plugs in by slot name, with something to play while nothing is plugged in.</summary>
	public Node ExternalGraphSlot( string slot, Node fallback = default )
		=> Add( NodeKind.Pose, "ExternalGraph", new JsonObject
		{
			["Slot"] = slot ?? "",
			["Fallback"] = Ref( fallback ),
		} );

	public Node IsExternalGraphSlotFilled( Node slot )
		=> Add( NodeKind.Bool, "ExternalGraphFilled", new JsonObject { ["Slot"] = Ref( slot ) } );

	/// <summary>Another graph, named and played inside this one.</summary>
	public Node ReferencedGraph( string graph, Node fallback = default )
		=> Add( NodeKind.Pose, "ReferencedGraph", new JsonObject
		{
			["Graph"] = graph ?? "",
			["Fallback"] = Ref( fallback ),
		} );

	//-- movement -------------------------------------------------------------

	/// <summary>Take the movement out of the animation and put the game's in - what makes a walk cycle go where the character is actually going.</summary>
	public Node RootMotionOverride( Node pose, RootMotionOverrideSettings settings = null )
	{
		settings ??= new RootMotionOverrideSettings();

		return Add( NodeKind.Pose, "RootMotionOverride", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Velocity"] = Ref( settings.MovingVelocity ),
			["Facing"] = Ref( settings.FacingDirection ),
			["LinearLimit"] = Ref( settings.LinearVelocityLimit ),
			["AngularLimit"] = Ref( settings.AngularVelocityLimit ),
			["MaxLinear"] = settings.MaxLinearVelocity,
			["MaxAngular"] = settings.MaxAngularVelocityDegrees,
			["Flags"] = (int)settings.Flags,
		} );
	}

	/// <summary>Bend the animation's own path so it ends up facing where it should - a turn played straight, made to turn as far as is wanted.</summary>
	public Node OrientationWarp( Node pose, Node direction, bool relativeToCharacter, RootMotionSamplingMode sampling )
		=> Add( NodeKind.Pose, "OrientationWarp", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Direction"] = Ref( direction ),
			["Relative"] = relativeToCharacter,
			["Sampling"] = sampling.ToString(),
		} );

	/// <summary>The same, towards a place rather than a direction.</summary>
	public Node TargetWarp( Node pose, Node target, TargetWarpSettings settings = null )
	{
		settings ??= new TargetWarpSettings();

		return Add( NodeKind.Pose, "TargetWarp", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Target"] = Ref( target ),
			["UpdateRule"] = settings.UpdateRule.ToString(),
			["Sampling"] = settings.SamplingMode.ToString(),
			["AlignAtLastEvent"] = settings.AlignAtLastWarpEvent,
			["AlignmentBone"] = settings.AlignmentBone ?? "",
			["ErrorThreshold"] = settings.SamplingPositionErrorThreshold,
			["MaxTangent"] = settings.MaxTangentLength,
			["LerpFallback"] = settings.LerpFallbackDistance,
			["UpdateDistance"] = settings.TargetUpdateDistance,
			["UpdateAngle"] = settings.TargetUpdateAngleDegrees,
		} );
	}

	//-- reaching -------------------------------------------------------------

	/// <summary>Difference between two poses (pose minus reference), the input to an additive layer</summary>
	public Node PoseDifference( Node pose, Node reference )
		=> Add( NodeKind.Pose, "Subtract", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Base"] = Ref( reference ),
		} );

	/// <summary>Put the playhead of <paramref name="pose"/> where <paramref name="cycle"/> says, instead of letting it run.</summary>
	public Node CycleControl( Node pose, Node cycle, bool inSeconds = false )
		=> Add( NodeKind.Pose, "CycleControl", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Cycle"] = Ref( cycle ),
			["InSeconds"] = inSeconds,
		} );

	//An effector is a bone to reach with instead, for a graph written before chains. The chain wins where both are given.
	/// <summary>Bend a limb the model names so its tip lands on a target.</summary>
	public Node Ik( Node pose, Node target, IkSettings settings = null, string effector = null )
	{
		settings ??= new IkSettings();

		return Add( NodeKind.Pose, "Ik", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["Chain"] = settings.Chain ?? "",
			["Bone"] = effector ?? "",
			["Target"] = Ref( target ),
			["Enabled"] = Ref( settings.Enabled ),
			["BlendTime"] = settings.BlendTime,
			["WorldSpace"] = settings.WorldSpace,
			["BlendMode"] = settings.BlendMode.ToString(),
			["Twist"] = settings.TwistWeight,
		} );
	}

	/// <summary>What an IK node was called when a limb had to be two bones.</summary>
	public Node TwoBoneIK( Node pose, string effector, Node target, IkSettings settings = null )
		=> Ik( pose, target, settings, effector );

	/// <summary>The same for both feet at once, which is how ground is stood on.</summary>
	public Node FootIK( Node pose, string leftFoot, string rightFoot, FootIKSettings settings = null )
	{
		settings ??= new FootIKSettings();

		return Add( NodeKind.Pose, "FootIK", new JsonObject
		{
			["Pose"] = Ref( pose ),
			["LeftChain"] = settings.LeftChain ?? "",
			["RightChain"] = settings.RightChain ?? "",
			["Left"] = leftFoot ?? "",
			["Right"] = rightFoot ?? "",
			["LeftTarget"] = Ref( settings.LeftTarget ),
			["RightTarget"] = Ref( settings.RightTarget ),
			["Enabled"] = Ref( settings.Enabled ),
			["BlendTime"] = settings.BlendTime,
			["BlendMode"] = settings.BlendMode.ToString(),
			["WorldSpace"] = settings.WorldSpace,
		} );
	}
}
