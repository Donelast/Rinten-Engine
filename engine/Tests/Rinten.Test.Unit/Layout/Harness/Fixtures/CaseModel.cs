using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace LayoutTests.Harness.Fixtures;

//One box of a fixture: its CSS, as property to value, and its children. The same description is rendered to HTML for the
//browser and mapped onto the engine's style, so the two can never drift apart: whatever the fixture says, both sides receive.
internal sealed class CaseNode
{
	[JsonPropertyName( "css" )]
	public Dictionary<string, string> Css { get; set; } = new();

	[JsonPropertyName( "children" )]
	public List<CaseNode> Children { get; set; } = new();

	//Pre-order walk: the order rects are recorded in on both sides.
	public IEnumerable<(CaseNode Node, string Path)> Walk( string path = "root" )
	{
		yield return (this, path);
		for ( var i = 0; i < Children.Count; i++ )
		{
			foreach ( var pair in Children[i].Walk( $"{path}/{i}" ) )
				yield return pair;
		}
	}
}

//A named fixture: a root box laid out inside a 400 x 400 owner.
internal sealed class FixtureCase
{
	public const float OwnerWidth = 400;
	public const float OwnerHeight = 400;

	[JsonPropertyName( "name" )]
	public string Name { get; set; }

	[JsonPropertyName( "root" )]
	public CaseNode Root { get; set; }
}

//The little language the catalogue is written in: N( "width:50px;height:30px", children... ) .
internal static class Dsl
{
	public static CaseNode N( string css, params CaseNode[] children )
	{
		var node = new CaseNode();
		foreach ( var declaration in css.Split( ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var colon = declaration.IndexOf( ':' );
			if ( colon <= 0 ) throw new ArgumentException( $"Bad declaration '{declaration}' in '{css}'" );
			node.Css[declaration[..colon].Trim()] = declaration[(colon + 1)..].Trim();
		}

		node.Children.AddRange( children );
		return node;
	}

	//Adds declarations to a node's CSS, overriding what was there, and returns it.
	public static CaseNode With( this CaseNode node, string css )
	{
		var extra = N( css );
		foreach ( var (key, value) in extra.Css ) node.Css[key] = value;
		return node;
	}
}
