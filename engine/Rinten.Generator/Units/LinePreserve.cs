using Rinten;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace Rinten.Generator
{
	static class LinePreserve
	{
		public static T AddLineNumber<T>( T node, T originalNode, SyntaxTree tree, Worker master ) where T : SyntaxNode
		{
			if ( !master.IsFullGeneration )
				return node;

			if ( tree.FilePath.StartsWith( "_gen_" ) )
				return node;

			var lineSpan = originalNode.GetLocation().GetLineSpan();
			if ( originalNode.HasLeadingTrivia ) lineSpan = originalNode.GetLeadingTrivia().First().GetLocation().GetLineSpan();

			var lineNm = SyntaxFactory.Literal( $"{lineSpan.StartLinePosition.Line + 1}", lineSpan.StartLinePosition.Line + 1 );
			var file = SyntaxFactory.Literal( $"{tree.FilePath}" );

			var line = SyntaxFactory.LineDirectiveTrivia( lineNm, file, true ).NormalizeWhitespace().WithLeadingTrivia( SyntaxFactory.CarriageReturn );

			var trivia = node.HasLeadingTrivia ? node.GetLeadingTrivia() : SyntaxFactory.TriviaList();

			trivia = trivia.Insert( 0, SyntaxFactory.Trivia( line ) );

			return node.WithLeadingTrivia( trivia );
		}
	}
}
