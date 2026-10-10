using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Rinten;

internal unsafe static partial class Interop
{
	private static Logger log = Logging.GetLogger();

	//Native can call back into managed (and thus allocate temporary return strings) from non-main threads,
	//while Free() drains this at frame end on the main thread, so this has to be a concurrent collection.
	[SkipHotload] static ConcurrentQueue<PassBackString> FrameAllocatedStrings = new();

	public static int Free()
	{
		int i = 0;

		while ( FrameAllocatedStrings.TryDequeue( out var entry ) )
		{
			entry.Free();
			i++;
		}

		return i;
	}

	const int maxNativeString = 1024 * 1024 * 64; //a 64mb string sounds sensible!

	/// <summary>Throw helpers for the generated bindings. Keeping the throw (and its message formatting) out of the generated methods keeps their bodies tiny, so they inline well and JIT fast.</summary>
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowNullSelf( string className, string methodName )
	{
		throw new System.NullReferenceException( $"{className} was null when calling {methodName}" );
	}

	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowNull( string className )
	{
		throw new System.NullReferenceException( $"{className} was null" );
	}

	//The name of the function comes from the caller rather than from the message: the generated bindings all throw through here, and an exception
	//that says only "function pointer is null" from a task continuation names nothing at all - the frames above it are the thread pool's.
	[System.Diagnostics.CodeAnalysis.DoesNotReturn]
	public static void ThrowNullFunctionPointer(
		[System.Runtime.CompilerServices.CallerMemberName] string member = null )
	{
		throw new System.Exception( member is null
			? "Function Pointer Is Null"
			: $"Function Pointer Is Null: {member}" );
	}

	/// <summary>Convert a native utf pointer to a string</summary>
	public static string GetString( IntPtr pointer )
	{
		if ( pointer == IntPtr.Zero )
			return null;

		//Uses the runtime's vectorized strlen to find the null terminator
		var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated( (byte*)pointer );

		if ( span.Length >= maxNativeString )
		{
			Log.Warning( "Really long, or really invalid string detected" );
			return null;
		}

		if ( span.IsEmpty )
			return string.Empty;

		return Encoding.UTF8.GetString( span );
	}

	/// <summary>Convert a native utf pointer to a string</summary>
	public static string GetString( IntPtr pointer, int byteLen )
	{
		if ( pointer == IntPtr.Zero || byteLen < 0 )
			return null;

		if ( byteLen == 0 )
			return string.Empty;

		return Encoding.UTF8.GetString( (byte*)pointer, byteLen );
	}

	/// <summary>Convert a native utf pointer to a string</summary>
	public static string GetWString( IntPtr pointer )
	{
		if ( pointer == IntPtr.Zero )
			return null;

		return Marshal.PtrToStringUni( pointer );
	}

	/// <summary>Convert a native utf pointer to a string</summary>
	public static string GetWString( IntPtr pointer, int byteLen )
	{
		if ( pointer == IntPtr.Zero || byteLen <= 0 )
			return null;

		return Marshal.PtrToStringUni( pointer, byteLen );
	}

	public unsafe ref struct InteropString
	{
		public IntPtr Pointer;
		private bool heap;

		public InteropString( string str )
		{
			if ( str is null )
				return;

			AllocateOnHeap( str, Encoding.UTF8.GetByteCount( str ) );
		}

		/// <summary>Marshal using a caller-provided buffer - the generated bindings pass a stackalloc, so typical strings never touch the heap allocator. Falls back to the heap when the encoded string doesn't fit. The buffer must outlive this struct.</summary>
		public InteropString( string str, Span<byte> buffer )
		{
			if ( str is null )
				return;

			//UTF8 is at most 3 bytes per char, so this guarantees a fit without counting first
			if ( str.Length * 3 + 1 <= buffer.Length )
			{
				UseBuffer( str, buffer );
				return;
			}

			int byteCount = Encoding.UTF8.GetByteCount( str );
			if ( byteCount < buffer.Length )
			{
				UseBuffer( str, buffer );
				return;
			}

			AllocateOnHeap( str, byteCount );
		}

		private void UseBuffer( string str, Span<byte> buffer )
		{
			int nb = Encoding.UTF8.GetBytes( str, buffer );
			buffer[nb] = 0;
			Pointer = (IntPtr)Unsafe.AsPointer( ref MemoryMarshal.GetReference( buffer ) );
		}

		private void AllocateOnHeap( string str, int byteCount )
		{
			byte* mem = (byte*)NativeMemory.Alloc( (uint)byteCount + 1 );

			fixed ( char* src = str )
			{
				Encoding.UTF8.GetBytes( src, str.Length, mem, byteCount );
			}

			mem[byteCount] = 0;
			Pointer = (IntPtr)mem;
			heap = true;
		}

		public void Free()
		{
			if ( heap )
			{
				NativeMemory.Free( (void*)Pointer );
				heap = false;
			}

			Pointer = default;
		}
	}


	/// <summary>used to pass a string back to native</summary>
	public unsafe struct PassBackString
	{
		public IntPtr Pointer;

		public PassBackString( string str )
		{
			if ( str is null )
				return;

			uint nb = (uint)Encoding.UTF8.GetByteCount( str );
			byte* mem = (byte*)NativeMemory.Alloc( nb + 1 );

			fixed ( char* src = str )
			{
				Encoding.UTF8.GetBytes( src, str.Length, mem, (int)nb );
			}

			mem[nb] = 0;
			Pointer = (IntPtr)mem;
		}

		public void Free()
		{
			NativeMemory.Free( (void*)Pointer );
			Pointer = default;
		}
	}

	//Strings returned to native can't be freed at once, so they are kept and freed at frame end
	internal static IntPtr GetTemporaryStringPointerForNative( string str )
	{
		var f = new PassBackString( str );
		FrameAllocatedStrings.Enqueue( f );
		return f.Pointer;
	}
}
