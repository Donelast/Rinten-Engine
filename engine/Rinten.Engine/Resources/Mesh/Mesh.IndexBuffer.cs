using System.Runtime.InteropServices;

namespace Rinten
{
	public partial class Mesh
	{
		/// <summary>Whether this mesh has an index buffer.</summary>
		public bool HasIndexBuffer => core.Indices is { Length: > 0 };

		/// <summary>Number of indices this mesh has.</summary>
		public int IndexCount => core.Indices?.Length ?? 0;

		/// <summary>Create an empty index buffer, it can be resized later</summary>
		public void CreateIndexBuffer()
		{
			CreateIndexBuffer( 0, Span<int>.Empty );
		}

		/// <summary>Create a index buffer with a number of indices</summary>
		public void CreateIndexBuffer( int indexCount, List<int> data )
		{
			CreateIndexBuffer( indexCount, CollectionsMarshal.AsSpan( data ) );
		}

		//The indices stay on the mesh: our renderer uploads them when it draws, rather than the mesh holding a buffer of its own on the card.
		/// <summary>Create a index buffer with a number of indices</summary>
		public void CreateIndexBuffer( int indexCount, Span<int> data = default )
		{
			if ( HasIndexBuffer )
				throw new InvalidOperationException( "Index buffer has already been created" );

			core.Indices = new int[Math.Max( indexCount, 0 )];

			data[..Math.Min( data.Length, core.Indices.Length )].CopyTo( core.Indices );
		}

		/// <summary>Set data of this buffer</summary>
		public void SetIndexBufferData( List<int> data, int elementOffset = 0 )
		{
			SetIndexBufferData( CollectionsMarshal.AsSpan( data ), elementOffset );
		}

		/// <summary>Set data of this buffer</summary>
		public void SetIndexBufferData( Span<int> data, int elementOffset = 0 )
		{
			if ( core.Indices is null )
				throw new InvalidOperationException( "Index buffer has not been created" );

			if ( elementOffset < 0 || elementOffset + data.Length > core.Indices.Length )
				throw new ArgumentException( "Setting index buffer data out of bounds" );

			data.CopyTo( core.Indices.AsSpan( elementOffset ) );
		}

		/// <summary>Resize the index buffer.</summary>
		public void SetIndexBufferSize( int elementCount )
		{
			elementCount = Math.Max( elementCount, 0 );

			var resized = new int[elementCount];

			core.Indices?.AsSpan( 0, Math.Min( core.Indices.Length, elementCount ) ).CopyTo( resized );
			core.Indices = resized;
		}

		public delegate void IndexBufferLockHandler( Span<int> data );

		//Nothing has to be locked: the indices are ours, in memory, so what the handler is given is the array itself.
		/// <summary>Lock all the memory in this buffer so you can write to it</summary>
		public void LockIndexBuffer( IndexBufferLockHandler handler )
		{
			LockIndexBuffer( 0, IndexCount, handler );
		}

		/// <summary>Lock a specific amount of the memory in this buffer so you can write to it</summary>
		public void LockIndexBuffer( int elementCount, IndexBufferLockHandler handler )
		{
			LockIndexBuffer( 0, elementCount, handler );
		}

		/// <summary>Lock a region of memory in this buffer so you can write to it</summary>
		public void LockIndexBuffer( int elementOffset, int elementCount, IndexBufferLockHandler handler )
		{
			if ( handler is null ) return;
			if ( core.Indices is null ) return;

			if ( elementOffset < 0 || elementCount < 0 ) return;
			if ( elementOffset + elementCount > core.Indices.Length ) return;

			handler( core.Indices.AsSpan( elementOffset, elementCount ) );
		}
	}
}
