using Editor;
using Rinten.Engine;
using Rinten.Internal;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace AddonTests;

//Every example in the Panel Gallery can be built. The gallery lives in the tools addon, which this project cannot reference
//at compile time, so the addon is compiled here the way the editor compiles it and its self-test is reached by reflection.
[TestClass]
public class PanelGalleryTests
{
	const string SelfTestType = "Editor.Gallery.GallerySelfTest";
	const string CatalogType = "Editor.Gallery.GalleryCatalog";

	[TestMethod]
	public async Task EveryGalleryExampleBuilds()
	{
		var assembly = await CompileToolsAddon();

		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;

		var library = GlobalToolsNamespace.EditorTypeLibrary ??= NewTypeLibrary();
		library.AddAssembly( assembly, true );

		var catalog = assembly.GetType( CatalogType );
		Assert.IsNotNull( catalog, $"{CatalogType} is not in the tools addon" );

		var instance = catalog.GetMethod( "CreateDefault", BindingFlags.Public | BindingFlags.Static )!.Invoke( null, null );
		var entries = (System.Collections.ICollection)catalog.GetMethod( "Entries" )!.Invoke( instance, null )!;

		Assert.IsTrue( entries.Count > 50, $"the gallery lists {entries.Count} examples; the attribute or the type library is not being read" );

		var selfTest = assembly.GetType( SelfTestType );
		Assert.IsNotNull( selfTest, $"{SelfTestType} is not in the tools addon" );

		var failures = ((IEnumerable<string>)selfTest.GetMethod( "Failures", BindingFlags.Public | BindingFlags.Static )!.Invoke( null, null )!).ToList();

		Assert.AreEqual( 0, failures.Count, "examples that could not be built:\n" + string.Join( "\n", failures ) );
	}

	//Each gallery example is laid out, ticked and deleted; a progress file names any that crash the host
	[TestMethod]
	public async Task EveryGalleryExampleLaysOutAndTicks()
	{
		var assembly = await CompileToolsAddon();

		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;

		var library = GlobalToolsNamespace.EditorTypeLibrary ??= NewTypeLibrary();
		library.AddAssembly( assembly, true );

		var catalog = assembly.GetType( CatalogType )!;
		var instance = catalog.GetMethod( "CreateDefault", BindingFlags.Public | BindingFlags.Static )!.Invoke( null, null );
		var entries = (System.Collections.IEnumerable)catalog.GetMethod( "Entries" )!.Invoke( instance, null )!;

		var progress = Path.Combine( Path.GetTempPath(), "panelgallery-progress.txt" );
		File.WriteAllText( progress, "" );
		Console.WriteLine( $"progress written to {progress}" );

		var root = new Rinten.UI.RootPanel { PanelBounds = new Rect( 0, 0, 900, 700 ) };
		var failures = new List<string>();

		foreach ( var entry in entries )
		{
			var type = entry.GetType();
			var id = (string)type.GetProperty( "Id" )!.GetValue( entry )!;
			var needsWindow = (bool)type.GetProperty( "RequiresWindow" )!.GetValue( entry )!;

			if ( needsWindow ) continue;

			using ( var log = new StreamWriter( progress, append: true ) )
			{
				log.WriteLine( id );
				log.Flush();
			}

			Rinten.UI.Panel panel = null;

			try
			{
				panel = (Rinten.UI.Panel)type.GetMethod( "Create" )!.Invoke( entry, null )!;
				root.AddChild( panel );

				for ( var frame = 0; frame < 3; frame++ )
				{
					root.TickInternal();
					root.SettleLayout();
				}
			}
			catch ( Exception e )
			{
				var inner = e is TargetInvocationException { InnerException: { } cause } ? cause : e;
				failures.Add( $"{id}: {inner.GetType().Name}: {inner.Message}" );
			}
			finally
			{
				panel?.Delete( true );
			}
		}

		Assert.AreEqual( 0, failures.Count, "examples that failed to lay out or tick:\n" + string.Join( "\n", failures ) );
	}

	//All Property Editors cards ticked together with hover moving per frame, mimicking a scroll
	[TestMethod]
	public async Task PropertyEditorsPageTicksTogether()
	{
		var assembly = await CompileToolsAddon();

		ThreadSafe.MarkMainThread();
		EditorBackend.Panels = true;

		if ( Theme.RowHeight <= 0.0f )
			Theme.RowHeight = 28.0f;

		var library = GlobalToolsNamespace.EditorTypeLibrary ??= NewTypeLibrary();
		library.AddAssembly( assembly, true );

		var catalog = assembly.GetType( CatalogType )!;
		var instance = catalog.GetMethod( "CreateDefault", BindingFlags.Public | BindingFlags.Static )!.Invoke( null, null );
		var entries = (System.Collections.IEnumerable)catalog.GetMethod( "Entries" )!.Invoke( instance, null )!;

		var progress = Path.Combine( Path.GetTempPath(), "panelgallery-page-progress.txt" );
		File.WriteAllText( progress, "" );

		var root = new Rinten.UI.RootPanel { PanelBounds = new Rect( 0, 0, 900, 700 ), IsEditorUi = true };
		var page = root.AddChild<Rinten.UI.Panel>( "cards" );
		page.Style.FlexDirection = Rinten.UI.FlexDirection.Column;

		var cards = new List<(string Id, Rinten.UI.Panel Panel)>();

		foreach ( var entry in entries )
		{
			var type = entry.GetType();
			var id = (string)type.GetProperty( "Id" )!.GetValue( entry )!;

			if ( !id.StartsWith( "property-editors/" ) ) continue;
			if ( (bool)type.GetProperty( "RequiresWindow" )!.GetValue( entry )! ) continue;

			Note( progress, $"build {id}" );

			var panel = (Rinten.UI.Panel)type.GetMethod( "Create" )!.Invoke( entry, null )!;
			page.AddChild( panel );
			cards.Add( (id, panel) );
		}

		Assert.IsTrue( cards.Count > 20, $"only {cards.Count} property editor cards" );

		for ( var frame = 0; frame < cards.Count + 10; frame++ )
		{
			var hovered = cards[frame % cards.Count];

			Note( progress, $"frame {frame} hover {hovered.Id}" );

			foreach ( var (_, panel) in cards )
				SetHover( panel, panel == hovered.Panel );

			root.TickInternal();
			root.SettleLayout();
		}

		root.Delete( true );
	}

	static void Note( string progress, string line )
	{
		using var log = new StreamWriter( progress, append: true );
		log.WriteLine( line );
		log.Flush();
	}

	//The pointer over a card is the pointer over everything in it, down to the leaf.
	static void SetHover( Rinten.UI.Panel panel, bool hovered )
	{
		if ( hovered ) panel.PseudoClass |= Rinten.UI.PseudoClass.Hover;
		else panel.PseudoClass &= ~Rinten.UI.PseudoClass.Hover;

		foreach ( var child in panel.Children.ToArray() )
			SetHover( child, hovered );
	}

	//The tools addon, compiled the way the editor compiles it - over the base addon,
	//which the editor's panels are partly made of - and loaded from its bytes.
	static async Task<Assembly> CompileToolsAddon()
	{
		var basePath = Path.Combine( Environment.CurrentDirectory, "addons", "base", "code" );
		var toolsPath = Path.Combine( Environment.CurrentDirectory, "addons", "tools", "Code" );

		Assert.IsTrue( Directory.Exists( basePath ), basePath );
		Assert.IsTrue( Directory.Exists( toolsPath ), toolsPath );

		using var group = new CompileGroup( "Test" );

		//The base addon first, the way Utility.Projects.Compile adds it under any game: what a game and the editor share - buttons, text entry, forms.
		var baseSettings = new Compiler.Configuration();
		baseSettings.Clean();
		baseSettings.ReleaseMode = Compiler.ReleaseMode.Release;

		var baseCompiler = group.CreateCompiler( "base", basePath, baseSettings );
		baseCompiler.UseAbsoluteSourcePaths = false;
		baseCompiler.GeneratedCode.AppendLine( "global using static Rinten.Internal.GlobalGameNamespace;" );
		baseCompiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components;" );
		baseCompiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components.Rendering;" );

		var toolSettings = new Compiler.Configuration();
		toolSettings.Clean();
		toolSettings.Unsafe = true;

		var toolbase = group.CreateCompiler( "toolbase", toolsPath, toolSettings );
		Reference( toolbase );
		toolbase.AddReference( baseCompiler );

		await group.BuildAsync();

		var errors = string.Join( "\n", group.BuildResult.Diagnostics
			.Where( x => x.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error )
			.Select( x => $"{x.Location?.SourceTree?.FilePath}:{x.Location?.GetLineSpan().StartLinePosition} {x.GetMessage()}" )
			.Take( 30 ) );

		Assert.IsTrue( group.BuildResult.Success, errors );

		var outputs = group.BuildResult.Output.Where( x => x.AssemblyData is { Length: > 0 } ).ToList();
		var bytes = toolbase.Output?.AssemblyData;

		Assert.IsNotNull( bytes, "the compiler produced no assembly for the tools addon" );

		//Anything the addon references that was compiled in the same group - the base addon - is resolved from those bytes too, by name.
		var byName = outputs.ToDictionary( x => x.Compiler.AssemblyName, x => x.AssemblyData, StringComparer.OrdinalIgnoreCase );

		AppDomain.CurrentDomain.AssemblyResolve += ( _, args ) =>
		{
			var name = new AssemblyName( args.Name ).Name;
			return name is not null && byName.TryGetValue( name, out var data ) ? Assembly.Load( data ) : null;
		};

		return Assembly.Load( bytes );
	}

	//What the editor gives every tool project. See Project.Compiling.
	static void Reference( Compiler compiler )
	{
		compiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components;" );
		compiler.GeneratedCode.AppendLine( "global using Microsoft.AspNetCore.Components.Rendering;" );
		compiler.GeneratedCode.AppendLine( "global using static Rinten.Internal.GlobalGameNamespace;" );
		compiler.GeneratedCode.AppendLine( "global using static Rinten.Internal.GlobalToolsNamespace;" );

		foreach ( var reference in new[]
		{
			"Rinten.Tools", "Rinten.Compiling", "Rinten.Bind", "Facepunch.ActionGraphs", "SkiaSharp",
			"Microsoft.CodeAnalysis", "Microsoft.CodeAnalysis.CSharp",
			"System.Diagnostics.Process", "System.Net.WebSockets", "System.Net.WebSockets.Client",
			"Microsoft.Win32.Registry", "System.Memory",
		} )
		{
			compiler.AddReference( reference );
		}
	}

	static TypeLibrary NewTypeLibrary()
	{
		var library = new TypeLibrary();

		library.AddIntrinsicTypes();
		library.AddAssembly( typeof( Vector3 ).Assembly, false );
		library.AddAssembly( typeof( EngineLoop ).Assembly, false );
		library.AddAssembly( typeof( EditorBackend ).Assembly, false );

		return library;
	}
}
