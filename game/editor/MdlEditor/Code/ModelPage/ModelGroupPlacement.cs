using Rinten.UI;
﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor.MdlEditor;

//One handle standing for a whole selection: drag it and all of them follow.
static class ModelGroupPlacement
{
	/// <summary>Null unless at least two of the picked rows have a handle of their own.</summary>
	public static ScenePlacement For( ModelDocContext ctx, IReadOnlyList<ModelDocItem> items, ModelDocItem lead )
	{
		var parts = new List<ScenePlacement>();
		ScenePlacement led = null;

		foreach ( var item in items )
		{
			if ( item is null )
				continue;

			var placement = ModelDoc.Of( item.Kind )?.Placement( ctx, item );
			if ( placement is not { IsValid: true } )
				continue;

			parts.Add( placement );
			if ( lead is not null && ModelOutliner.SameId( item, lead ) )
				led = placement;
		}

		if ( parts.Count < 2 )
			return null;

		var head = led ?? parts[0];
		var scaled = parts.Where( p => p.CanScale ).ToList();
		var scaleHead = scaled.Contains( head ) ? head : scaled.FirstOrDefault();

		Func<Vector3> getScale = null;
		Action<Vector3> setScale = null;
		if ( scaleHead is not null )
		{
			getScale = () => scaleHead.GetScale();
			setScale = scale => Resize( scaled, scaleHead, scale );
		}

		return new ScenePlacement
		{
			Name = "group",
			GetPosition = () => Center( parts ),
			GetRotation = () => head.GetRotation(),
			Set = ( position, rotation ) => Apply( parts, head, position, rotation ),
			GetScale = getScale,
			SetScale = setScale,

			//One number for the lot as soon as any of them only has one, so the handle never offers an axis some of the selection cannot take.
			UniformScale = scaled.Any( p => p.UniformScale ),
		};
	}

	//The pivot the handle sits on, which is the middle of what is picked.
	static Vector3 Center( List<ScenePlacement> parts )
	{
		var sum = Vector3.Zero;
		var count = 0;

		foreach ( var part in parts )
		{
			var at = part.GetPosition();
			if ( at.IsNaN )
				continue;

			sum += at;
			count++;
		}

		return count == 0 ? Vector3.Zero : sum / count;
	}

	//Applies this frame's move and turn to every part, relative to their current average pivot
	static void Apply( List<ScenePlacement> parts, ScenePlacement head, Vector3 position, Angles rotation )
	{
		var from = Center( parts );
		var was = head.GetRotation();
		var turning = rotation != was;
		var turn = turning ? rotation.ToRotation() * was.ToRotation().Inverse : Rotation.Identity;
		var move = position - from;

		foreach ( var part in parts )
		{
			var at = part.GetPosition();
			if ( at.IsNaN )
				continue;

			var next = turning ? from + turn * ( at - from ) + move : at + move;
			part.Set( next, turning ? ( turn * part.GetRotation().ToRotation() ).Angles() : part.GetRotation() );
		}
	}

	//Scales every part by however much the handle scaled the one it reads.
	static void Resize( List<ScenePlacement> parts, ScenePlacement head, Vector3 scale )
	{
		var was = head.GetScale();
		var factor = new Vector3( Ratio( scale.x, was.x ), Ratio( scale.y, was.y ), Ratio( scale.z, was.z ) );

		foreach ( var part in parts )
		{
			var next = part.GetScale() * factor;
			next = new Vector3( Math.Max( 0.001f, next.x ), Math.Max( 0.001f, next.y ), Math.Max( 0.001f, next.z ) );
			part.SetScale( part.UniformScale ? new Vector3( next.x ) : next );
		}
	}

	static float Ratio( float to, float from ) => MathF.Abs( from ) < 0.0001f ? 1f : to / from;
}
