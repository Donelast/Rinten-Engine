using System.IO;

namespace Rinten.Engine;

public static class SystemInfo
{
	static DriveInfo _driveInfo;

	static SystemInfo()
	{
		UpdateDriveInfo();
	}

	//I am worried about this taking like 4 seconds to run or something
	static void UpdateDriveInfo()
	{
		_driveInfo = new DriveInfo( Path.GetPathRoot( System.Environment.CurrentDirectory ) );
		StorageSizeAvailable = _driveInfo.AvailableFreeSpace;
		StorageSizeTotal = _driveInfo.TotalSize;
	}

	/// <summary>Human-readable product name of this system's processor.</summary>
	public static string ProcessorName { get; private set; } = "unset";

	/// <summary>The frequency of this system's processor in GHz.</summary>
	public static float ProcessorFrequency { get; private set; } = 0;

	/// <summary>The number of logical processors in this system.</summary>
	public static float ProcessorCount { get; private set; } = 0;

	/// <summary>Total physical memory available on this machine, in bytes.</summary>
	public static ulong TotalMemory { get; private set; } = 0;

	/// <summary>Human-readable product name of the graphics card in this system.</summary>
	public static string Gpu { get; private set; } = "unset";

	/// <summary>The version number of the graphics card driver.</summary>
	public static string GpuVersion { get; private set; } = "unset";

	/// <summary>Total VRAM on this system's graphics card.</summary>
	public static ulong GpuMemory { get; private set; } = 0;

	internal static void Set( string cpu, ushort processorCount, ulong frequency, ulong memory )
	{
		ProcessorName = cpu;
		ProcessorFrequency = frequency / 1000000000.0f; //cycles/sec -> GHz
		ProcessorCount = processorCount;
		TotalMemory = memory;
	}

	internal static void SetGpu( string driver, string version, ulong memory )
	{
		Gpu = driver;
		GpuVersion = version;
		GpuMemory = memory;
	}

	/// <summary>Indicates the amount of available free space on game drive in bytes</summary>
	public static long StorageSizeAvailable { get; private set; }

	/// <summary>Gets the total size of storage space on game drive in bytes</summary>
	public static long StorageSizeTotal { get; private set; }

	//Return as an object, for sending to backends
	internal static object AsObject()
	{
		return new
		{
			SystemInfo.ProcessorName,
			SystemInfo.ProcessorCount,
			SystemInfo.ProcessorFrequency,
			SystemInfo.Gpu,
			SystemInfo.GpuVersion,
			GpuMb = SystemInfo.GpuMemory / (1024 * 1024),
			RamMb = SystemInfo.TotalMemory / (1024 * 1024),
			StorageSizeAvailable,
			StorageSizeTotal
		};
	}
}
