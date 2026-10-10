
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Rinten;

//Ref struct that rents a pooled buffer as a Span; use in a using block to return it
internal readonly ref struct PooledSpan<T>
{
	//The usable length within array.
	private readonly int length;

	//The underlying T array.
	private readonly T[] array;

	private readonly bool clearOnReturn;

	/// <summary>Initializes a new instance of the <see cref="PooledSpan{T}"/> struct with the specified parameters.</summary>
	public PooledSpan( int length, bool clearOnReturn = false )
	{
		this.length = length;
		this.clearOnReturn = clearOnReturn;
		this.array = ArrayPool<T>.Shared.Rent( length );
	}

	/// <summary>Gets a <see cref="Span{T}"/> wrapping the memory belonging to the current instance.</summary>
	public Span<T> Span => array.AsSpan( 0, length );

	/// <summary>The rented array, which may be longer than <see cref="Span"/>. Only for APIs that can't accept a span, such as anything called through a delegate - prefer <see cref="Span"/> everywhere else.</summary>
	public T[] Array => array;

	/// <summary>Implements the duck-typed <see cref="IDisposable.Dispose"/> method.</summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public void Dispose()
	{
		//A default instance never rented anything.
		if ( array is null )
			return;

		ArrayPool<T>.Shared.Return( array, clearOnReturn );
	}
}
