using NativeEngine;
using System.Runtime.InteropServices;

namespace Rinten;

public enum AppSystemFlags
{
	None = 0,
	IsConsoleApp = 1 << 0,
	IsGameApp = 1 << 1,
	IsDedicatedServer = 1 << 2,
	IsStandaloneGame = 1 << 3,
	IsEditor = 1 << 4,
	IsUnitTest = 1 << 5,
}

public struct AppSystemCreateInfo
{
	public AppSystemFlags Flags;
	public string WindowTitle;
}
