using Rinten.Diagnostics;
using Rinten.Engine;
using Rinten.Internal;
using Rinten.Mounting;
using Rinten.UI;
using Rinten.Network;
using Rinten.Rendering;
using System;
using System.Globalization;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Threading.Tasks;

namespace Rinten;

public class AppSystem
{
	protected Logger log = new Logger( "AppSystem" );

	//For the runs that are a build step rather than a game - baking shaders, a headless test - where whoever started this has to be able to tell a failure from a success without reading the log.
	/// <summary>What the process answers with when it ends. Zero unless something set it.</summary>
	public int ExitCode { get; protected set; }

	[DllImport( "user32.dll", CharSet = CharSet.Unicode )]
	private static extern int MessageBox( IntPtr hWnd, string text, string caption, uint type );

	/// <summary>We should check all the system requirements here as early as possible.</summary>
	public void TestSystemRequirements()
	{
		if ( !OperatingSystem.IsWindows() )
			return;

		//AVX is on any sane CPU since 2011
		if ( !Avx.IsSupported )
		{
			MessageBox( IntPtr.Zero, "Your CPU needs to support AVX instructions to run this game.", "Unsupported CPU", 0x10 );
			Environment.Exit( 1 );
		}

		//check core count, ram, os? rendersystemvulkan ends up checking gpu, driver, vram later on

	}

	public virtual void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		try
		{
			NetCore.InitializeInterop( Environment.CurrentDirectory );
		}
		catch
		{
			if ( OperatingSystem.IsWindows() )
			{
				MissingDependancyDiagnosis.Run();
			}
			throw;
		}
	}

	void SetupEnvironment()
	{
		CultureInfo culture = (CultureInfo)CultureInfo.CurrentCulture.Clone();

		//force GregorianCalendar, because that's how we're going to be parsing dates etc
		if ( culture.DateTimeFormat.Calendar is not GregorianCalendar )
		{
			culture.DateTimeFormat.Calendar = new GregorianCalendar();
		}

		CultureInfo.DefaultThreadCurrentCulture = culture;
		CultureInfo.DefaultThreadCurrentUICulture = culture;
	}

	//Create the Game (Rinten.GameInstance)
	protected void CreateGame()
	{
		ManagedAssemblyDiagnosis.ProbeGameInstance();

		//Install the compiler if this build has one (not shipped games); caught here since loading may fail
		try
		{
			InstallCompiler();
		}
		catch ( System.IO.FileNotFoundException )
		{
			//Not in this build.
		}

		GameInstanceDll.Create();
	}

	//Never inlined: it names Rinten.Code, which a shipped game has not got, and its caller has to be compiled and running to catch that.
	[System.Runtime.CompilerServices.MethodImpl( System.Runtime.CompilerServices.MethodImplOptions.NoInlining )]
	private static void InstallCompiler() => RintenCode.Install();

	//Create the editor (Rinten.Tools)
	protected void CreateEditor()
	{
		Editor.AssemblyInitialize.Initialize();
	}

	public void Run()
	{
		try
		{
			SetupEnvironment();

			Application.TryLoadVersionInfo( Environment.CurrentDirectory );

			Init();

			if ( OperatingSystem.IsBrowser() )
			{
				log.Error( "the browser host must call RunAsync" );
				return;
			}

			//The native frame counter, where there is one. A process without one keeps its own count and tells nobody.

			//Made once, out here: a lambda that captures this is a new delegate each time it is evaluated, and inside the loop that was every frame.
			Func<bool> oneFrame = RunFrame;
			Action pumpFrame = () => FrameGuard.Survive( oneFrame );

			while ( FrameGuard.Survive( oneFrame ) )
			{
				BlockingLoopPumper.Run( pumpFrame );
			}

			Shutdown();
		}
		catch ( System.Exception e )
		{
			//Into the log before anything else: the console belongs to whoever started this, and a player started it by clicking a picture.
			try { log.Error( e, "the run ended here" ); } catch { }

			Console.WriteLine( $"Error: ({e.GetType()}) {e.Message}" );
			Console.WriteLine( e.StackTrace );

			CrashShutdown( $"{e.GetType()}: {e.Message}\n\n{e.StackTrace}" );
		}
	}

	/// <summary>The same loop as <see cref="Run"/>, yielding each frame to the browser.</summary>
	public async Task RunAsync()
	{
		try
		{
			SetupEnvironment();

			Application.TryLoadVersionInfo( Environment.CurrentDirectory );

			Init();

			Func<bool> oneFrame = RunFrame;

			while ( FrameGuard.Survive( oneFrame ) )
			{
				if ( OperatingSystem.IsBrowser() )
				{
					var next = WebPage.NextFrame;
					if ( next is not null ) await next();
					else await Task.Delay( 16 );
					continue;
				}

				BlockingLoopPumper.Run( () => FrameGuard.Survive( oneFrame ) );
			}

			Shutdown();
		}
		catch ( System.Exception e )
		{
			try { log.Error( e, "the run ended here" ); } catch { }

			Console.WriteLine( $"Error: ({e.GetType()}) {e.Message}" );
			Console.WriteLine( e.StackTrace );

			CrashShutdown( $"{e.GetType()}: {e.Message}\n\n{e.StackTrace}" );
		}
	}

	protected virtual bool RunFrame()
	{
		//A frame of the engine, for whoever is running one. Ours is in CoreAppSystem, which is what every launcher starts.
		return false;
	}

	//Fatal startup shutdown, GPU first; editor shows a box, shipped games log and exit quietly
	private void CrashShutdown( string message = null )
	{
		try { CoreUiWindow.DropDisplay(); } catch { }

		if ( Application.IsEditor && !Application.IsHeadless && !string.IsNullOrEmpty( message ) )
		{
			try { EnginePlatform.MessageBox( "Engine error", message ); } catch { }
		}

		try { ConVarSystem.SaveAll(); } catch { }
		try { Api.Shutdown(); } catch { }
		try { NLog.LogManager.Shutdown(); } catch { }

		Environment.Exit( 1 );
	}

	public virtual void Shutdown()
	{
		IGameInstanceDll.Current?.CloseGame();

		var exit = new Api.Events.EventRecord( "Exit" );
		exit.SetValue( "uptime", RealTime.Now );
		exit.Submit();

		ConVarSystem.SaveAll();

		IToolsDll.Current?.Exiting();
		IMenuDll.Current?.Exiting();
		IGameInstanceDll.Current?.Exiting();

		Api.Shutdown();

		SoundFile.Shutdown();
		SoundHandle.Shutdown();
		SceneWorld.FlushQueuedDeletes();
		InputRouter.Shutdown();
		Logging.ClearListeners();
		MountUtility.FlushCache();
		PackageManager.UnmountAll();
		StyleSheet.ResetStyleSheets();

		var engineAsmName = typeof( Texture ).Assembly.GetName().Name;

		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
		{
			var isEngine = assembly.GetName().Name == engineAsmName;
			var isDynamic = assembly.IsDynamic;
			var referencesEngine = !isDynamic && assembly.GetReferencedAssemblies().Any( r => r.Name == engineAsmName );

			if ( !isEngine && !isDynamic && !referencesEngine )
				continue;

			ReflectionUtility.NullStaticReferencesOfType( assembly, typeof( Texture ) );
			ReflectionUtility.NullStaticReferencesOfType( assembly, typeof( Model ) );
			ReflectionUtility.NullStaticReferencesOfType( assembly, typeof( Material ) );
			ReflectionUtility.NullStaticReferencesOfType( assembly, typeof( ComputeShader ) );
			ReflectionUtility.NullStaticReferencesOfType( assembly, typeof( CommandList ) );
			ReflectionUtility.NullStaticReferencesOfType( assembly, typeof( Panel ) );
		}

		Material.Shutdown();
		TextRendering.Shutdown();
		NativeResourceCache.ClearCache();
		RenderTarget.Shutdown();
		RenderAttributes.Pool.Clear();

		GlobalContext.Menu.Shutdown();
		GlobalContext.Game.Shutdown();

		FontManager.Instance.Clear( removeMenu: true );

		EngineLoop.DrainFrameEndDisposables();
		MainThread.RunQueues();
		GC.Collect();
		GC.WaitForPendingFinalizers();

		EngineLoop.DrainFrameEndDisposables();
		MainThread.RunQueues();
		GC.Collect();
		GC.WaitForPendingFinalizers();

		NativeResourceCache.HandleShutdownLeaks();

		foreach ( var scene in Scene.All )
		{
			log.Warning( $"Leaked scene {scene.Id} during shutdown." );
		}

		//Shutting the native engine down, where it is the one that was started. A process that never started it has no native window to close,
		//no application object to destroy, and no native crash handler to stop - and every one of these would be a call into nothing.

		EngineFileSystem.Shutdown();
		Application.Shutdown();
	}

	protected virtual void SetWindowTitle( string title )
	{
	}
}
