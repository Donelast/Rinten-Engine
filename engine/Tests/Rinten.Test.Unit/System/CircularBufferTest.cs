using Rinten.Utility;
using System;

namespace SystemTests;

[TestClass]
public class CircularBufferTest
{
	//A fresh buffer should be empty, then fill up as items are pushed.
	[TestMethod]
	public void PushBackFills()
	{
		var buffer = new CircularBuffer<int>( 3 );

		Assert.IsTrue( buffer.IsEmpty );
		Assert.AreEqual( 3, buffer.Capacity );

		buffer.PushBack( 1 );
		buffer.PushBack( 2 );

		Assert.AreEqual( 2, buffer.Size );
		Assert.AreEqual( 1, buffer.Front() );
		Assert.AreEqual( 2, buffer.Back() );

		buffer.PushBack( 3 );
		Assert.IsTrue( buffer.IsFull );
	}

	//Pushing onto a full buffer should overwrite the element at the opposite end rather than throwing.
	[TestMethod]
	public void PushBackWhenFullOverwritesFront()
	{
		var buffer = new CircularBuffer<int>( 3 );
		buffer.PushBack( 1 );
		buffer.PushBack( 2 );
		buffer.PushBack( 3 );

		buffer.PushBack( 4 );

		Assert.AreEqual( 3, buffer.Size );
		Assert.AreEqual( 2, buffer.Front() );
		Assert.AreEqual( 4, buffer.Back() );
		CollectionAssert.AreEqual( new[] { 2, 3, 4 }, buffer.ToArray() );
	}

	//PushFront should prepend, and when full overwrite the back element.
	[TestMethod]
	public void PushFront()
	{
		var buffer = new CircularBuffer<int>( 3 );
		buffer.PushFront( 1 );
		buffer.PushFront( 2 );

		CollectionAssert.AreEqual( new[] { 2, 1 }, buffer.ToArray() );

		buffer.PushFront( 3 );
		buffer.PushFront( 4 );

		CollectionAssert.AreEqual( new[] { 4, 3, 2 }, buffer.ToArray() );
	}

	//Popping from either end should remove exactly that element.
	[TestMethod]
	public void PopBothEnds()
	{
		var buffer = new CircularBuffer<int>( 4 );
		buffer.PushBack( 1 );
		buffer.PushBack( 2 );
		buffer.PushBack( 3 );

		buffer.PopFront();
		CollectionAssert.AreEqual( new[] { 2, 3 }, buffer.ToArray() );

		buffer.PopBack();
		CollectionAssert.AreEqual( new[] { 2 }, buffer.ToArray() );
	}

	//The indexer should address elements from the front, and throw beyond the current size.
	[TestMethod]
	public void Indexer()
	{
		var buffer = new CircularBuffer<int>( 3 );
		buffer.PushBack( 10 );
		buffer.PushBack( 20 );

		Assert.AreEqual( 10, buffer[0] );
		Assert.AreEqual( 20, buffer[1] );
		Assert.ThrowsException<IndexOutOfRangeException>( () => buffer[2] );
	}

	//Clear should empty the buffer so it can be reused.
	[TestMethod]
	public void Clear()
	{
		var buffer = new CircularBuffer<int>( 3 );
		buffer.PushBack( 1 );
		buffer.PushBack( 2 );

		buffer.Clear();

		Assert.IsTrue( buffer.IsEmpty );
		Assert.AreEqual( 0, buffer.ToArray().Length );

		buffer.PushBack( 5 );
		Assert.AreEqual( 5, buffer.Front() );
	}

	//Front, Back and Pop on an empty buffer should throw rather than return garbage.
	[TestMethod]
	public void EmptyBufferThrows()
	{
		var buffer = new CircularBuffer<int>( 3 );

		Assert.ThrowsException<InvalidOperationException>( () => buffer.Front() );
		Assert.ThrowsException<InvalidOperationException>( () => buffer.Back() );
		Assert.ThrowsException<InvalidOperationException>( () => buffer.PopBack() );
		Assert.ThrowsException<InvalidOperationException>( () => buffer.PopFront() );
	}

	//Enumeration should yield elements front to back, even after the buffer has wrapped around its internal array.
	[TestMethod]
	public void EnumerationAfterWrap()
	{
		var buffer = new CircularBuffer<int>( 3 );
		for ( int i = 1; i <= 5; i++ )
		{
			buffer.PushBack( i );
		}

		CollectionAssert.AreEqual( new[] { 3, 4, 5 }, buffer.ToList() );
	}
}
