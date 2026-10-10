using Silk.NET.Vulkan;
using VkBuffer = Silk.NET.Vulkan.Buffer;

namespace Rinten;

//Async GPU-to-host buffer copies, done when the recording's ticket completes; staging is reused
internal static unsafe class VkReadbacks
{
	private enum Stage
	{
		/// <summary>Waiting for a recording to go into.</summary>
		Asked,

		/// <summary>Recorded into the open recording, whose ticket is not known yet.</summary>
		Recorded,

		/// <summary>Submitted as <see cref="Request.Ticket"/>.</summary>
		Submitted,

		/// <summary>The buffer went before the copy could be recorded.</summary>
		Failed,
	}

	private sealed class Request
	{
		public ulong Buffer;
		public ulong Offset;
		public ulong Size;
		public ulong Ticket;
		public Stage Stage;

		public VkBuffer Staging;
		public DeviceMemory Memory;
		public byte* Mapped;
		public ulong Capacity;
	}

	private static readonly Dictionary<int, Request> requests = new();

	//Staging a finished request left behind, for the next.
	private static readonly List<Request> spare = new();

	//How many are kept, and the largest - the rest go back to the device.
	private const int SpareKept = 8;
	private const ulong SpareLargest = 16ul * 1024ul * 1024ul;

	private static int nextId = 1;

	//A copy of size bytes of a buffer from offset, to be recorded at the end of the
	//next recording. The request's number, or nought where it could not be asked.
	internal static int Ask( ulong buffer, long offset, long size )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !VkDevice.Ready || size <= 0 || offset < 0 ) return 0;

			var bytes = VkBuffers.BytesOf( buffer );

			if ( bytes == 0 || (ulong)offset + (ulong)size > bytes )
			{
				VkDevice.SetText( "that read runs past the end of the buffer" );
				return 0;
			}

			var request = Staged( (ulong)size );
			if ( request is null ) return 0;

			request.Buffer = buffer;
			request.Offset = (ulong)offset;
			request.Size = (ulong)size;
			request.Ticket = 0;
			request.Stage = Stage.Asked;

			var id = nextId;
			nextId = nextId == int.MaxValue ? 1 : nextId + 1;

			requests[id] = request;

			return id;
		}
	}

	//A request with host memory of at least this size: a spare one, or one made now.
	private static Request Staged( ulong size )
	{
		var best = -1;

		for ( var i = 0; i < spare.Count; i++ )
		{
			if ( spare[i].Capacity < size ) continue;
			if ( best < 0 || spare[i].Capacity < spare[best].Capacity ) best = i;
		}

		if ( best >= 0 )
		{
			var kept = spare[best];
			spare.RemoveAt( best );
			return kept;
		}

		var vk = VkDevice.vk;
		var device = VkDevice.device;

		var info = new BufferCreateInfo
		{
			SType = StructureType.BufferCreateInfo,
			Size = size,
			Usage = BufferUsageFlags.TransferDstBit,
			SharingMode = SharingMode.Exclusive,
		};

		VkBuffer staging;

		if ( vk.CreateBuffer( device, &info, null, &staging ) != Result.Success )
		{
			VkDevice.SetText( "no buffer to read a buffer back through" );
			return null;
		}

		MemoryRequirements requirements;
		vk.GetBufferMemoryRequirements( device, staging, &requirements );

		//Cached where the device has it: the host reads every byte of this, and reading uncached memory is a trip across the bus a word at a time.
		var type = VkDevice.FindMemoryType( requirements.MemoryTypeBits,
			MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit | MemoryPropertyFlags.HostCachedBit );

		if ( type == uint.MaxValue )
		{
			type = VkDevice.FindMemoryType( requirements.MemoryTypeBits,
				MemoryPropertyFlags.HostVisibleBit | MemoryPropertyFlags.HostCoherentBit );
		}

		var allocate = new MemoryAllocateInfo
		{
			SType = StructureType.MemoryAllocateInfo,
			AllocationSize = requirements.Size,
			MemoryTypeIndex = type,
		};

		DeviceMemory memory = default;
		void* mapped = null;

		if ( type == uint.MaxValue ||
			vk.AllocateMemory( device, &allocate, null, &memory ) != Result.Success ||
			vk.BindBufferMemory( device, staging, memory, 0 ) != Result.Success ||
			vk.MapMemory( device, memory, 0, Vk.WholeSize, 0, &mapped ) != Result.Success )
		{
			if ( memory.Handle != 0 ) vk.FreeMemory( device, memory, null );
			vk.DestroyBuffer( device, staging, null );

			VkDevice.SetText( "no host memory to read a buffer back into" );
			return null;
		}

		return new Request
		{
			Staging = staging,
			Memory = memory,
			Mapped = (byte*)mapped,
			Capacity = size,
		};
	}

	//The copies asked for so far, recorded into a recording about to end - outside its passes, after everything else it does. The render lock is held.
	internal static void RecordInto( VkFrame.Frame frame )
	{
		if ( requests.Count == 0 || frame is null || frame.Commands.Handle == 0 ) return;

		var recorded = false;

		foreach ( var request in requests.Values )
		{
			if ( request.Stage != Stage.Asked ) continue;

			var source = VkBuffers.Get( request.Buffer );

			if ( source.Handle == 0 )
			{
				request.Stage = Stage.Failed;
				continue;
			}

			//Whatever wrote the buffer this frame - a dispatch, a copy, a fill - is finished with it before the copy reads, once, for all of them.
			if ( !recorded )
			{
				var written = new MemoryBarrier2
				{
					SType = StructureType.MemoryBarrier2,
					SrcStageMask = PipelineStageFlags2.AllCommandsBit,
					SrcAccessMask = AccessFlags2.MemoryWriteBit,
					DstStageMask = PipelineStageFlags2.AllTransferBit,
					DstAccessMask = AccessFlags2.TransferReadBit,
				};

				var before = new DependencyInfo
				{
					SType = StructureType.DependencyInfo,
					MemoryBarrierCount = 1,
					PMemoryBarriers = &written,
				};

				VkDevice.dialect.Barrier( frame.Commands, &before );
			}

			var copy = new BufferCopy { SrcOffset = request.Offset, DstOffset = 0, Size = request.Size };

			VkDevice.vk.CmdCopyBuffer( frame.Commands, source, request.Staging, 1, &copy );

			request.Stage = Stage.Recorded;
			recorded = true;
		}

		if ( !recorded ) return;

		//So the host may read what the copies wrote once the fence says so.
		var barrier = new MemoryBarrier2
		{
			SType = StructureType.MemoryBarrier2,
			SrcStageMask = PipelineStageFlags2.AllTransferBit,
			SrcAccessMask = AccessFlags2.TransferWriteBit,
			DstStageMask = PipelineStageFlags2.HostBit,
			DstAccessMask = AccessFlags2.HostReadBit,
		};

		var dependency = new DependencyInfo
		{
			SType = StructureType.DependencyInfo,
			MemoryBarrierCount = 1,
			PMemoryBarriers = &barrier,
		};

		VkDevice.dialect.Barrier( frame.Commands, &dependency );
	}

	//The recording they went into was submitted as this ticket.
	internal static void Submitted( ulong ticket )
	{
		foreach ( var request in requests.Values )
		{
			if ( request.Stage != Stage.Recorded ) continue;

			request.Ticket = ticket;
			request.Stage = Stage.Submitted;
		}
	}

	//The recording they went into never reached the queue: they are asked again.
	internal static void Abandoned()
	{
		foreach ( var request in requests.Values )
		{
			if ( request.Stage == Stage.Recorded ) request.Stage = Stage.Asked;
		}
	}

	//Whether a request has arrived: one with data pointing at its bytes until Release, nought while it is still on its way, minus
	//one for one that never will - no such request, or its buffer went. recorded says whether it has gone into a recording yet.
	internal static int Poll( int id, out byte* data, out int size, out bool recorded )
	{
		data = null;
		size = 0;
		recorded = false;

		lock ( VkDevice.renderLock )
		{
			if ( !requests.TryGetValue( id, out var request ) || request.Stage == Stage.Failed ) return -1;

			recorded = request.Stage != Stage.Asked;

			if ( request.Stage != Stage.Submitted || request.Ticket > VkDevice.CompletedTicket ) return 0;

			data = request.Mapped;
			size = (int)request.Size;

			return 1;
		}
	}

	//Done with a request. Its staging is kept for the next where the card is provably done with it, and handed to the graveyard where it may not be.
	internal static void Release( int id )
	{
		lock ( VkDevice.renderLock )
		{
			if ( !requests.Remove( id, out var request ) ) return;

			var inFlight = request.Stage == Stage.Recorded ||
				( request.Stage == Stage.Submitted && request.Ticket > VkDevice.CompletedTicket );

			if ( inFlight )
			{
				VkDevice.vk.UnmapMemory( VkDevice.device, request.Memory );
				VkDevice.RetireBuffer( request.Staging, request.Memory );
				return;
			}

			if ( spare.Count < SpareKept && request.Capacity <= SpareLargest )
			{
				spare.Add( request );
				return;
			}

			Destroy( request );
		}
	}

	private static void Destroy( Request request )
	{
		var vk = VkDevice.vk;
		var device = VkDevice.device;

		if ( request.Mapped is not null ) vk.UnmapMemory( device, request.Memory );
		if ( request.Staging.Handle != 0 ) vk.DestroyBuffer( device, request.Staging, null );
		if ( request.Memory.Handle != 0 ) vk.FreeMemory( device, request.Memory, null );

		request.Mapped = null;
		request.Staging = default;
		request.Memory = default;
	}

	//Everything, with the device - which has been waited for by then.
	internal static void DestroyAll()
	{
		foreach ( var request in requests.Values ) Destroy( request );
		foreach ( var request in spare ) Destroy( request );

		requests.Clear();
		spare.Clear();
	}
}
