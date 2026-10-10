using System.Collections.Generic;
using System.Linq;

namespace Rinten;

/// <summary>Writing scene meshes out as an FBX file, for Blender or anything else that takes one.</summary>
public static class FbxExport
{
	/// <summary>Writes <paramref name="meshes"/> into a new FBX file at <paramref name="path"/>, replacing any that is there.</summary>
	public static int SaveMeshes( string path, IEnumerable<MeshComponent> meshes )
	{
		var document = Document( meshes, out var written );

		FbxBinary.Save( path, document );

		return written;
	}

	//What a material is called in the file: the path it is found at.
	internal static string MaterialName( Material material )
	{
		if ( material is null ) return "default";
		if ( !string.IsNullOrEmpty( material.ResourcePath ) ) return material.ResourcePath;
		if ( !string.IsNullOrEmpty( material.ResourceName ) ) return material.ResourceName;

		return "default";
	}

	private static List<FbxNode> Document( IEnumerable<MeshComponent> meshes, out int written )
	{
		//Any number will do as long as no two objects share one; 0 is the scene's root.
		var nextId = 1_000_000L;

		var objects = new FbxNode( "Objects" );
		var connections = new FbxNode( "Connections" );
		var materials = new Dictionary<string, long>();

		var parts = new List<(string Name, Vector3 Position, FbxGeometry Geometry)>();

		foreach ( var mesh in meshes )
		{
			if ( !mesh.IsValid() || mesh.Mesh is not { } polygons ) continue;

			var world = mesh.WorldTransform;
			var geometry = new FbxGeometry();

			polygons.WriteFbxGeometry( geometry, world.Rotation, world.Scale );

			if ( geometry.PolygonMaterials.Count == 0 ) continue;

			var name = string.IsNullOrEmpty( mesh.GameObject?.Name ) ? "mesh" : mesh.GameObject.Name;

			parts.Add( (name, world.Position, geometry) );
		}

		var origin = Origin( parts );

		written = parts.Count;

		foreach ( var (name, position, geometry) in parts )
		{
			var modelId = nextId++;
			var geometryId = nextId++;

			AddModel( objects, modelId, name, position - origin );
			AddGeometry( objects, geometryId, name, geometry );

			connections.Add( "C", "OO", modelId, 0L );
			connections.Add( "C", "OO", geometryId, modelId );

			//The order a model's materials are connected in is the order its faces count them in.
			foreach ( var material in geometry.Materials )
			{
				if ( !materials.TryGetValue( material, out var materialId ) )
				{
					materialId = nextId++;
					materials.Add( material, materialId );

					AddMaterial( objects, materialId, material );
				}

				connections.Add( "C", "OO", materialId, modelId );
			}
		}

		var document = new List<FbxNode>
		{
			Header(),
			new( "FileId", FbxBinary.FileId ),
			new( "CreationTime", FbxBinary.CreationTime ),
			new( "Creator", "Rinten" ),
			GlobalSettings(),
			Documents( nextId++ ),
			new( "References" ),
			Definitions( written, materials.Count ),
			objects,
			connections,
		};

		var takes = new FbxNode( "Takes" );
		takes.Add( "Current", "" );
		document.Add( takes );

		return document;
	}

	//File origin: the object's origin for one mesh, the centre of all of them for several
	private static Vector3 Origin( List<(string Name, Vector3 Position, FbxGeometry Geometry)> parts )
	{
		if ( parts.Count == 0 ) return Vector3.Zero;
		if ( parts.Count == 1 ) return parts[0].Position;

		var min = new Vector3( float.MaxValue, float.MaxValue, float.MaxValue );
		var max = new Vector3( float.MinValue, float.MinValue, float.MinValue );

		foreach ( var (_, position, geometry) in parts )
		{
			var points = geometry.Positions;

			for ( var i = 0; i + 2 < points.Count; i += 3 )
			{
				var p = position + new Vector3( (float)points[i], (float)points[i + 1], (float)points[i + 2] );

				min = new Vector3( MathF.Min( min.x, p.x ), MathF.Min( min.y, p.y ), MathF.Min( min.z, p.z ) );
				max = new Vector3( MathF.Max( max.x, p.x ), MathF.Max( max.y, p.y ), MathF.Max( max.z, p.z ) );
			}
		}

		return (min + max) * 0.5f;
	}

	private static FbxNode Header()
	{
		var header = new FbxNode( "FBXHeaderExtension" );
		header.Add( "FBXHeaderVersion", 1003 );
		header.Add( "FBXVersion", (int)FbxBinary.Version );
		header.Add( "EncryptionType", 0 );

		var now = DateTime.Now;
		var stamp = header.Add( "CreationTimeStamp" );
		stamp.Add( "Version", 1000 );
		stamp.Add( "Year", now.Year );
		stamp.Add( "Month", now.Month );
		stamp.Add( "Day", now.Day );
		stamp.Add( "Hour", now.Hour );
		stamp.Add( "Minute", now.Minute );
		stamp.Add( "Second", now.Second );
		stamp.Add( "Millisecond", now.Millisecond );

		header.Add( "Creator", "Rinten" );

		return header;
	}

	//Right-handed, Y up, metres: glTF's basis, which is the one the engine counts in. Up is +Y, the front +Z,
	//across +X - how Maya and Blender's own Y-up exports say it - and a unit is a hundred of FBX's centimetres.
	private static FbxNode GlobalSettings()
	{
		var settings = new FbxNode( "GlobalSettings" );
		settings.Add( "Version", 1000 );

		var p = settings.Add( "Properties70" );
		p.Property( "UpAxis", "int", "Integer", "", 1 );
		p.Property( "UpAxisSign", "int", "Integer", "", 1 );
		p.Property( "FrontAxis", "int", "Integer", "", 2 );
		p.Property( "FrontAxisSign", "int", "Integer", "", 1 );
		p.Property( "CoordAxis", "int", "Integer", "", 0 );
		p.Property( "CoordAxisSign", "int", "Integer", "", 1 );
		p.Property( "OriginalUpAxis", "int", "Integer", "", 1 );
		p.Property( "OriginalUpAxisSign", "int", "Integer", "", 1 );
		p.Property( "UnitScaleFactor", "double", "Number", "", 100.0 );
		p.Property( "OriginalUnitScaleFactor", "double", "Number", "", 100.0 );
		p.Property( "AmbientColor", "ColorRGB", "Color", "", 0.0, 0.0, 0.0 );
		p.Property( "DefaultCamera", "KString", "", "", "Producer Perspective" );
		p.Property( "TimeMode", "enum", "", "", 11 );
		p.Property( "TimeProtocol", "enum", "", "", 2 );
		p.Property( "SnapOnFrameMode", "enum", "", "", 0 );
		p.Property( "TimeSpanStart", "KTime", "Time", "", 0L );
		p.Property( "TimeSpanStop", "KTime", "Time", "", 46186158000L );
		p.Property( "CustomFrameRate", "double", "Number", "", -1.0 );
		p.Property( "CurrentTimeMarker", "int", "Integer", "", -1 );

		return settings;
	}

	private static FbxNode Documents( long id )
	{
		var documents = new FbxNode( "Documents" );
		documents.Add( "Count", 1 );

		var document = documents.Add( "Document", id, "Scene", "Scene" );
		document.Add( "Properties70" );
		document.Add( "RootNode", 0L );

		return documents;
	}

	private static FbxNode Definitions( int meshes, int materials )
	{
		var definitions = new FbxNode( "Definitions" );
		definitions.Add( "Version", 100 );
		definitions.Add( "Count", 1 + meshes * 2 + materials );

		definitions.Add( "ObjectType", "GlobalSettings" ).Add( "Count", 1 );
		definitions.Add( "ObjectType", "Model" ).Add( "Count", meshes );
		definitions.Add( "ObjectType", "Geometry" ).Add( "Count", meshes );
		definitions.Add( "ObjectType", "Material" ).Add( "Count", materials );

		return definitions;
	}

	private static void AddModel( FbxNode objects, long id, string name, Vector3 position )
	{
		var model = objects.Add( "Model", id, $"{name}\0Model", "Mesh" );
		model.Add( "Version", 232 );

		var p = model.Add( "Properties70" );
		p.Property( "Lcl Translation", "Lcl Translation", "", "A", (double)position.x, (double)position.y, (double)position.z );
		p.Property( "DefaultAttributeIndex", "int", "Integer", "", 0 );
		p.Property( "InheritType", "enum", "", "", 1 );

		model.Add( "MultiLayer", 0 );
		model.Add( "MultiTake", 0 );
		model.Add( "Shading", true );
		model.Add( "Culling", "CullingOff" );
	}

	private static void AddGeometry( FbxNode objects, long id, string name, FbxGeometry geometry )
	{
		var node = objects.Add( "Geometry", id, $"{name}\0Geometry", "Mesh" );
		node.Add( "Properties70" );
		node.Add( "GeometryVersion", 124 );
		node.Add( "Vertices", geometry.Positions.ToArray() );
		node.Add( "PolygonVertexIndex", geometry.PolygonVertices.ToArray() );

		var corners = geometry.PolygonVertices.Count;

		var normals = node.Add( "LayerElementNormal", 0 );
		normals.Add( "Version", 101 );
		normals.Add( "Name", "" );
		normals.Add( "MappingInformationType", "ByPolygonVertex" );
		normals.Add( "ReferenceInformationType", "Direct" );
		normals.Add( "Normals", geometry.Normals.ToArray() );

		if ( geometry.HasColors )
		{
			var colors = node.Add( "LayerElementColor", 0 );
			colors.Add( "Version", 101 );
			colors.Add( "Name", "Color" );
			colors.Add( "MappingInformationType", "ByPolygonVertex" );
			colors.Add( "ReferenceInformationType", "IndexToDirect" );
			colors.Add( "Colors", geometry.Colors.ToArray() );
			colors.Add( "ColorIndex", Enumerable.Range( 0, corners ).ToArray() );
		}

		var uvs = node.Add( "LayerElementUV", 0 );
		uvs.Add( "Version", 101 );
		uvs.Add( "Name", "UVMap" );
		uvs.Add( "MappingInformationType", "ByPolygonVertex" );
		uvs.Add( "ReferenceInformationType", "IndexToDirect" );
		uvs.Add( "UV", geometry.Uvs.ToArray() );
		uvs.Add( "UVIndex", Enumerable.Range( 0, corners ).ToArray() );

		var materials = node.Add( "LayerElementMaterial", 0 );
		materials.Add( "Version", 101 );
		materials.Add( "Name", "" );
		materials.Add( "MappingInformationType", "ByPolygon" );
		materials.Add( "ReferenceInformationType", "IndexToDirect" );
		materials.Add( "Materials", geometry.PolygonMaterials.ToArray() );

		var layer = node.Add( "Layer", 0 );
		layer.Add( "Version", 100 );

		AddLayerElement( layer, "LayerElementNormal" );
		if ( geometry.HasColors ) AddLayerElement( layer, "LayerElementColor" );
		AddLayerElement( layer, "LayerElementUV" );
		AddLayerElement( layer, "LayerElementMaterial" );
	}

	private static void AddLayerElement( FbxNode layer, string type )
	{
		var element = layer.Add( "LayerElement" );
		element.Add( "Type", type );
		element.Add( "TypedIndex", 0 );
	}

	private static void AddMaterial( FbxNode objects, long id, string name )
	{
		var material = objects.Add( "Material", id, $"{name}\0Material", "" );
		material.Add( "Version", 102 );
		material.Add( "ShadingModel", "Phong" );
		material.Add( "MultiLayer", 0 );

		var p = material.Add( "Properties70" );
		p.Property( "DiffuseColor", "Color", "", "A", 0.8, 0.8, 0.8 );
		p.Property( "DiffuseFactor", "Number", "", "A", 1.0 );
	}
}

//One mesh as FBX lays it out - see PolygonMesh.WriteFbxGeometry.
internal sealed class FbxGeometry
{
	/// <summary>The points, three to a point.</summary>
	public List<double> Positions { get; } = new();

	/// <summary>Every face's corners as points, each face's last one complemented.</summary>
	public List<int> PolygonVertices { get; } = new();

	/// <summary>A normal for every corner, three to a corner.</summary>
	public List<double> Normals { get; } = new();

	/// <summary>A texture coordinate for every corner, two to a corner.</summary>
	public List<double> Uvs { get; } = new();

	/// <summary>A colour for every corner, four to a corner.</summary>
	public List<double> Colors { get; } = new();

	/// <summary>Whether any corner is painted anything but white; the colours are left out when none is.</summary>
	public bool HasColors { get; set; }

	/// <summary>Every face's material, as a place in <see cref="Materials"/>.</summary>
	public List<int> PolygonMaterials { get; } = new();

	/// <summary>The names of the materials this mesh's faces use, in the order they count them.</summary>
	public List<string> Materials { get; } = new();
}
