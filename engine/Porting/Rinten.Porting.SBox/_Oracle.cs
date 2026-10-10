using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Rinten.Porting.SBox;

/// <summary>Scratch: reads their compiled clips back to check our ported clips against them</summary>
public static class Oracle
{
	/// <summary>Which way their model faces, out of their own compiled skeleton.</summary>
	public static void Facing( string compiledModel )
	{
		var animations = CoreImportedAnimations.Open( File.ReadAllBytes( compiledModel ), named =>
		{
			var beside = Path.Combine( Path.GetDirectoryName( compiledModel ) ?? "", Path.GetFileName( named ) );
			return File.Exists( beside ) ? File.ReadAllBytes( beside ) : null;
		} );

		var bones = animations.Skeleton;

		var place = new Vector3[bones.Count];
		var turn = new Rotation[bones.Count];

		for ( var i = 0; i < bones.Count; i++ )
		{
			if ( bones[i].Parent < 0 || bones[i].Parent >= i )
			{
				place[i] = bones[i].Position;
				turn[i] = bones[i].Rotation;
				continue;
			}

			var parent = bones[i].Parent;

			place[i] = place[parent] + ( turn[parent] * bones[i].Position );
			turn[i] = turn[parent] * bones[i].Rotation;
		}

		int Named( string name )
		{
			for ( var i = 0; i < bones.Count; i++ )
				if ( string.Equals( bones[i].Name, name, StringComparison.OrdinalIgnoreCase ) ) return i;

			return -1;
		}

		void Say( string what, string from, string to )
		{
			var a = Named( from );
			var b = Named( to );

			if ( a < 0 || b < 0 ) { Console.WriteLine( $"   {what}: no {from}/{to}" ); return; }

			var theirs = ( place[b] - place[a] ).Normal;
			var ours = ForeignBasis.Quake.Vector( theirs ).Normal;

			Console.WriteLine( $"   {what}: in their axes {theirs}, on ours {ours}" );
		}

		Console.WriteLine( $"their rest skeleton, {bones.Count} bone(s):" );

		Say( "ankle -> ball ", "ankle_L", "ball_L" );
		Say( "pelvis -> head", "pelvis", "head" );
	}

	/// <summary>How fast each clip of theirs actually travels, out of their own compiled model - against which a blend space's authored numbers can be checked.</summary>
	public static void Speeds( string compiledModel, params string[] wanted )
	{
		var animations = CoreImportedAnimations.Open( File.ReadAllBytes( compiledModel ), named =>
		{
			var beside = Path.Combine( Path.GetDirectoryName( compiledModel ) ?? "", Path.GetFileName( named ) );
			return File.Exists( beside ) ? File.ReadAllBytes( beside ) : null;
		} );

		Console.WriteLine( "clip                                   travels (forward, right) m/s   speed" );

		for ( var at = 0; at < animations.Clips.Count; at++ )
		{
			var name = animations.Clips[at];

			if ( wanted.Length > 0 && !wanted.Any( w => name.StartsWith( w, StringComparison.OrdinalIgnoreCase ) ) )
				continue;

			var clip = animations.Read( at );
			if ( clip is null || clip.Speed < 0.01f ) continue;

			Console.WriteLine( $"  {name,-38} ({clip.GroundVelocity.x,6:0.00}, {clip.GroundVelocity.y,6:0.00})" +
				$"   {clip.Speed,6:0.00}" );
		}
	}

	/// <summary>How far one bone of one of their clips travels over the clip.</summary>
	public static void Travel( string compiledModel, string wanted, params string[] bones )
	{
		var animations = CoreImportedAnimations.Open( File.ReadAllBytes( compiledModel ), named =>
		{
			var beside = Path.Combine( Path.GetDirectoryName( compiledModel ) ?? "", Path.GetFileName( named ) );
			return File.Exists( beside ) ? File.ReadAllBytes( beside ) : null;
		} );

		var at = -1;
		for ( var i = 0; i < animations.Clips.Count; i++ )
			if ( string.Equals( animations.Clips[i], wanted, StringComparison.OrdinalIgnoreCase ) ) { at = i; break; }

		if ( at < 0 ) { Console.WriteLine( "they have no " + wanted ); return; }

		var clip = animations.Read( at );

		Console.WriteLine( $"{wanted}: {clip.FrameCount} frames, {clip.BoneCount} bones" );

		foreach ( var named in bones )
		{
			var bone = -1;
			for ( var i = 0; i < animations.Skeleton.Count; i++ )
				if ( string.Equals( animations.Skeleton[i].Name, named, StringComparison.OrdinalIgnoreCase ) ) { bone = i; break; }

			if ( bone < 0 || bone >= clip.BoneCount ) { Console.WriteLine( $"   no {named}" ); continue; }

			var low = new Vector3( 1e9f, 1e9f, 1e9f );
			var high = new Vector3( -1e9f, -1e9f, -1e9f );

			for ( var frame = 0; frame < clip.FrameCount; frame++ )
			{
				var p = clip.Positions[( frame * clip.BoneCount ) + bone];

				low = new Vector3( MathF.Min( low.x, p.x ), MathF.Min( low.y, p.y ), MathF.Min( low.z, p.z ) );
				high = new Vector3( MathF.Max( high.x, p.x ), MathF.Max( high.y, p.y ), MathF.Max( high.z, p.z ) );
			}

			Console.WriteLine( $"   {named,-18} travels ({high.x - low.x:0.000}, {high.y - low.y:0.000}, {high.z - low.z:0.000}) m" +
				$"   moves={clip.Moves[bone]}" );
		}
	}

	/// <summary>What the axis read makes of one file.</summary>
	public static void Axes( string source )
	{
		Console.WriteLine( $"{Path.GetFileName( source )}: turn {SBoxFbxAxes.Turn( source )}  {SBoxFbxAxes.Said( source )}" );
	}

	/// <summary>One clip, bone by bone, both sides side by side.</summary>
	public static void Dump( string compiledModel, string ourFolder, string wanted, params string[] bones )
	{
		var animations = CoreImportedAnimations.Open( File.ReadAllBytes( compiledModel ), named =>
		{
			var beside = Path.Combine( Path.GetDirectoryName( compiledModel ) ?? "", Path.GetFileName( named ) );
			return File.Exists( beside ) ? File.ReadAllBytes( beside ) : null;
		} );

		var at = -1;
		for ( var i = 0; i < animations.Clips.Count; i++ )
			if ( string.Equals( animations.Clips[i], wanted, StringComparison.OrdinalIgnoreCase ) ) { at = i; break; }

		if ( at < 0 ) { Console.WriteLine( "they have no " + wanted ); return; }

		var theirs = animations.Read( at );
		var mine = Rinten.CoreAnimation.AnimClipData.Load( Path.Combine( ourFolder, wanted + ".anim_c" ) );

		Console.WriteLine( $"{wanted}: theirs {theirs.FrameCount}f/{theirs.BoneCount}b rate {theirs.FrameRate}" +
			$"  ours {mine.FrameCount}f/{mine.Bones.Length}b rate {mine.FrameRate}   their skeleton {animations.Skeleton.Count}b" );

		var pose = new Rinten.CoreAnimation.AnimTrs[mine.Bones.Length];
		mine.Sample( 0.0f, false, pose );

		foreach ( var named in bones )
		{
			var them = -1;
			for ( var i = 0; i < animations.Skeleton.Count; i++ )
				if ( string.Equals( animations.Skeleton[i].Name, named, StringComparison.OrdinalIgnoreCase ) ) { them = i; break; }

			var us = Array.FindIndex( mine.Bones, b => string.Equals( b.Name, named, StringComparison.OrdinalIgnoreCase ) );

			if ( them < 0 || us < 0 ) { Console.WriteLine( $"   {named}: theirs {them} ours {us}" ); continue; }

			//Their file counts in their axes; declared once rather than swizzled by hand - see ForeignBasis.
			var t = ForeignBasis.Quake.Vector( theirs.Positions[them] );
			var o = pose[us].Position;

			Console.WriteLine( $"   {named,-18} theirs ({t.x,7:0.000},{t.y,7:0.000},{t.z,7:0.000})" +
				$"   ours ({o.x,7:0.000},{o.y,7:0.000},{o.z,7:0.000})   moves={theirs.Moves[them]}" );
		}
	}

	public static void Compare( string compiledModel, string ourFolder, int most )
	{
		var animations = CoreImportedAnimations.Open( File.ReadAllBytes( compiledModel ), named =>
		{
			var beside = Path.Combine( Path.GetDirectoryName( compiledModel ) ?? "", Path.GetFileName( named ) );
			return File.Exists( beside ) ? File.ReadAllBytes( beside ) : null;
		} );

		if ( animations is null ) { Console.WriteLine( "their model has no animations in it" ); return; }

		Console.WriteLine( $"theirs: {animations.Clips.Count} clip(s), {animations.Skeleton.Count} bone(s)" );
		Console.WriteLine( "   first names: " + string.Join( ", ", animations.Clips.Take( 6 ) ) );

		var checked_ = 0;
		var rows = new List<(float Worst, string Name, string Where, int Frames, int Bones)>();
		var missing = new List<string>();

		for ( var at = 0; at < animations.Clips.Count && checked_ < most; at++ )
		{
			var name = animations.Clips[at];

			var ours = Path.Combine( ourFolder, name + ".anim_c" );
			if ( !File.Exists( ours ) )
			{
				//Their names are lower case in the compiled file.
				var found = Directory.EnumerateFiles( ourFolder, "*.anim_c" )
					.FirstOrDefault( f => string.Equals( Path.GetFileNameWithoutExtension( f ), name,
						StringComparison.OrdinalIgnoreCase ) );

				if ( found is null ) { missing.Add( name ); continue; }

				ours = found;
			}

			var mine = Rinten.CoreAnimation.AnimClipData.Load( ours );
			if ( mine is null ) { missing.Add( name + " (ours unreadable)" ); continue; }

			var theirs = animations.Read( at );
			if ( theirs is null || theirs.FrameCount <= 0 ) continue;

			//Differences only agree on the bones they drive: what a clip says nothing about is filled in with the rest pose
			//on their side and with nothing on ours, and neither is wrong. Poses are what can be compared bone for bone.
			if ( theirs.Delta ) continue;

			checked_++;

			var worst = 0.0f;
			var where = "";

			var worstSkinned = 0.0f;
			var whereSkinned = "";

			var frames = Math.Min( theirs.FrameCount, mine.FrameCount );

			var pose = new Rinten.CoreAnimation.AnimTrs[mine.Bones.Length];

			for ( var frame = 0; frame < frames; frame += Math.Max( 1, frames / 8 ) )
			{
				mine.Sample( frame / MathF.Max( mine.FrameRate, 1.0f ), false, pose );

				for ( var bone = 0; bone < animations.Skeleton.Count && bone < theirs.BoneCount; bone++ )
				{
					var named = animations.Skeleton[bone].Name;

					var at2 = Array.FindIndex( mine.Bones, b =>
						string.Equals( b.Name, named, StringComparison.OrdinalIgnoreCase ) );

					if ( at2 < 0 ) continue;

					var theirPlace = ForeignBasis.Quake.Vector(
						theirs.Positions[( frame * theirs.BoneCount ) + bone] );
					var off = ( pose[at2].Position - theirPlace ).Length;

					if ( off > worst ) { worst = off; where = $"{named} f{frame}"; }

					//Bones a body is drawn on, as against the markers a rig carries for its own solvers.
					var helper = named.Contains( "_IK", StringComparison.OrdinalIgnoreCase )
						|| named.Contains( "ikrule", StringComparison.OrdinalIgnoreCase )
						|| named.Contains( "helper", StringComparison.OrdinalIgnoreCase )
						|| named.Contains( "_to_", StringComparison.OrdinalIgnoreCase );

					if ( !helper && off > worstSkinned ) { worstSkinned = off; whereSkinned = $"{named} f{frame}"; }
				}
			}

			rows.Add( (worstSkinned, name, $"{whereSkinned}   [all bones: {worst * 100.0f:0.0}cm at {where}]",
				theirs.FrameCount, theirs.BoneCount) );
		}

		rows.Sort( ( a, b ) => b.Worst.CompareTo( a.Worst ) );

		Console.WriteLine( $"compared {rows.Count}, ours missing for {missing.Count}" );
		Console.WriteLine( "worst offset on a bone the body is drawn on, in cm:" );

		foreach ( var row in rows.Take( 20 ) )
			Console.WriteLine( $"   {row.Worst * 100.0f,8:0.00}  {row.Name,-42} {row.Where}" );

		var good = rows.Count( r => r.Worst < 0.005f );
		Console.WriteLine( $"within 5mm: {good} of {rows.Count}" );
	}
}
