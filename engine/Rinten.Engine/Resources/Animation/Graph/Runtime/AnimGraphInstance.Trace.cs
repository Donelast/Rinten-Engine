using System.Text;
using System.Text.Json.Nodes;
using Rinten.CoreAnimation;

namespace Rinten.Animation;

//Logs what a graph did on an update, for debugging in-game; off unless enabled by anim_trace
internal sealed partial class AnimGraphInstance
{
	//How many more graph updates to write out. Also settable before anything has loaded, as -animtrace 1 , which is the only way to see the
	//first update a character ever plays - by the time a console is there to type in, whatever went wrong on frame one is a hundred frames back.
	internal static int TraceUpdates = Rinten.Utility.CommandLine.GetSwitchInt( "-animtrace", 0 );

	//Only models whose path holds this, where it is set. -animtraceonly .
	internal static string TraceOnly =
		Rinten.Utility.CommandLine.GetSwitch( "-animtraceonly", "" ).TrimQuoted();

	//Whether this update is one of the ones being written out.
	private bool Tracing => TraceUpdates > 0
		&& ( string.IsNullOrEmpty( TraceOnly )
			|| ( model?.ResourcePath ?? "" ).Contains( TraceOnly, StringComparison.OrdinalIgnoreCase ) );

	//How much of this update has been written out already.
	private int lines;

	//Whether a node may write a measurement of its own into the report.
	internal bool Measuring => Tracing;

	//Logs one bone of a mid-graph pose, to find which layer broke the body's shape
	internal void Note( string what, int pose, string bone = "spine_1" )
	{
		if ( !Tracing || instance is null || pose < 0 ) return;

		var at = instance.Skeleton?.Find( bone ) ?? -1;
		if ( at < 0 ) return;

		var held = instance.Pose( pose );
		if ( held is null || at >= held.Length ) return;

		//Kept apart from the report and printed with it: these are written while the graph runs and the
		//report is written afterwards, so anything said straight into it is said before it is cleared.
		notes.Append( $"   [{bone} {held[at].Position.Length * 100.0f:0.0}cm]  {what}" ).Append( (char)10 );
	}

	//Records each node's output pose depth-first, so the first wrong line is the node that broke it
	internal void Measured( GraphNode node, int pose )
	{
		if ( !Tracing || instance is null || pose < 0 || measurements > 400 ) return;

		var skeleton = instance.Skeleton;
		if ( skeleton is null ) return;

		var held = instance.Pose( pose );
		if ( held is null ) return;

		//Lower case, which is how the skeleton keeps its names - see the report below, which looks its own bones up the same way.
		var spine = skeleton.Find( "spine_1" );
		var hand = skeleton.Find( "hand_r" );
		var ankle = skeleton.Find( "ankle_r" );

		++measurements;

		var root = held.Length > 0 ? held[0] : default;

		var shape = new StringBuilder();
		shape.Append( $"root({root.Position.x * 100.0f:0}, {root.Position.y * 100.0f:0}, {root.Position.z * 100.0f:0})" );
		shape.Append( $" rot({root.Rotation.X:0.00},{root.Rotation.Y:0.00},{root.Rotation.Z:0.00},{root.Rotation.W:0.00})" );
		shape.Append( $" scale({root.Scale.x:0.00})" );

		if ( spine >= 0 && spine < held.Length )
			shape.Append( $" spine_1 {held[spine].Position.Length * 100.0f:0.0}cm" );

		if ( hand >= 0 && hand < held.Length )
			shape.Append( $" hand_R {held[hand].Position.Length * 100.0f:0.0}cm" );

		if ( ankle >= 0 && ankle < held.Length )
			shape.Append( $" ankle_R {held[ankle].Position.Length * 100.0f:0.0}cm" );

		notes.Append( $"   #{node.Index} {node.GetType().Name.Replace( "Node", "" )}  {shape}" ).Append( (char)10 );
	}

	//How many nodes have been measured this update - see Measured.
	private int measurements;

	//The whole report, written as one entry. Two characters are posed at once and on different threads, so a report written a line
	//at a time comes out of the log shuffled together with another character's - which is unreadable exactly where it matters.
	private readonly StringBuilder said = new();

	//What the nodes measured while the graph ran - see Note.
	private readonly StringBuilder notes = new();

	private void Say( string line ) => said.Append( line ).Append( (char)10 );

	//Every node the update went through, what it played and what came of it.
	private void Trace()
	{
		--TraceUpdates;

		var definition = graph?.Graph;
		if ( definition is null ) return;

		said.Clear();
		measurements = 0;
		Say( "----" );
		Say( $"model {model?.ResourcePath ?? "<none>"}" );
		Say( $"graph {Name}, {definition.Nodes.Count} node(s), root {definition.Root}" );

		Values( definition );

		lines = 0;

		var seen = new HashSet<int>();

		Walk( definition, definition.Root, 0, seen );

		Playing( definition );

		Bones();

		if ( notes.Length > 0 )
		{
			Say( "measured as it ran:" );
			said.Append( notes );
			notes.Clear();
		}

		Log.Info( said.ToString() );
	}

	//Every clip this update sampled, whatever part of the graph asked for it. The tree above walks each node once, so a subtree reached twice is drawn
	//once - which is fine for reading the shape and no good for the question this answers, which is what the character is actually being posed out of.
	private void Playing( AnimGraph.Definition definition )
	{
		var played = new SortedDictionary<string, int>( StringComparer.OrdinalIgnoreCase );

		for ( var index = 0; index < definition.Nodes.Count; index++ )
		{
			if ( !watching.TryGetInfo( index, out var info ) || !info.IsActive ) continue;

			var kind = Text( definition.Nodes[index], "Kind" );
			if ( kind is not ( "Clip" or "Pose" ) ) continue;

			var clip = Text( definition.Nodes[index], "Clip" );
			if ( string.IsNullOrEmpty( clip ) ) continue;

			played[clip] = played.GetValueOrDefault( clip ) + 1;
		}

		//How far through each clip that ran actually is. A loop that catches is a phase that jumps, and a phase that jumps shows here as
		//a number that goes backwards by more than a frame between two updates - which no amount of looking at the tree will tell you.
		var phases = new StringBuilder();

		for ( var index = 0; index < nodes.Length; index++ )
		{
			if ( nodes[index] is not ClipNode clip ) continue;
			if ( !watching.TryGetInfo( index, out var running ) || !running.IsActive ) continue;

			phases.Append( $"  #{index}={clip.Progress( context ):0.000}" );
		}

		if ( phases.Length > 0 ) Say( $"how far through, per clip:{phases}" );

		Say( $"clips this update ({played.Count}):" );

		foreach ( var (clip, count) in played )
		{
			Say( $"   {clip} x{count}" +
				( CoreSkeletons.ClipPath( model, clip ) is { Length: > 0 } path ? "" : "   <the model has no such clip>" ) );
		}
	}

	//What the game has set on it, which is what it is answering.
	private void Values( AnimGraph.Definition definition )
	{
		var line = new StringBuilder();

		foreach ( var parameter in definition.Described )
		{
			if ( !byName.TryGetValue( parameter.Name ?? "", out var index ) ) continue;

			var value = parameter.Kind switch
			{
				AnimGraphBuilder.NodeKind.Bool => values[index].Flag ? "true" : "false",
				AnimGraphBuilder.NodeKind.Id => values[index].Id ?? "",
				AnimGraphBuilder.NodeKind.Vector => values[index].Vector.ToString(),
				_ => values[index].Number.ToString( "0.###" ),
			};

			//A parameter still sitting at nothing says nothing about what the character is doing.
			if ( value is "" or "0" or "false" or "0,0,0" ) continue;

			line.Append( line.Length > 0 ? ", " : "" ).Append( parameter.Name ).Append( '=' ).Append( value );
		}

		Say( $"set: {( line.Length > 0 ? line.ToString() : "nothing" )}" );
	}

	//Down from the root, through the nodes this update actually reached. Reached, rather than drawn: a graph is a thousand nodes and
	//a frame goes through a few dozen of them. Which few is the whole question when a character is posed by something nobody expected.
	private void Walk( AnimGraph.Definition definition, int index, int depth, HashSet<int> seen )
	{
		if ( index < 0 || index >= definition.Nodes.Count ) return;
		if ( depth > 64 || lines > 500 || !seen.Add( index ) ) return;

		if ( watching is null || !watching.TryGetInfo( index, out var info ) || !info.IsActive ) return;

		var node = definition.Nodes[index];
		var kind = Text( node, "Kind" );

		var drawn = new StringBuilder();

		drawn.Append( new string( ' ', depth * 2 ) ).Append( '#' ).Append( index ).Append( ' ' ).Append( kind );

		Describe( definition, node, kind, index, drawn );

		Say( drawn.ToString() );
		++lines;

		foreach ( var child in Children( definition, node ) )
			Walk( definition, child, depth + 1, seen );
	}

	//What a node of this kind is worth saying beyond its name.
	private void Describe( AnimGraph.Definition definition, JsonObject node, string kind,
		int index, StringBuilder said )
	{
		switch ( kind )
		{
			case "Clip":
			case "Pose":
			{
				var clip = Text( node, "Clip" );

				said.Append( " '" ).Append( clip ).Append( '\'' );

				//Which file it is, which tells apart two clips of the same name imported from different sources.
				if ( CoreSkeletons.ClipPath( model, clip ) is { Length: > 0 } path )
					said.Append( " -> " ).Append( path );
				else
					said.Append( " -> <not in the model>" );

				if ( watching.TryGetInfo( index, out var info ) )
					said.Append( $"  t={info.CurrentTime:0.00}/{info.Duration:0.00}" );

				break;
			}

			case "Layers":
			{
				if ( node["Layers"] is not JsonArray layers ) break;

				for ( var i = 0; i < layers.Count; i++ )
				{
					if ( layers[i] is not JsonObject layer ) continue;

					watching.TryGetLayerWeight( index, i, out var weight );

					said.Append( $"  [{i}] {Text( layer, "BlendMode" )}" );

					if ( Flag( layer, "ModelSpace" ) ) said.Append( "+model" );
					if ( Flag( layer, "RotationOnly" ) ) said.Append( "+rotonly" );
					if ( Flag( layer, "ChannelsApart" ) ) said.Append( "+apart" );

					said.Append( $" w={weight:0.00}" );

					if ( Mask( definition, layer["Mask"] ) is { Length: > 0 } mask )
						said.Append( " mask=" ).Append( mask );
				}

				break;
			}

			case "LookAt":
				said.Append( $"  chain={Text( node, "Chain" )} aim={Text( node, "Attachment" )}" );
				break;

			case "BoneMask":
				said.Append( $"  '{Text( node, "Name" )}'" );
				break;

			case "StateMachine":
			{
				if ( !watching.TryGetState( index, out var state, out var from, out var transition, out var progress ) )
					break;

				said.Append( $"  state {state}" );

				if ( from >= 0 && from != state ) said.Append( $" <- {from} ({progress:0.00})" );
				if ( transition >= 0 ) said.Append( $" through {transition}" );

				break;
			}

			case "Blend":
			case "BlendSpace":
			case "BlendSpace2D":
			{
				if ( watching.TryGetBlend2D( index, out var at ) ) said.Append( $"  at ({at.x:0.0},{at.y:0.0})" );
				else if ( watching.TryGetBlend( index, out _, out _, out var weight ) ) said.Append( $"  w={weight:0.00}" );

				break;
			}

			default:
			{
				if ( watching.TryGetValue( index, out var value )
					&& watching.TryGetInfo( index, out var info )
					&& info.ValueType != AnimGraphNodeValueType.Pose )
				{
					said.Append( "  = " ).Append( info.ValueType switch
					{
						AnimGraphNodeValueType.Float => value.Float.ToString( "0.###" ),
						AnimGraphNodeValueType.Bool => value.Bool ? "true" : "false",
						AnimGraphNodeValueType.ID => watching.Name( value.ID ) ?? "",
						AnimGraphNodeValueType.Vector => value.Vector.ToString(),
						_ => "",
					} );
				}

				break;
			}
		}
	}

	//A node index, where that is what the number is. Everything in the definition is numbers - weights, times, positions - and only the whole ones
	//are ever a node. A time of four seconds read as a node is a node, which is how a walk of the graph ends up somewhere it was never wired to.
	private static int? Whole( JsonNode value )
	{
		if ( value?.GetValueKind() != System.Text.Json.JsonValueKind.Number ) return null;

		return value.AsValue().TryGetValue<int>( out var index ) ? index : null;
	}

	//The name of a mask node, since a layer names it by number.
	private static string Mask( AnimGraph.Definition definition, JsonNode reference )
	{
		if ( reference is null ) return null;
		if ( reference.GetValueKind() != System.Text.Json.JsonValueKind.Number ) return null;

		if ( Whole( reference ) is not { } at ) return null;
		if ( at < 0 || at >= definition.Nodes.Count ) return null;

		return Text( definition.Nodes[at], "Name" ) is { Length: > 0 } named ? named : Text( definition.Nodes[at], "Kind" );
	}

	//Every node a node points at, whatever the field is called.
	private static IEnumerable<int> Children( AnimGraph.Definition definition, JsonObject node )
	{
		foreach ( var pair in node )
		{
			if ( pair.Key is "Kind" or "Parameter" ) continue;

			foreach ( var index in Indices( pair.Value ) )
			{
				if ( index >= 0 && index < definition.Nodes.Count ) yield return index;
			}
		}
	}

	private static IEnumerable<int> Indices( JsonNode value )
	{
		switch ( value )
		{
			case null:
				yield break;

			case JsonArray array:
			{
				foreach ( var item in array )
				{
					foreach ( var index in Indices( item ) ) yield return index;
				}

				yield break;
			}

			case JsonObject entry:
			{
				foreach ( var pair in entry )
				{
					if ( Whole( pair.Value ) is { } index ) yield return index;
				}

				yield break;
			}
		}

		if ( Whole( value ) is { } at ) yield return at;
	}

	//What the pose did to the skeleton, which is the part a clip from the wrong rig gives itself away by. A pose may turn a bone as far as it likes; it
	//may not change how far that bone sits from its parent. Anything here that moved is a clip authored on another skeleton being played on this one.
	private void Bones()
	{
		if ( instance is not { } live ) return;

		var pose = live.Local;
		var bones = live.Skeleton.Bones;

		if ( pose is null || bones is null ) return;

		var moved = new List<(float Off, string Said)>();

		for ( var bone = 0; bone < pose.Length && bone < bones.Length; bone++ )
		{
			var was = bones[bone].Rest.Position.Length;
			var now = pose[bone].Position.Length;

			var off = MathF.Abs( now - was );
			if ( off <= 0.002f ) continue;

			moved.Add( (off, $"{bones[bone].Name} {was * 100.0f:0.0}->{now * 100.0f:0.0}cm") );
		}

		moved.Sort( ( a, b ) => b.Off.CompareTo( a.Off ) );

		var line = new StringBuilder();

		foreach ( var named in new[] { "pelvis", "spine_1", "neck_0", "hand_R", "ankle_R" } )
		{
			var bone = live.Skeleton.Find( named.ToLowerInvariant() );
			if ( bone < 0 || bone >= pose.Length ) continue;

			line.Append( $"  {named} {pose[bone].Position.Length * 100.0f:0.0}/" +
				$"{bones[bone].Rest.Position.Length * 100.0f:0.0}cm" );
		}

		//Whether what is being read back is still the graph's answer. A ragdoll and the network both write bones straight onto the pose after the
		//graph has had its say, and a report that does not tell them apart sends somebody looking for a broken layer in a graph that was fine.
		Say( $"bones written from outside the graph: {live.OverrideCount}" );

		//Named rather than counted: a count says a character is the wrong shape and the
		//names say which part of it, which is the difference between a layer and a whole rig.
		Say( $"bones whose length moved: {moved.Count}" +
			( moved.Count > 0 ? "   worst " + string.Join( ", ", moved.Take( 12 ).Select( entry => entry.Said ) ) : "" ) );
		Say( $"lengths now/rest:{line}" );

		Standing( live, pose );
	}

	//Logs final joint positions relative to the pelvis, so the same pose reads the same anywhere
	private void Standing( CoreAnimRuntime.Instance live, AnimTrs[] pose )
	{
		var root = live.Skeleton.Find( "pelvis" );
		if ( root < 0 ) return;

		var origin = CoreAnimRuntime.ModelOf( pose, live.Skeleton, root );

		var line = new StringBuilder();

		foreach ( var named in new[]
		{
			"spine_2", "neck_0", "head", "clavicle_R", "arm_upper_R", "arm_lower_R", "hand_R",
			"clavicle_L", "arm_upper_L", "arm_lower_L", "hand_L", "hold_R",
			"leg_upper_R", "leg_lower_R", "ankle_R", "ankle_L",
		} )
		{
			var bone = live.Skeleton.Find( named.ToLowerInvariant() );
			if ( bone < 0 || bone >= pose.Length ) continue;

			var at = AnimTrs.Then( CoreAnimRuntime.ModelOf( pose, live.Skeleton, bone ), AnimTrs.Invert( origin ) );

			line.Append( $"  {named}({at.Position.x * 100.0f:0},{at.Position.y * 100.0f:0},{at.Position.z * 100.0f:0})" );
		}

		Say( $"joints from the pelvis, in cm:{line}" );

		//Up and forward in model space; two directions since one can't tell a turn from a tumble
		Vector3 Between( string from, string to )
		{
			var a = live.Skeleton.Find( from );
			var b = live.Skeleton.Find( to );

			if ( a < 0 || b < 0 || a >= pose.Length || b >= pose.Length ) return Vector3.Zero;

			return ( CoreAnimRuntime.ModelOf( pose, live.Skeleton, b ).Position
				- CoreAnimRuntime.ModelOf( pose, live.Skeleton, a ).Position ).Normal;
		}

		//Where the things a leg is solved towards actually are, in the model's own space: an inverted knee is either
		//a solver reaching wrongly or a target that is not where it should be, and only these tell the two apart.
		var where = new StringBuilder();

		foreach ( var named in new[] { "pelvis", "root_ik", "ankle_l", "ball_l", "foot_l_ik_target", "head" } )
		{
			var bone = live.Skeleton.Find( named );
			if ( bone < 0 || bone >= pose.Length ) continue;

			var stands = CoreAnimRuntime.ModelOf( pose, live.Skeleton, bone ).Position;

			where.Append( $"  {named}({stands.x * 100.0f:0},{stands.y * 100.0f:0},{stands.z * 100.0f:0})" );
		}

		Say( $"in the model's own space, cm:{where}" );

		//The root's own local turn, which is the one number that says whether the pose being played
		//is the one on disk: a clip built with a model turn carries it here and nowhere else.
		var pelvis = live.Skeleton.Find( "pelvis" );

		if ( pelvis >= 0 && pelvis < pose.Length )
		{
			var q = pose[pelvis].Rotation;
			var rest = live.Skeleton.Bones[pelvis].Rest.Rotation;

			Say( $"the pelvis, as played: ({q.X:0.000},{q.Y:0.000},{q.Z:0.000},{q.W:0.000})" +
				$"   as the model rests it: ({rest.X:0.000},{rest.Y:0.000},{rest.Z:0.000},{rest.W:0.000})" );
		}

		//Where the head actually points against where it was asked to. The aim is set in the object's space and the head is posed in the
		//model's, and those are the same space only if nothing has been baked between them - so the two printed together is the whole question.
		var looking = live.Skeleton.Find( "head" );

		if ( looking >= 0 && looking < pose.Length )
		{
			var head = CoreAnimRuntime.ModelOf( pose, live.Skeleton, looking );

			var asked = Vector3.Zero;

			if ( byName.TryGetValue( "aim_head", out var index ) ) asked = values[index].Vector;

			var facing = head.ToTransform().Rotation;

			//Against its own rest, because a bone's axes are the rig's business and not the engine's: the head's own forward is along
			//its +X here. How far it has turned from where the model rests it is the only rig-free way to say whether it is looking up.
			var resting = AnimTrs.Identity;

			for ( var bone = looking; bone >= 0; bone = live.Skeleton.Bones[bone].Parent )
				resting = AnimTrs.Then( resting, live.Skeleton.Bones[bone].Rest );

			var from = resting.ToTransform().Rotation;
			var turned = from.Inverse * facing;

			Say( $"the head: asked for {asked.Normal}, turned from its rest by {turned.Angles()}" +
				$"   (its rest looks {Vector3.Forward * from}, it now looks {Vector3.Forward * facing})" );
		}

		Say( $"which way it stands: pelvis->head {Between( "pelvis", "head" )}, " +
			$"ankle->ball {Between( "ankle_l", "ball_l" )}, " +
			$"eye_L->eye_R {Between( "eye_l", "eye_r" )}   " +
			$"(forward is {Vector3.Forward}, up is {Vector3.Up})" );
	}

	private static string Text( JsonObject node, string name )
		=> node?[name]?.GetValueKind() == System.Text.Json.JsonValueKind.String ? node[name].GetValue<string>() : "";

	private static bool Flag( JsonObject node, string name )
		=> node?[name]?.GetValueKind() == System.Text.Json.JsonValueKind.True;
}

//Graph parameters forced from the command line (-animset "move_y=1.9,holdtype=rifle"), every update
internal sealed partial class AnimGraphInstance
{
	private static readonly string[] Forced = Rinten.Utility.CommandLine
		.GetSwitch( "-animset", "" ).TrimQuoted()
		.Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries );

	//Whether anything was asked for at all, so an ordinary run pays nothing.
	private static bool Forcing => Forced.Length > 0;

	private void Force()
	{
		if ( !Forcing ) return;

		foreach ( var pair in Forced )
		{
			var split = pair.IndexOf( '=' );
			if ( split <= 0 || split >= pair.Length - 1 ) continue;

			var name = pair[..split].Trim();
			var said = pair[( split + 1 )..].Trim();

			if ( !byName.TryGetValue( name, out var index ) ) continue;

			switch ( kinds[index] )
			{
				case AnimGraphBuilder.NodeKind.Bool:
					SetBool( name, said is "1" or "true" or "yes" );
					break;

				case AnimGraphBuilder.NodeKind.Id:
					SetId( name, said );
					break;

				case AnimGraphBuilder.NodeKind.Vector:
				{
					var parts = said.Split( '/', StringSplitOptions.RemoveEmptyEntries );

					float Number( int at ) => parts.Length > at
						&& float.TryParse( parts[at], System.Globalization.NumberStyles.Float,
							System.Globalization.CultureInfo.InvariantCulture, out var read ) ? read : 0.0f;

					SetVector( name, new Vector3( Number( 0 ), Number( 1 ), Number( 2 ) ) );
					break;
				}

				default:
					if ( float.TryParse( said, System.Globalization.NumberStyles.Float,
						System.Globalization.CultureInfo.InvariantCulture, out var value ) )
					{
						SetFloat( name, value );
					}

					break;
			}
		}
	}
}
