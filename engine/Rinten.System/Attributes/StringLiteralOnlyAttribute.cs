using System;

namespace Rinten;

/// <summary>Ask codegen to shit itself if the parameter isn't passed in as a string literal</summary>
[AttributeUsage( AttributeTargets.Parameter )]
public class StringLiteralOnlyAttribute : System.Attribute
{
}
