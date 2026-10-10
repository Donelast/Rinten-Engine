using System.Text;
using System.Text.RegularExpressions;

namespace Rinten;

partial class CoreShaderCompiler
{
	//Fixes Slang's WGSL for the browser: entry named main, push block on the push ring, group samplers
	//Also flips position to Y-up, strips stray @interpolate, and allows sampling in divergent branches
	private static byte[] Wgsl( byte[] words, Stage stage )
	{
		var text = Encoding.UTF8.GetString( words );

		text = EntryName().Replace( text, "${stage}fn main(" );
		text = StrayInterpolate().Replace( text, "$1" );
		text = UnboundUniform().Replace( text, $"@binding({BrowserDevice.PushBinding}) @group(0) $1" );
		text = OneSamplerAGroup( text );

		if ( stage == Stage.Vertex ) text = PositionTurnedOver( text );

		return Encoding.UTF8.GetBytes( "diagnostic(off, derivative_uniformity);\n" + text );
	}

	private static string OneSamplerAGroup( string text )
	{
		var samplers = SamplerDeclaration().Matches( text );
		if ( samplers.Count == 0 ) return text;

		var declared = new SortedSet<string>();
		var renamed = new Dictionary<string, string>();

		foreach ( Match m in samplers )
		{
			var group = int.Parse( m.Groups["group"].Value );
			var type = m.Groups["type"].Value;

			//One sampler of each kind per group: WGSL refuses a filtered read through a comparison sampler
			var compares = type.Contains( "sampler_comparison" );
			var name = $"rinten_sampler_{group}{( compares ? "_compare" : "" )}";

			var at = group == 0
				? BrowserDevice.MaterialSamplerBinding
				: compares ? BrowserDevice.ShadowSamplerBinding : BrowserDevice.FilterSamplerBinding;

			renamed[m.Groups["name"].Value] = name;
			declared.Add( $"@binding({at}) @group({group}) var {name} : {type};" );
		}

		text = SamplerDeclaration().Replace( text, "" );

		foreach ( var (from, to) in renamed )
			text = Regex.Replace( text, $@"\b{Regex.Escape( from )}\b", to );

		return string.Join( "\n", declared ) + "\n" + text;
	}

	//Every value the vertex entry returns, with its position's Y negated.
	private static string PositionTurnedOver( string text )
	{
		var entry = VertexEntry().Match( text );
		if ( !entry.Success ) return text;

		var returns = entry.Groups["type"].Value.Trim();
		string turn;

		if ( returns.Contains( "@builtin(position)" ) )
		{
			turn = "return ({0}) * vec4<f32>(1.0, -1.0, 1.0, 1.0);";
		}
		else
		{
			var field = Regex.Match( text, $@"struct\s+{Regex.Escape( returns )}\s*\{{[^}}]*?@builtin\(position\)\s*(\w+)\s*:" );
			if ( !field.Success ) return text;

			var name = field.Groups[1].Value;
			turn = $"{{{{ var rinten_out = {{0}}; rinten_out.{name}.y = -rinten_out.{name}.y; return rinten_out; }}}}";
		}

		//The entry's body, by its braces: WGSL has no nested functions, so every return between them is the entry's.
		var open = text.IndexOf( '{', entry.Index + entry.Length - 1 );
		if ( open < 0 ) return text;

		var depth = 0;
		var close = open;

		for ( ; close < text.Length; close++ )
		{
			if ( text[close] == '{' ) depth++;
			else if ( text[close] == '}' && --depth == 0 ) break;
		}

		var body = ReturnStatement().Replace( text[open..close], m => string.Format( turn, m.Groups[1].Value ) );

		return text[..open] + body + text[close..];
	}

	[GeneratedRegex( @"(?<stage>@(?:vertex|fragment|compute)\b[^\n]*\s*)fn\s+\w+\s*\(" )]
	private static partial Regex EntryName();

	[GeneratedRegex( @"^([ \t]*)@interpolate\([^)]*\)[ \t]*(?=[^@\n]*$)", RegexOptions.Multiline )]
	private static partial Regex StrayInterpolate();

	[GeneratedRegex( @"^(var<uniform>)", RegexOptions.Multiline )]
	private static partial Regex UnboundUniform();

	[GeneratedRegex( @"^@binding\(\d+\)\s*@group\((?<group>\d+)\)\s*var\s+(?<name>\w+)\s*:\s*(?<type>sampler(?:_comparison)?)\s*;[ \t]*\r?\n?", RegexOptions.Multiline )]
	private static partial Regex SamplerDeclaration();

	//The parameters may carry attributes of their own - @builtin(vertex_index) - so a parenthesis one deep is part of them.
	[GeneratedRegex( @"@vertex\s*fn\s+\w+\s*\((?:[^()]|\([^()]*\))*\)\s*->\s*(?<type>[^{]+)\{" )]
	private static partial Regex VertexEntry();

	[GeneratedRegex( @"\breturn\s+([^;]+);" )]
	private static partial Regex ReturnStatement();
}
