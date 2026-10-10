using System;

namespace Editor
{
	public partial class FileDialog : Widget
	{
		/// <summary>A dialog for picking a file or a folder. The parent is what it opens over; the window system takes it from there.</summary>
		public FileDialog( Widget parent = null ) : base( parent )
		{
		}

		public bool Execute()
		{
			return ExecuteManaged();
		}

		public string Title
		{
			get => TitleManaged;
			set
			{
				TitleManaged = value;
			}
		}

		public string SelectedFile
		{
			get
			{
				return SelectedFileManaged;
			}
		}

		public string Directory
		{
			get => DirectoryManaged;
			set
			{
				DirectoryManaged = value;
			}
		}

		public string DefaultSuffix
		{
			get => SuffixManaged;
			set
			{
				SuffixManaged = value;
			}
		}

		public List<string> SelectedFiles
		{
			get
			{
				return SelectedFilesManaged;
			}
		}

		public void SetNameFilter( string text )
		{
			SetFilterManaged( text );
		}

		public void SetFindDirectory()
		{
			SetFoldersManaged( true );
		}

		public void SetFindFile()
		{
			SetFoldersManaged( false );
			SetManyManaged( false );
		}

		public void SetFindExistingFile()
		{
			SetFoldersManaged( false );
			SetManyManaged( false );
		}

		public void SetFindExistingFiles()
		{
			SetFoldersManaged( false );
			SetManyManaged( true );
		}

		public void SetModeOpen()
		{
			SetModeManaged( false );
		}

		public void SetModeSave()
		{
			SetModeManaged( true );
		}

		public void SelectFile( string file )
		{
			SelectedFileManaged = file;
		}

		internal enum ViewMode { Detail, List }
		internal enum FileMode { AnyFile, ExistingFile, Directory, ExistingFiles }
		internal enum AcceptMode { AcceptOpen, AcceptSave }
		internal enum DialogLabel { LookIn, FileName, FileType, Accept, Reject }
		internal enum Option
		{
			ShowDirsOnly = 0x00000001,
			DontResolveSymlinks = 0x00000002,
			DontConfirmOverwrite = 0x00000004,
			DontUseNativeDialog = 0x00000010,
			ReadOnly = 0x00000020,
			HideNameFilterDetails = 0x00000040,
			DontUseCustomDirectoryIcons = 0x00000080
		};

	}
}
