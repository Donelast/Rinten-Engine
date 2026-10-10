using System;
using System.Collections.Generic;

namespace Rinten;

//Lamp shadow maps per drawer: faces rebaked only on change, movers drawn into a separate map
//Faces sized by screen coverage, only visible faces drawn, with a Guard border for filtering
internal sealed class CoreLampMaps : IDisposable
{
	//Layer width (finest face), coarsest face and guard border; match LampLayerTexels/LampGuardTexels
	internal const int Side = 2048;
	internal const int Smallest = 256;
	internal const int Guard = 4;

	//How many sizes a face comes in: Side halved this many times less one.
	private const int Sizes = 4;

	//How many layers the map has at the most - sixteen megabytes a layer, a hundred and twenty-eight in all, and as much again for the movers' map
	//beside it: one lamp at the finest and two more beside it, or a room of them at a coarser size - and how many views of faces one frame draws.
	internal const int MostLayers = 8;
	internal const int MostFaces = 14;

	//Where a face's depth starts, in metres from the lamp - LampNear in frame.slang is the same number.
	internal const float Near = 0.05f;

	//How much of a face's side its ninety degrees take at this many texels across: the rest is the border.
	internal static float FitOf( int size ) => ( size - ( 2.0f * Guard ) ) / size;

	//How many texels a face of a size class is across - nought is Side, each after it half.
	private static int SizeOf( int sizeClass ) => Side >> sizeClass;

	//How many faces of a size class one layer holds.
	private static int CellsOf( int sizeClass ) => 1 << ( 2 * sizeClass );

	//The widest a spot's shadow is kept for, as a half-angle: past it the texels spread too thin to be worth drawing.
	private const float WidestSpotDegrees = 80.0f;

	private readonly CoreCasters casters;

	private ulong map;
	private int layers;

	//What moves in each face, square for square where map has it - see the remarks on the class.
	private ulong movers;

	//Which size class each layer is cut into, below nought for a layer nobody uses; and which of its squares are taken, a bit each.
	private readonly int[] layerClass = [-1, -1, -1, -1, -1, -1, -1, -1];
	private readonly ulong[] layerCells = new ulong[MostLayers];

	//One lamp that casts: its size class and the first of its squares - the squares
	//of a class counted layer by layer - what it was baked as, and each face's state.
	private sealed class Lamp
	{
		public Light Light;
		public int First, Faces, Class;
		public bool Spot;

		/// <summary>Texels a face of it is across.</summary>
		public int Size => SizeOf( Class );

		/// <summary>The squares it had before its size changed, read until the new ones are all baked - below nought where there are none: a lamp baked again at its new size lit without a shadow for the frames that took, a blink.</summary>
		public int OldFirst = -1, OldClass = -1;
		public Vector3 Position, Forward;
		public float Range, Outer;
		public long Seen;

		/// <summary>Which faces' still world in the map is as the world stands.</summary>
		public readonly bool[] Baked = new bool[6];

		/// <summary>Where in each face's square of the movers' map something was last drawn - what the next draw there clears.</summary>
		public readonly Area[] Moved = new Area[6];

		/// <summary>Which faces have what moves in them drawn this frame, a bit each - the record's shape.y.</summary>
		public int Moving;

		/// <summary>Whether it moved this frame: its faces are drawn whole every frame instead of baked - see PlanTravelling.</summary>
		public bool Travelling;

		/// <summary>Faces with a valid picture from the current pose, still read while being rebaked</summary>
		public readonly bool[] Held = new bool[6];

		/// <summary>Which faces the camera can see into this frame - see Meets.</summary>
		public readonly bool[] Visible = new bool[6];
		public readonly System.Numerics.Matrix4x4[] Clip = new System.Numerics.Matrix4x4[6];
		public readonly System.Numerics.Vector4[] Planes = new System.Numerics.Vector4[6 * 6];

		/// <summary>The finest triangles of everything still within reach - what a face of it can hold at the most; below nought when it wants counting again.</summary>
		public long Triangles = -1;

		/// <summary>Whether every face the camera can see into holds a picture - see <see cref="Held"/>.</summary>
		public bool Whole()
		{
			for ( var f = 0; f < Faces; f++ )
				if ( Visible[f] && !Held[f] ) return false;

			return true;
		}

		public ReadOnlySpan<System.Numerics.Vector4> PlanesOf( int face ) => Planes.AsSpan( face * 6, 6 );
	}

	private readonly Dictionary<Light, Lamp> lamps = new();
	private readonly List<Lamp> gone = new();
	private long frameNumber;
	private bool warnedFull;

	//Whether a lamp found no room for the size it wanted last frame - see the remarks on the class.
	private bool crowded;
	private bool crowding;

	//A rectangle of a face's square, in its texels from the square's corner; nothing where it is empty.
	private readonly record struct Area( int X, int Y, int Width, int Height )
	{
		public bool Empty => Width <= 0 || Height <= 0;

		public static Area Whole( int size ) => new( 0, 0, size, size );

		public Area With( Area other )
		{
			if ( Empty ) return other;
			if ( other.Empty ) return this;

			var left = Math.Min( X, other.X );
			var top = Math.Min( Y, other.Y );

			return new Area( left, top, Math.Max( X + Width, other.X + other.Width ) - left, Math.Max( Y + Height, other.Y + other.Height ) - top );
		}
	}

	//What this frame does to one face: bake its still world into the map (a view), and draw what moves in it into the movers' map (a view).
	private readonly struct Face
	{
		public readonly Lamp Lamp;
		public readonly int Index;

		/// <summary>The view its still world is baked from, the one what moves in it is drawn from - below nought for neither.</summary>
		public readonly int BakeView, MoverView;

		/// <summary>What of its square in the movers' map is cleared, and where what moves is drawn.</summary>
		public readonly Area Clear, Movers;

		/// <summary>The face's state before this frame - what it holds again if the bake does not happen.</summary>
		public readonly bool WasHeld;

		public Face( Lamp lamp, int index, int bakeView, int moverView, Area clear, Area movers )
		{
			Lamp = lamp; Index = index; BakeView = bakeView; MoverView = moverView; Clear = clear; Movers = movers;
			WasHeld = lamp.Held[index];
		}
	}

	private readonly List<Face> faces = new();
	private readonly CoreMeshRender.ViewSetup[] planned = new CoreMeshRender.ViewSetup[MostFaces];
	private int viewCount;

	//Faces drawn since the process started, for the report - a bake or what moves, each one.
	internal static long FacesDrawn;

	//How many views this frame adds to the cull.
	internal int ViewCount => viewCount;

	//How many triangles this frame's faces can hold between them - see CoreMeshRender.ShadowTriangles.
	internal long Triangles { get; private set; }

	internal CoreLampMaps( CoreCasters casters )
	{
		this.casters = casters;
	}

	//Decides this frame's faces and says in each lamp's record which layer its faces start at - nought or more where every face
	//is baked, below nought where it has none. Before the lights and the frame block go up; the casters are the last frame's.
	internal void Plan( float[] frame, float[] records, IReadOnlyList<Light> lights, int count, bool casts, float lodPixels, int room,
		Vector3 eye, float pixelsAtAMetre, ReadOnlySpan<System.Numerics.Vector4> seen, in System.Numerics.Matrix4x4 clipToWorld )
	{
		faces.Clear();
		viewCount = 0;
		Triangles = 0;

		foreach ( var lamp in lamps.Values ) lamp.Moving = 0;
		frameNumber++;
		Frustum( seen, clipToWorld );
		crowded = crowding;
		crowding = false;

		count = Math.Min( count, Math.Min( lights.Count, records.Length / CoreLights.LightFloats ) );

		//A whole look: every face baked again, each read as it stands meanwhile.
		if ( casters.Whole )
		{
			foreach ( var lamp in lamps.Values )
			{
				Array.Clear( lamp.Baked );
				lamp.Triangles = -1;
			}
		}
		else Stirred();

		for ( var i = 0; i < count; i++ )
		{
			var at = i * CoreLights.LightFloats;
			records[at + 14] = -1.0f;
			records[at + 15] = 0.0f;
			records[at + 17] = 0.0f;

			if ( !casts ) continue;

			var kind = records[at + 7];
			var light = lights[i];

			if ( kind < 0.5f || light is null || !light.Shadows ) continue;

			//A lamp whose shadow of the still world is in the bake's mask has no still world to draw - see Light.Baked.
			if ( CoreSceneRenderer.MaskChannelOf( kind ) >= 0 ) continue;

			var lamp = Keep( light, kind > 1.5f, records, at, eye, pixelsAtAMetre );
			if ( lamp is null ) continue;

			lamp.Seen = frameNumber;

			for ( var f = 0; f < lamp.Faces; f++ ) lamp.Visible[f] = Meets( lamp, f );
		}

		//What left the frame gives its layers back.
		gone.Clear();

		foreach ( var lamp in lamps.Values )
			if ( lamp.Seen != frameNumber ) gone.Add( lamp );

		foreach ( var lamp in gone )
		{
			Release( lamp );
			lamps.Remove( lamp.Light );
		}

		//The faces to draw, nearest lamp first: what this frame cannot do without, then bakes that can wait for room.
		room = Math.Min( room, MostFaces );

		for ( var pass = 0; pass < 2; pass++ )
		{
			for ( var i = 0; i < count && viewCount < room; i++ )
			{
				//A light that is gone leaves its place in the list: asking the lamps for it is asking a dictionary for
				//a null key, which throws - and in the middle of a frame nobody was catching for, that was the editor.
				if ( lights[i] is not { } asking ) continue;

				if ( !lamps.TryGetValue( asking, out var lamp ) || lamp.Seen != frameNumber ) continue;

				if ( pass == 0 && lamp.Travelling ) PlanTravelling( lamp, room, lodPixels );
				else if ( !lamp.Travelling ) PlanStill( lamp, room, lodPixels, bakes: pass == 1 );
			}
		}

		//And where each lamp's faces are, where every one is baked: the first of its squares, and their size class - or the old ones, until the new are.
		for ( var i = 0; i < count; i++ )
		{
			if ( lights[i] is not { } asking ) continue;
			if ( !lamps.TryGetValue( asking, out var lamp ) || lamp.Seen != frameNumber ) continue;

			if ( lamp.Whole() && lamp.OldClass >= 0 )
			{
				Free( lamp.OldClass, lamp.OldFirst, lamp.Faces );
				lamp.OldClass = -1;
			}

			var at = i * CoreLights.LightFloats;

			if ( lamp.Whole() )
			{
				records[at + 14] = lamp.First;
				records[at + 15] = lamp.Class;

				//Which faces read the movers' map as well - only where the squares read are the ones what moves was drawn into.
				records[at + 17] = lamp.Moving;
			}
			else if ( lamp.OldClass >= 0 )
			{
				records[at + 14] = lamp.OldFirst;
				records[at + 15] = lamp.OldClass;
			}
		}

		CoreSceneRenderer.SetLampMaps( frame, CoreRenderDevice.ShadowMapIndex( map ), CoreRenderDevice.ShadowMapIndex( map, depth: true ),
			CoreRenderDevice.ShadowMapIndex( movers ), CoreRenderDevice.ShadowMapIndex( movers, depth: true ) );
	}

	//A lamp that moved this frame: every face it can be seen through drawn whole, what stands still and what moves in one view,
	//or none - part of them is no shadow anyway. Left unbaked, so the still world is baked once it stops.
	private void PlanTravelling( Lamp lamp, int room, float lodPixels )
	{
		var wanted = 0;
		for ( var f = 0; f < lamp.Faces; f++ ) if ( lamp.Visible[f] ) wanted++;

		if ( viewCount + wanted > room ) return;

		for ( var f = 0; f < lamp.Faces; f++ )
		{
			if ( !lamp.Visible[f] ) continue;

			faces.Add( new Face( lamp, f, View( lamp, f, Everything, lodPixels ), -1, default, default ) );
			lamp.Held[f] = true;
		}
	}

	//A lamp standing still: first what moves in faces already baked, each frame; then, with bakes, the faces not baked.
	private void PlanStill( Lamp lamp, int room, float lodPixels, bool bakes )
	{
		//Out of sight faces are left as they are: no pixel reads them.
		for ( var f = 0; f < lamp.Faces && viewCount < room; f++ )
		{
			if ( !lamp.Visible[f] || lamp.Baked[f] != !bakes ) continue;

			var moving = Moving( lamp, f );
			if ( !moving && !bakes ) continue;

			var wanted = ( bakes ? 1 : 0 ) + ( moving ? 1 : 0 );
			if ( viewCount + wanted > room ) return;

			//What moves, where it lands in the square - and what was drawn there before cleared away with it.
			var movers = moving ? MoversIn( lamp, f ) : default;
			var clear = movers.With( lamp.Moved[f] );

			var bakeView = bakes ? View( lamp, f, StillOnly, lodPixels ) : -1;
			var moverView = moving && !movers.Empty ? View( lamp, f, MovingOnly, lodPixels ) : -1;

			faces.Add( new Face( lamp, f, bakeView, moverView, clear, movers ) );

			lamp.Baked[f] = true;
			lamp.Held[f] = true;

			if ( moverView >= 0 )
			{
				lamp.Moving |= 1 << f;
				lamp.Moved[f] = movers;
			}
		}
	}

	//A light's lamp: made where it is new, given its squares at the size it wants and marked to bake where it changed or its size did.
	private Lamp Keep( Light light, bool spot, float[] records, int at, Vector3 eye, float pixelsAtAMetre )
	{
		var position = new Vector3( records[at + 0], records[at + 1], records[at + 2] );
		var range = MathF.Max( records[at + 3], Near + 0.01f );
		var forward = new Vector3( records[at + 8], records[at + 9], records[at + 10] );
		var outer = records[at + 12];
		var wanted = spot ? 1 : 6;
		var sizeClass = ClassFor( position, range, eye, pixelsAtAMetre );

		if ( lamps.TryGetValue( light, out var lamp ) && lamp.Faces != wanted )
		{
			Release( lamp );
			lamps.Remove( light );
			lamp = null;
		}

		if ( lamp is null )
		{
			lamp = new Lamp { Light = light, Faces = wanted, Spot = spot, Class = -1 };

			if ( !Place( lamp, sizeClass, coarser: true ) || lamp.Class != sizeClass ) crowding = true;

			if ( lamp.Class < 0 )
			{
				if ( !warnedFull )
				{
					warnedFull = true;
					Log.Warning( $"the lamps' shadow maps are full at {MostLayers} layers - the furthest lamps light without a shadow" );
				}

				return null;
			}

			lamps[light] = lamp;
			Aim( lamp, position, forward, range, outer );
			return lamp;
		}

		//Finer the moment it should be; coarser only two sizes on - see the remarks on the class. Its old squares
		//stay its own and are read until the new ones are baked; where there is no room for both, it stays.
		if ( lamp.OldClass < 0 && ( sizeClass < lamp.Class || sizeClass >= lamp.Class + ( crowded ? 1 : 2 ) ) )
		{
			var was = lamp.Class;
			var first = lamp.First;

			//Only the size it wants: a lamp that got a coarser one for want of room asked for the finer one again every frame, and was baked every frame.
			if ( Place( lamp, sizeClass, coarser: sizeClass > was ) )
			{
				if ( lamp.Whole() ) { lamp.OldFirst = first; lamp.OldClass = was; }
				else Free( was, first, lamp.Faces );

				Aim( lamp, position, forward, range, outer );
			}
			else
			{
				lamp.Class = was;
				lamp.First = first;
				crowding = true;
			}
		}

		var moved = ( position - lamp.Position ).Length > 1e-4f || MathF.Abs( range - lamp.Range ) > 1e-4f ||
			( spot && ( ( forward - lamp.Forward ).Length > 1e-5f || MathF.Abs( outer - lamp.Outer ) > 1e-5f ) );

		if ( moved ) Aim( lamp, position, forward, range, outer );

		lamp.Travelling = moved;
		return lamp;
	}

	//Size class from the lamp's on-screen reach, finest when the camera is inside, coarsest if off-screen
	private int ClassFor( Vector3 position, float range, Vector3 eye, float pixelsAtAMetre )
	{
		if ( hasFrustum && !CoreSceneRenderer.SphereVisible( frustum, position, range ) ) return Sizes - 1;

		//A camera with no perspective has no size for it on the screen.
		if ( pixelsAtAMetre <= 0.0f ) return 1;

		var away = ( eye - position ).Length;
		if ( away <= range ) return 0;

		var pixels = range * pixelsAtAMetre / away;

		for ( var sizeClass = Sizes - 1; sizeClass > 0; sizeClass-- )
			if ( SizeOf( sizeClass ) >= pixels ) return sizeClass;

		return 0;
	}

	//Finds a run of squares for a lamp's faces at this size (or coarser if allowed); false if no room
	private bool Place( Lamp lamp, int sizeClass, bool coarser )
	{
		for ( var c = sizeClass; c < ( coarser ? Sizes : sizeClass + 1 ); c++ )
		{
			var first = Room( c, lamp.Faces );
			if ( first < 0 ) continue;

			Claim( lamp, c, first );

			//A map with the layers the squares are in - made again bigger where it is short, which forgets every face.
			var last = ( first + lamp.Faces - 1 ) / CellsOf( c );
			if ( !EnsureMap( last + 1 ) )
			{
				Free( c, first, lamp.Faces );
				lamp.Class = -1;
				return false;
			}

			return true;
		}

		return false;
	}

	//The first run of free squares of a class this long; below nought where there is none.
	private int Room( int sizeClass, int wanted )
	{
		var cells = CellsOf( sizeClass );

		for ( var first = 0; first + wanted <= MostLayers * cells; first++ )
		{
			var free = true;

			for ( var f = 0; f < wanted && free; f++ )
			{
				var square = first + f;
				var layer = square / cells;

				if ( layerClass[layer] >= 0 && layerClass[layer] != sizeClass ) free = false;
				else if ( ( layerCells[layer] & ( 1ul << ( square % cells ) ) ) != 0 ) free = false;
			}

			if ( free ) return first;
		}

		return -1;
	}

	//A lamp's squares taken: these, at this class.
	private void Claim( Lamp lamp, int sizeClass, int first )
	{
		var cells = CellsOf( sizeClass );

		for ( var f = 0; f < lamp.Faces; f++ )
		{
			var square = first + f;
			layerClass[square / cells] = sizeClass;
			layerCells[square / cells] |= 1ul << ( square % cells );
		}

		lamp.Class = sizeClass;
		lamp.First = first;

		//New squares hold another lamp's movers or a new map's garbage: the first movers' draw clears all of them
		Array.Fill( lamp.Moved, Area.Whole( SizeOf( sizeClass ) ) );
	}

	//A lamp's squares given back, old and new.
	private void Release( Lamp lamp )
	{
		if ( lamp.Class >= 0 ) Free( lamp.Class, lamp.First, lamp.Faces );
		if ( lamp.OldClass >= 0 ) Free( lamp.OldClass, lamp.OldFirst, lamp.Faces );

		lamp.Class = -1;
		lamp.OldClass = -1;
	}

	//A run of squares given back - and a layer nobody has a square of left uncut.
	private void Free( int sizeClass, int first, int faces )
	{
		var cells = CellsOf( sizeClass );

		for ( var f = 0; f < faces; f++ )
		{
			var square = first + f;
			layerCells[square / cells] &= ~( 1ul << ( square % cells ) );
			if ( layerCells[square / cells] == 0 ) layerClass[square / cells] = -1;
		}
	}

	//Where a face of a lamp is in the map: its layer and its square's corner, in texels.
	private static (int Layer, int X, int Y) WhereIs( Lamp lamp, int face )
	{
		var cells = CellsOf( lamp.Class );
		var square = lamp.First + face;
		var within = square % cells;
		var perRow = 1 << lamp.Class;

		return ( square / cells, ( within % perRow ) * lamp.Size, ( within / perRow ) * lamp.Size );
	}

	//A map with at least this many layers - made again bigger where it is short, which forgets every face.
	private bool EnsureMap( int needed )
	{
		if ( map != 0 && layers >= needed ) return true;

		var want = Math.Min( MostLayers, Math.Max( needed, layers + 2 ) );

		CoreRenderDevice.DestroyShadowMap( map );
		CoreRenderDevice.DestroyShadowMap( movers );

		//Read as depth too: a lamp's edge is softened by what stands in front of a point - see LampShadow.
		map = CoreRenderDevice.CreateShadowMap( Side, want, readDepth: true );

		//And what moves, square for square, read the same way.
		movers = map != 0 ? CoreRenderDevice.CreateShadowMap( Side, want, readDepth: true ) : 0;

		if ( movers == 0 )
		{
			CoreRenderDevice.DestroyShadowMap( map );
			map = 0;
		}

		layers = map != 0 ? want : 0;

		//Every face and every old square with it: the map they were in is gone, and the new one holds garbage.
		foreach ( var lamp in lamps.Values )
		{
			Forget( lamp );
			Array.Fill( lamp.Moved, Area.Whole( lamp.Size ) );

			if ( lamp.OldClass >= 0 ) Free( lamp.OldClass, lamp.OldFirst, lamp.Faces );
			lamp.OldClass = -1;
		}

		return map != 0;
	}

	private static void Forget( Lamp lamp )
	{
		Array.Clear( lamp.Baked );
		Array.Clear( lamp.Held );
		lamp.Triangles = -1;
	}

	//A lamp where it now stands: each face's projection and planes again, and every face to bake.
	private void Aim( Lamp lamp, Vector3 position, Vector3 forward, float range, float outer )
	{
		lamp.Position = position;
		lamp.Forward = forward;
		lamp.Range = range;
		lamp.Outer = outer;

		for ( var f = 0; f < lamp.Faces; f++ )
		{
			lamp.Clip[f] = FaceClip( lamp, f );
			CoreSceneRenderer.FrustumPlanes( lamp.Clip[f], lamp.Planes.AsSpan( f * 6, 6 ) );
		}

		Forget( lamp );
	}

	//Which way a face looks: down the world's axes for a point light, in the order LampShadow picks them, and down the cone for a spot.
	private static Vector3 FaceForward( Lamp lamp, int face ) => lamp.Spot ? lamp.Forward : face switch
	{
		0 => new Vector3( 1.0f, 0.0f, 0.0f ),
		1 => new Vector3( -1.0f, 0.0f, 0.0f ),
		2 => new Vector3( 0.0f, 1.0f, 0.0f ),
		3 => new Vector3( 0.0f, -1.0f, 0.0f ),
		4 => new Vector3( 0.0f, 0.0f, 1.0f ),
		_ => new Vector3( 0.0f, 0.0f, -1.0f ),
	};

	//How far across a face is at a metre out: one for ninety degrees, the tangent of the cone's half-angle for a spot.
	private static float HalfTan( Lamp lamp )
	{
		if ( !lamp.Spot ) return 1.0f;

		var widest = MathF.Cos( WidestSpotDegrees * ( MathF.PI / 180.0f ) );
		var outer = Math.Clamp( lamp.Outer, widest, 0.9998f );
		return MathF.Sqrt( 1.0f - ( outer * outer ) ) / outer;
	}

	//A face's projection: across and up the face's square over its half-width, less the border, and depth from Near to the lamp's
	//reach - written out rather than made of a look-at and a perspective so that LampShadow can do the same sums the other way.
	private static System.Numerics.Matrix4x4 FaceClip( Lamp lamp, int face )
	{
		var ( right, up, forward ) = CoreSunPages.LightAxes( FaceForward( lamp, face ) );
		var p = lamp.Position;

		var s = FitOf( lamp.Size ) / HalfTan( lamp );
		var far = lamp.Range;
		var a = far / ( far - Near );
		var b = Near * far / ( far - Near );

		//Row-vector: clip = (across·s, up·s, a·along - b, along), along the distance down the face's axis.
		return new System.Numerics.Matrix4x4(
			right.x * s, up.x * s, forward.x * a, forward.x,
			right.y * s, up.y * s, forward.y * a, forward.y,
			right.z * s, up.z * s, forward.z * a, forward.z,
			-p.Dot( right ) * s, -p.Dot( up ) * s, ( -p.Dot( forward ) * a ) - b, -p.Dot( forward ) );
	}

	//The camera's frustum this frame, as planes and as corners - see Meets.
	private readonly System.Numerics.Vector4[] frustum = new System.Numerics.Vector4[6];
	private readonly Vector3[] corners = new Vector3[8];
	private bool hasFrustum;

	private void Frustum( ReadOnlySpan<System.Numerics.Vector4> seen, in System.Numerics.Matrix4x4 clipToWorld )
	{
		hasFrustum = seen.Length == 6;
		if ( !hasFrustum ) return;

		seen.CopyTo( frustum );

		for ( var c = 0; c < 8; c++ )
		{
			var clip = new System.Numerics.Vector4( ( c & 1 ) != 0 ? 1.0f : -1.0f, ( c & 2 ) != 0 ? 1.0f : -1.0f, ( c & 4 ) != 0 ? 1.0f : 0.0f, 1.0f );
			var world = System.Numerics.Vector4.Transform( clip, clipToWorld );
			corners[c] = new Vector3( world.X, world.Y, world.Z ) / ( MathF.Abs( world.W ) > 1e-8f ? world.W : 1e-8f );
		}
	}

	//Whether a face's pyramid and the camera's frustum can meet - not where all of one lies outside a plane of
	//the other. Where they cannot, no pixel on the screen reads the face. True where there is no frustum to ask.
	private bool Meets( Lamp lamp, int face )
	{
		if ( !hasFrustum ) return true;

		var ( right, up, forward ) = CoreSunPages.LightAxes( FaceForward( lamp, face ) );
		var across = lamp.Range * HalfTan( lamp );
		var far = lamp.Position + ( forward * lamp.Range );

		Span<Vector3> pyramid = stackalloc Vector3[5];
		pyramid[0] = lamp.Position;
		pyramid[1] = far + ( right * across ) + ( up * across );
		pyramid[2] = far + ( right * across ) - ( up * across );
		pyramid[3] = far - ( right * across ) + ( up * across );
		pyramid[4] = far - ( right * across ) - ( up * across );

		foreach ( var plane in frustum )
			if ( Outside( plane, pyramid ) ) return false;

		foreach ( var plane in lamp.PlanesOf( face ) )
			if ( Outside( plane, corners ) ) return false;

		return true;
	}

	private static bool Outside( System.Numerics.Vector4 plane, ReadOnlySpan<Vector3> points )
	{
		foreach ( var p in points )
			if ( ( plane.X * p.x ) + ( plane.Y * p.y ) + ( plane.Z * p.z ) + plane.W >= 0.0f ) return false;

		return true;
	}

	//Whether anything that moves stands in a face's frustum now - and it was, for as long as it does.
	private bool Moving( Lamp lamp, int face )
	{
		var planes = lamp.PlanesOf( face );

		foreach ( var slot in casters.Movers )
		{
			var sphere = casters.SphereOf( slot );
			if ( sphere.W < 0.0f ) continue;
			if ( CoreSceneRenderer.SphereVisible( planes, new Vector3( sphere.X, sphere.Y, sphere.Z ), sphere.W ) ) return true;
		}

		return false;
	}

	//The still world's changes since the last plan, as faces to bake again.
	private void Stirred()
	{
		foreach ( var change in casters.Changes )
		{
			foreach ( var lamp in lamps.Values )
			{
				Touch( lamp, change.Was );
				Touch( lamp, change.Now );
			}
		}
	}

	private static void Touch( Lamp lamp, System.Numerics.Vector4 sphere )
	{
		if ( sphere.W < 0.0f ) return;

		var centre = new Vector3( sphere.X, sphere.Y, sphere.Z );
		if ( ( centre - lamp.Position ).Length - sphere.W > lamp.Range ) return;

		lamp.Triangles = -1;

		for ( var f = 0; f < lamp.Faces; f++ )
			if ( CoreSceneRenderer.SphereVisible( lamp.PlanesOf( f ), centre, sphere.W ) ) lamp.Baked[f] = false;
	}

	//Where in a face's square everything moving in it lands - its bounding spheres through the face's projection,
	//widened by the border the filter reads past an edge - or all of it where one reaches behind the lamp.
	private Area MoversIn( Lamp lamp, int face )
	{
		var size = lamp.Size;
		var planes = lamp.PlanesOf( face );
		var clip = lamp.Clip[face];

		var left = float.MaxValue;
		var top = float.MaxValue;
		var right = float.MinValue;
		var bottom = float.MinValue;

		foreach ( var slot in casters.Movers )
		{
			var sphere = casters.SphereOf( slot );
			if ( sphere.W < 0.0f ) continue;

			var centre = new Vector3( sphere.X, sphere.Y, sphere.Z );
			if ( !CoreSceneRenderer.SphereVisible( planes, centre, sphere.W ) ) continue;

			for ( var corner = 0; corner < 8; corner++ )
			{
				var point = new System.Numerics.Vector4(
					sphere.X + ( ( corner & 1 ) != 0 ? sphere.W : -sphere.W ),
					sphere.Y + ( ( corner & 2 ) != 0 ? sphere.W : -sphere.W ),
					sphere.Z + ( ( corner & 4 ) != 0 ? sphere.W : -sphere.W ), 1.0f );

				var at = System.Numerics.Vector4.Transform( point, clip );

				//Reaching round behind the lamp: no rectangle bounds it.
				if ( at.W <= Near * 0.5f ) return Area.Whole( size );

				//Texels the way LampShadow reads them: the square's corner plus half of clip space across it, no flip.
				var x = ( ( at.X / at.W ) * 0.5f + 0.5f ) * size;
				var y = ( ( at.Y / at.W ) * 0.5f + 0.5f ) * size;

				left = MathF.Min( left, x );
				top = MathF.Min( top, y );
				right = MathF.Max( right, x );
				bottom = MathF.Max( bottom, y );
			}
		}

		if ( right < left ) return default;

		//The filter's taps reach this far past what casts.
		const int border = ( Guard * 2 ) + 2;

		var x0 = Math.Clamp( (int)MathF.Floor( left ) - border, 0, size );
		var y0 = Math.Clamp( (int)MathF.Floor( top ) - border, 0, size );
		var x1 = Math.Clamp( (int)MathF.Ceiling( right ) + border, x0, size );
		var y1 = Math.Clamp( (int)MathF.Ceiling( bottom ) + border, y0, size );

		return new Area( x0, y0, x1 - x0, y1 - y0 );
	}

	//ViewCasters in the cull: everything, what stands still, what moves.
	private const int Everything = 0, StillOnly = 1, MovingOnly = 2;

	//A view of one face for this frame's cull: of the still world, to bake, of what moves, for the movers' map, or of both.
	private int View( Lamp lamp, int face, int casters, float lodPixels )
	{
		if ( lamp.Triangles < 0 ) lamp.Triangles = TrianglesNear( lamp );

		Triangles += ( casters == MovingOnly ? 0 : lamp.Triangles ) + ( casters == StillOnly ? 0 : this.casters.MoverTriangles );

		var view = new CoreMeshRender.ViewSetup
		{
			WorldToClip = lamp.Clip[face],
			Camera = false,
			Perspective = true,
			Casters = casters,
			Eye = lamp.Position,
			ErrorScale = FitOf( lamp.Size ) / HalfTan( lamp ) * lamp.Size * 0.5f,
			LodPixels = lodPixels,
			Near = Near,
			Far = lamp.Range,
		};

		view.SetPlanes( lamp.PlanesOf( face ) );
		planned[viewCount] = view;

		return viewCount++;
	}

	//The finest levels of everything still within a lamp's reach - the most a face of it can hold.
	private long TrianglesNear( Lamp lamp )
	{
		var sum = 0L;

		for ( var slot = 0; slot < casters.Count; slot++ )
		{
			if ( !casters.Still( slot ) ) continue;

			var sphere = casters.SphereOf( slot );
			if ( ( new Vector3( sphere.X, sphere.Y, sphere.Z ) - lamp.Position ).Length - sphere.W > lamp.Range ) continue;

			sum += casters.TrianglesOf( slot );
		}

		return sum;
	}

	//This frame's faces, after the views already in the cull's list; how many there are now.
	internal int AddViews( CoreMeshRender.ViewSetup[] into, int count )
	{
		for ( var i = 0; i < viewCount && count < into.Length; i++ )
			into[count++] = planned[i];

		return count;
	}

	//Draws this frame's faces from the lists the cull made for the views this added - after the bin pass has
	//filled them, before the materials read the map. With no lists, the faces planned are given back unbaked.
	internal void Draw( CoreMeshRender mesh, int firstView, CoreMeshRender.LooseView loose )
	{
		if ( faces.Count == 0 ) return;

		var clusters = mesh is not null && mesh.Ready;

		if ( !clusters && loose is null )
		{
			foreach ( var face in faces )
			{
				Unbake( face );
				if ( face.MoverView >= 0 ) face.Lamp.Moved[face.Index] = Area.Whole( face.Lamp.Size );
			}

			foreach ( var lamp in lamps.Values ) lamp.Moving = 0;
			return;
		}

		CoreRenderDevice.ZoneBegin( "lamp maps" );

		foreach ( var face in faces )
		{
			var ( layer, x, y ) = WhereIs( face.Lamp, face.Index );
			var size = face.Lamp.Size;

			//The still world into the map, where it is kept.
			if ( face.BakeView >= 0 )
			{
				CoreRenderDevice.ZoneBegin( "lamp bake" );
				var baked = Into( map, layer, x, y, size, Area.Whole( size ), mesh, clusters, firstView, face.BakeView, loose, Area.Whole( size ) );
				CoreRenderDevice.ZoneEnd();

				if ( !baked ) Unbake( face );
			}

			//What moves into the movers' map: where it was and is cleared, drawn where it is.
			if ( face.MoverView >= 0 )
			{
				CoreRenderDevice.ZoneBegin( "lamp movers" );
				var drawn = Into( movers, layer, x, y, size, face.Clear, mesh, clusters, firstView, face.MoverView, loose, face.Movers );
				CoreRenderDevice.ZoneEnd();

				//Not drawn: what is there is stale, so the next draw clears all of it.
				if ( !drawn ) face.Lamp.Moved[face.Index] = Area.Whole( size );
			}
		}

		CoreRenderDevice.ZoneEnd();
	}

	//One view's casters into a face's square of a map: this part of it cleared, and the draws landing only in that part.
	private bool Into( ulong target, int layer, int x, int y, int size, Area clear, CoreMeshRender mesh, bool clusters,
		int firstView, int view, CoreMeshRender.LooseView loose, Area only )
	{
		if ( !CoreRenderDevice.BeginShadow( target, layer ) ) return false;

		//The whole face's projection.
		CoreRenderDevice.ShadowArea( x, y, size, size, clear: false );

		if ( !clear.Empty ) CoreRenderDevice.ShadowScissor( x + clear.X, y + clear.Y, clear.Width, clear.Height, clear: true );
		CoreRenderDevice.ShadowScissor( x + only.X, y + only.Y, only.Width, only.Height );

		if ( clusters ) mesh.DrawView( firstView + view );
		loose?.Invoke( in planned[view] );

		CoreRenderDevice.EndTarget();

		FacesDrawn++;

		return true;
	}

	//A face whose bake did not happen: baked again next frame, and read as it was meanwhile.
	private static void Unbake( in Face face )
	{
		face.Lamp.Baked[face.Index] = false;
		face.Lamp.Held[face.Index] = face.WasHeld;
	}

	public void Dispose()
	{
		CoreRenderDevice.DestroyShadowMap( map );
		CoreRenderDevice.DestroyShadowMap( movers );
		map = 0;
		movers = 0;
		layers = 0;
		lamps.Clear();
		Array.Fill( layerClass, -1 );
		Array.Clear( layerCells );
	}
}
