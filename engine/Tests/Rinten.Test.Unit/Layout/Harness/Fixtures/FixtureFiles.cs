using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace LayoutTests.Harness.Fixtures;

//The files the browser pipeline exchanges: the case list (JSON, committed), the page the browser renders (HTML, generated on
//demand), and the rects the browser reported (JSON, committed). Regeneration is a deliberate step, see docs/layout-metrics.md.
internal static class FixtureFiles
{
	public const string CasesFile = "browser-cases.json";
	public const string ExpectedFile = "browser-expected.json";
	public const string PageFile = "browser-cases.html";

	//Set to a directory to write the case list and the page there.
	public const string WriteVariable = "RINTEN_LAYOUT_FIXTURES_OUT";

	private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

	public static string Serialize( List<FixtureCase> cases ) => JsonSerializer.Serialize( cases, Options );

	public static List<FixtureCase> ReadCases( string path ) => JsonSerializer.Deserialize<List<FixtureCase>>( File.ReadAllText( path ), Options );

	public static Dictionary<string, float[][]> ReadExpected( string path )
		=> File.Exists( path ) ? JsonSerializer.Deserialize<Dictionary<string, float[][]>>( File.ReadAllText( path ), Options ) : new Dictionary<string, float[][]>();

	//One page with all cases in 400x400 owners, reset to engine-like flex boxes; rects() returns JSON
	public static string RenderPage( List<FixtureCase> cases )
	{
		var sb = new StringBuilder();
		sb.Append( "<!doctype html><html><head><meta charset=\"utf-8\"><title>layout fixtures</title><style>" );
		sb.Append( "html,body{margin:0;padding:0;font-size:0;line-height:0}" );
		sb.Append( ".case{position:relative;display:block;width:400px;height:400px;margin:0 0 8px 0;overflow:visible}" );
		sb.Append( ".case *{display:flex;box-sizing:border-box;margin:0;padding:0;border:0 solid transparent;min-width:0;min-height:0;position:static;scrollbar-width:none;flex-direction:row}" );
		//The root is the containing block for its absolute descendants, as the engine treats a root, and for
		//its fixed ones: a transformed box contains those, where a plain one leaves them to the viewport.
		sb.Append( ".case>div{position:relative;transform:translate(0,0)}" );
		sb.Append( "</style></head><body>" );

		foreach ( var c in cases )
		{
			sb.Append( "<div class=\"case\" data-case=\"" ).Append( Escape( c.Name ) ).Append( "\">" );
			RenderNode( sb, c.Root, isRoot: true );
			sb.Append( "</div>" );
		}

		sb.Append( "<script>" );
		sb.Append( "function rectsOf(el, originLeft, originTop, out){" );
		sb.Append( "var r=el.getBoundingClientRect();var disp=getComputedStyle(el).display;" );
		sb.Append( "if(disp==='none'){zeros(el,out);return;}" );
		sb.Append( "if(disp==='contents'){out.push([0,0,0,0]);for(var c=el.firstElementChild;c;c=c.nextElementSibling)rectsOf(c,originLeft,originTop,out);return;}" );
		sb.Append( "out.push([round(r.left-originLeft),round(r.top-originTop),round(r.width),round(r.height)]);" );
		sb.Append( "for(var c=el.firstElementChild;c;c=c.nextElementSibling)rectsOf(c,r.left,r.top,out);}" );
		sb.Append( "function round(v){return Math.round(v*1000)/1000;}" );
		sb.Append( "function zeros(el,out){out.push([0,0,0,0]);for(var c=el.firstElementChild;c;c=c.nextElementSibling)zeros(c,out);}" );
		sb.Append( "window.rects=function(start,count){var out={};var cases=document.querySelectorAll('.case');" );
		sb.Append( "for(var i=start;i<Math.min(cases.length,start+count);i++){var c=cases[i];var cr=c.getBoundingClientRect();var list=[];rectsOf(c.firstElementChild,cr.left,cr.top,list);out[c.getAttribute('data-case')]=list;}" );
		sb.Append( "return JSON.stringify(out);};" );
		sb.Append( "window.caseCount=function(){return document.querySelectorAll('.case').length;};" );
		sb.Append( "</script></body></html>" );
		return sb.ToString();
	}

	private static void RenderNode( StringBuilder sb, CaseNode node, bool isRoot = false )
	{
		sb.Append( "<div style=\"" );
		foreach ( var (property, value) in node.Css )
		{
			//A block root starts a formatting context in the engine, as every root does; flow-root
			//is the browser's word for a block that does, so its first child's margin stays inside.
			var rendered = isRoot && property == "display" && value == "block" ? "flow-root" : value;
			sb.Append( property ).Append( ':' ).Append( rendered ).Append( ';' );
		}
		sb.Append( "\">" );
		foreach ( var child in node.Children ) RenderNode( sb, child );
		sb.Append( "</div>" );
	}

	private static string Escape( string text ) => text.Replace( "&", "&amp;" ).Replace( "\"", "&quot;" ).Replace( "<", "&lt;" );
}
