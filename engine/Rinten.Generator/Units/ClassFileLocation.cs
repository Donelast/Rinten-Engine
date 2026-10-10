using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Rinten.Generator;

//If we're calling this from the server, throw an assertion. Add a transport functions so it can be called from the server.
internal static class ClassFileLocation
{
	internal static MemberDeclarationSyntax VisitNode( MemberDeclarationSyntax node, MemberDeclarationSyntax originalNode, ISymbol symbol, Worker master, SyntaxTree inputTree )
	{
		if ( !master.IsFullGeneration ) return node;

		if ( string.IsNullOrWhiteSpace( inputTree?.FilePath ) )
			return node;

		var relativePath = inputTree.FilePath;

		var mappedSpan = originalNode.GetLocation().GetMappedLineSpan();
		var lineNumber = mappedSpan.Span.Start.Line + 1;

		if ( master.AddonFileMap.TryGetValue( relativePath, out var outRelativePath ) )
		{
			relativePath = outRelativePath;
		}

		if ( System.IO.Path.IsPathRooted( relativePath ) )
		{
			if ( master.IsFullGeneration )
			{
				master.AddError( symbol.Locations.First(), $"Couldn't find relative class location for {inputTree.FilePath}" );
			}

			return node;
		}

		var name = ParseName( "Rinten.Internal.SourceLocation" );

		var argList = AttributeArgumentList();

		argList = argList.AddArguments( AttributeArgument( LiteralExpression( SyntaxKind.StringLiteralExpression, Literal( relativePath ) ) ) );
		argList = argList.AddArguments( AttributeArgument( LiteralExpression( SyntaxKind.NumericLiteralExpression, Literal( lineNumber ) ) ) );


		var attribute = Attribute( name, argList );

		var attributeList = new SeparatedSyntaxList<AttributeSyntax>();
		attributeList = attributeList.Add( attribute );


		//On the member's own line, not one of its own: the #line directive in front of the member names its line
		//in the source, and an attribute line between the two put every diagnostic on the member one line late.
		var list = AttributeList( attributeList ).WithTrailingTrivia( Whitespace( " " ) );

		var trivia = node.GetLeadingTrivia();
		node = node.WithoutLeadingTrivia().AddAttributeLists( list ).WithLeadingTrivia( trivia );
		return node;
	}
}
