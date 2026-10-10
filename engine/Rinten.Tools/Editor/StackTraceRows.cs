using System;
using System.Reflection;
using System.Text.RegularExpressions;
using JetBrains.Annotations;

namespace Editor;

/// <summary>Marks a method as a custom handler for stack trace lines matching a certain pattern. The method must take in a <see cref="Match"/> parameter, and return a <see cref="StackRow"/> (or null).</summary>
[AttributeUsage( AttributeTargets.Method )]
public sealed class StackLineHandlerAttribute : Attribute
{
	[RegexPattern]
	public string Regex { get; }
	public int Order { get; set; }

	public StackLineHandlerAttribute( [RegexPattern] string regex )
	{
		Regex = regex;
	}
}

//Reads a stack trace into rows (name, place, click action); the console panel draws them
internal static class StackTraceRows
{
	private delegate StackRow StackLineHandler( Match match );

	private record struct StackLineHandlerInfo
		( Regex Regex, StackLineHandler Handler, int Order ) : IComparable<StackLineHandlerInfo>
	{
		public int CompareTo( StackLineHandlerInfo other ) => Order - other.Order;
	}

	[SkipHotload]
	private static List<StackLineHandlerInfo> StackLineHandlers { get; set; }

	[EditorEvent.Hotload]
	private static void OnHotload()
	{
		StackLineHandlers = null;
	}

	private static void UpdateStackLineHandlers()
	{
		if ( StackLineHandlers != null )
		{
			return;
		}

		StackLineHandlers = new List<StackLineHandlerInfo>();

		var methods = EditorTypeLibrary.GetMethodsWithAttribute<StackLineHandlerAttribute>();

		foreach ( var (method, attrib) in methods )
		{
			if ( method.Parameters.Length != 1 )
			{
				continue;
			}

			try
			{
				var regex = new Regex( attrib.Regex );
				var handler = (StackLineHandler)Delegate.CreateDelegate( typeof( StackLineHandler ),
					(MethodInfo)method.MemberInfo );
				StackLineHandlers.Add( new( regex, handler, attrib.Order ) );
			}
			catch ( Exception e )
			{
				Log.Warning( $"{method.MemberInfo.ToSimpleString()}: {e}" );
			}
		}

		StackLineHandlers.Sort();
	}

	/// <summary>Reads a whole stack, in the order its frames were written.</summary>
	public static List<StackRow> Read( string stack )
	{
		var rows = new List<StackRow>();

		if ( string.IsNullOrWhiteSpace( stack ) ) return rows;

		foreach ( var line in stack.Split( '\n', '\r' ) )
		{
			if ( ReadLine( line ) is not { } row ) continue;

			row.Index = rows.Count;

			rows.Add( row );
		}

		return rows;
	}

	//Reads one frame, through the first handler whose pattern it matches.
	private static StackRow ReadLine( string line )
	{
		if ( string.IsNullOrWhiteSpace( line ) )
			return null;

		UpdateStackLineHandlers();

		line = line.Trim();

		foreach ( var (regex, handler, _) in StackLineHandlers )
		{
			var match = regex.Match( line );

			if ( !match.Success )
				continue;

			return handler( match );
		}

		return null;
	}

	[StackLineHandler( @"^at (.+?)( in (.+):line (.+))?$", Order = 1000 )]
	public static StackRow DefaultStackLineHandler( Match match )
	{
		var hasFile = match.Groups[3].Success;
		var functionName = match.Groups[1].Value;
		if ( functionName.IndexOf( '(' ) > 0 ) functionName = functionName.Substring( 0, functionName.IndexOf( '(' ) );

		if ( !hasFile )
		{
			return UnknownStackLineHandler( match );
		}

		var fileName = match.Groups[3].Value;
		var fileLine = match.Groups[4].Value;

		var row = new StackRow( functionName, $"{fileName}:{fileLine}" )
		{
			IsFromEngine = match.Value.Contains( "\\engine\\Rinten." )
		};

		row.MouseClick += () => CodeEditor.OpenFile( fileName, fileLine.ToInt() );

		return row;
	}

	[StackLineHandler( @".+", Order = 1001 )]
	public static StackRow UnknownStackLineHandler( Match match )
	{
		return new StackRow( match.Value, null )
		{
			IsFromEngine = match.Value.Contains( "\\engine\\Rinten." )
		};
	}
}

//A reading rather than a control. Handlers written elsewhere - the action graph editor has two - build these, and whoever is showing the stack decides what one looks like.
/// <summary>One frame of a stack: what it was in, where that is, and what to do about it.</summary>
public class StackRow
{
	/// <summary>What the frame was in - a method, a node, whatever the handler made of it.</summary>
	public string FunctionName { get; }

	/// <summary>Where that is, as text to read, or nothing where the frame names no place.</summary>
	public string FileName { get; }

	/// <summary>Whether the frame is inside the engine rather than in what is being written.</summary>
	public bool IsFromEngine;

	/// <summary>Whether the frame is inside an action graph.</summary>
	public bool IsFromAction;

	/// <summary>How far down the stack it is.</summary>
	public int Index;

	/// <summary>Run when the row is pressed - usually opening whatever it names.</summary>
	public Action MouseClick;

	public StackRow( string functionName, string fileName )
	{
		FunctionName = functionName;
		FileName = fileName;
	}
}
