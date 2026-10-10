using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor;

/// <summary>Registry of open resource documents, one per type and path, shared by views through leases</summary>
public static class ResourceDocuments
{
	sealed class Entry
	{
		public Type Type;
		public string Path;
		public IResourceDocument Document;
		public int Leases;
	}

	static readonly List<Entry> _open = new();

	/// <summary>The document of this kind for a file, opened now or shared with whoever opened it already. Dispose the lease when the view is done with it.</summary>
	public static DocumentLease<TDocument> Acquire<TDocument>( Asset asset )
		where TDocument : class, IResourceDocument, new()
	{
		ArgumentNullException.ThrowIfNull( asset );

		var path = Key( asset );
		var entry = _open.FirstOrDefault( x => x.Type == typeof( TDocument ) && x.Path == path );

		if ( entry is null )
		{
			var document = new TDocument();
			document.Open( asset );

			entry = new Entry { Type = typeof( TDocument ), Path = path, Document = document };
			_open.Add( entry );
		}

		entry.Leases++;

		return new DocumentLease<TDocument>( (TDocument)entry.Document, () => Release( entry ) );
	}

	/// <summary>The documents of one kind open right now, for anything asking which files are being edited.</summary>
	public static IEnumerable<TDocument> Open<TDocument>() where TDocument : class, IResourceDocument
		=> _open.Where( x => x.Type == typeof( TDocument ) ).Select( x => (TDocument)x.Document );

	/// <summary>The document of this kind holding a file, or null where nothing has it open.</summary>
	public static TDocument Holding<TDocument>( Asset asset ) where TDocument : class, IResourceDocument
	{
		if ( asset is null ) return null;

		var path = Key( asset );

		return _open.FirstOrDefault( x => x.Type == typeof( TDocument ) && x.Path == path )?.Document as TDocument;
	}

	static void Release( Entry entry )
	{
		if ( --entry.Leases > 0 ) return;

		_open.Remove( entry );
		entry.Document.Close();
	}

	//The edits of every open document go to the disk at the end of the frame they were made in.
	[EditorEvent.Frame]
	static void WriteEdits()
	{
		//Over a copy: a write can compile the asset, and whatever that wakes up may open or let go of a document while this walks the list.
		foreach ( var entry in _open.ToArray() )
			entry.Document.WriteEdits();
	}

	//One file however its path is spelt.
	static string Key( Asset asset )
		=> ( asset.Path ?? asset.AbsolutePath ?? "" ).Replace( '\\', '/' ).ToLowerInvariant();
}

/// <summary>A view's hold on a shared document - see <see cref="ResourceDocuments"/>. Disposing it is how the view says it has finished; the document itself is closed by the registry once nobody holds it.</summary>
public sealed class DocumentLease<TDocument> : IDisposable
	where TDocument : class, IResourceDocument
{
	Action _release;

	public TDocument Document { get; }

	internal DocumentLease( TDocument document, Action release )
	{
		Document = document;
		_release = release;
	}

	public void Dispose()
	{
		var release = _release;
		_release = null;
		release?.Invoke();
	}
}
