using NativeEngine;
using System.Runtime.InteropServices;

namespace Rinten;

public sealed partial class PhysicsShape
{

	public ITagSet Tags { get; private set; }

	/// <summary>Does this shape have a specific tag?</summary>
	[System.Obsolete( "Use Tags" )]
	public bool HasTag( string tag ) => Tags.Has( tag );

	/// <summary>Add a tag to this shape.</summary>
	[System.Obsolete( "Use Tags" )]
	public bool AddTag( string tag )
	{
		Tags.Add( tag );
		return true;
	}

	/// <summary>Remove a tag from this shape.</summary>
	[System.Obsolete( "Use Tags" )]
	public bool RemoveTag( string tag )
	{
		Tags.Remove( tag );
		return true;
	}

	/// <summary>Clear all tags from this shape.</summary>
	[System.Obsolete( "Use Tags" )]
	public bool ClearTags()
	{
		Tags.RemoveAll();
		return true;
	}


	internal class TagAccessor : ITagSet
	{
		readonly PhysicsShape shape;
		HashSet<string> all = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		internal TagAccessor( PhysicsShape shape )
		{
			this.shape = shape;
		}

		public override void Add( string tag )
		{
			if ( all.Add( tag ) )
			{
				shape.Backend.AddTag( StringToken.FindOrCreate( tag ) );
			}
		}

		public override IEnumerable<string> TryGetAll()
		{
			return all.AsEnumerable();
		}

		public override bool Has( string tag )
		{
			return all.Contains( tag );
		}

		public override void Remove( string tag )
		{
			if ( all.Remove( tag ) )
			{
				shape.Backend.RemoveTag( StringToken.FindOrCreate( tag ) );
			}
		}

		public override void RemoveAll()
		{
			foreach ( var t in all.ToArray() )
			{
				Remove( t );
			}

			shape.Backend.ClearTags();
		}
	}

}
