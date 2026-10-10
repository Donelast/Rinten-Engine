using Editor;
using Rinten;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Editor.TreeBrowser;

/// <summary>Helper class for creating assets in the tree browser. Based on the built-in CreateAsset functionality.</summary>
public static class AssetCreator
{
    public struct Entry
    {
        public string Name { get; init; }
        public string Icon { get; init; }
        public Pixmap IconImage { get; init; }
        public string Category { get; init; }
        public bool Pinned { get; init; }
        public Action<DirectoryInfo> Action { get; init; }
        public string Default { get; init; }

        public void Execute(DirectoryInfo folder)
        {
            if (Action is not null)
            {
                Action(folder);
                return;
            }

            if (!string.IsNullOrEmpty(Default))
            {
                CreateFromTemplate(Name, Default, folder, Icon, IconImage);
            }
        }
    }

    public static void AddOptions(Menu parent, string folderPath)
    {
        var folder = new DirectoryInfo(folderPath);
        var locationType = GetLocationType(folderPath);

        parent.AddOption("Folder", "folder", () =>
        {
            ShowNameDialog("New Folder", "New Folder", "folder", null, (name) =>
            {
                var newFolderPath = Path.Combine(folderPath, name);
                if (Directory.Exists(newFolderPath))
                {
                    Log.Warning($"Folder already exists: {newFolderPath}");
                    return;
                }
                Directory.CreateDirectory(newFolderPath);
            });
        });

        parent.AddSeparator();

        var gameResources = EditorTypeLibrary.GetAttributes<AssetTypeAttribute>().Select(
            x => new Entry()
            {
                Name = x.Name,
                Category = x.Category,
                IconImage = AssetType.FromType(x.TargetType)?.Icon64,
                Action = (DirectoryInfo d) => CreateGameResource(x, d),
                Pinned = x.Extension is "sound" or "prefab" or "scene" or "mat" or "mdl"
            }
        );

        var entries = new List<Entry>();

        if (locationType == LocationType.Code)
        {
            entries.Add(new Entry { Name = "Empty C# File", Icon = "description", Default = "default.cs", Category = "Code", Pinned = true });
            entries.Add(new Entry { Name = "Component", Icon = "sports_esports", Default = "component.cs", Category = "Code", Pinned = true });
            entries.Add(new Entry { Name = "Panel Component", Icon = "desktop_windows", Default = "default.razor", Category = "Razor" });
            entries.Add(new Entry { Name = "Style Sheet", Icon = "brush", Default = "default.scss", Category = "Razor" });
        }
        else if (locationType == LocationType.Assets)
        {
            //Materials and models are game resources of ours - .mat and .mdl - and come in with the rest of
            //them, pinned above. No .vmap either: a level is a prefab here, and prefabs are in the list already.
            entries.Add(new Entry { Name = "Standard Material Shader", Icon = "brush", Default = "material.shader", Category = "Shader" });
            entries.Add(new Entry { Name = "Unlit Shader", Icon = "brush", Default = "unlit.shader", Category = "Shader" });
            entries.Add(new Entry { Name = "Compute Shader", Icon = "brush", Default = "compute.shader", Category = "Shader" });

            entries.AddRange(gameResources);
        }

        //Add pinned entries first
        foreach (var entry in entries.Where(x => x.Pinned).OrderBy(x => x.Name))
        {
            if (entry.IconImage != null)
                parent.AddOptionWithImage(entry.Name, entry.IconImage, () => entry.Execute(folder));
            else
                parent.AddOption(entry.Name, entry.Icon, () => entry.Execute(folder));
        }

        parent.AddSeparator();

        //Group remaining by category
        var grouped = entries.OrderBy(x => x.Name).GroupBy(x => x.Category).OrderBy(x => x.Key);
        foreach (var group in grouped.Where(x => x.Key is not null))
        {
            var menu = parent.FindOrCreateMenu(group.Key);

            foreach (var entry in group)
            {
                if (entry.IconImage != null)
                    menu.AddOptionWithImage(entry.Name, entry.IconImage, () => entry.Execute(folder));
                else
                    menu.AddOption(entry.Name, entry.Icon, () => entry.Execute(folder));
            }
        }
    }

    private enum LocationType
    {
        Unknown,
        Assets,
        Code,
        Localization
    }

    private static LocationType GetLocationType(string path)
    {
        var assetsPath = Project.Current?.GetAssetsPath();
        var codePath = Project.Current?.GetCodePath();

        if (!string.IsNullOrEmpty(assetsPath) && path.StartsWith(assetsPath, StringComparison.OrdinalIgnoreCase))
            return LocationType.Assets;

        if (!string.IsNullOrEmpty(codePath) && path.StartsWith(codePath, StringComparison.OrdinalIgnoreCase))
            return LocationType.Code;

        return LocationType.Unknown;
    }

    private static string GetNewFilename(DirectoryInfo folder, string typeName, string extension)
    {
        typeName = typeName.ToLower();
        string destName = $"new {typeName}{extension}";

        int i = 1;
        while (File.Exists(Path.Combine(folder.FullName, destName)))
        {
            destName = $"new {typeName} {i++}{extension}";
        }

        return destName;
    }

    private static void CreateFromTemplate(string name, string defaultFile, DirectoryInfo folder,
        string icon = null, Pixmap image = null)
    {
        var extension = Path.GetExtension(defaultFile);
        var defaultName = $"new {name.ToLower()}";

        ShowNameDialog($"Create {name}", defaultName, icon, image, (chosenName) =>
        {
            var sourceFile = global::Editor.FileSystem.Root.GetFullPath($"/templates/{defaultFile}");
            if (!File.Exists(sourceFile))
            {
                Log.Error($"Can't create asset! Missing template: {defaultFile}");
                return;
            }

            //Ensure correct extension
            if (!chosenName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                chosenName += extension;
            }

            string destPath = Path.Combine(folder.FullName, chosenName);
            if (File.Exists(destPath))
            {
                Log.Warning($"File already exists: {destPath}");
                return;
            }

            File.Copy(sourceFile, destPath);
            AssetSystem.RegisterFile(destPath);
        });
    }

    public static void CreateGameResource(AssetTypeAttribute gameResource, DirectoryInfo folder)
    {
        int slash = gameResource.Name.LastIndexOf('/');
        string name = slash == -1 ? gameResource.Name : gameResource.Name.Substring(slash + 1, gameResource.Name.Length - slash - 1);
        var extension = $".{gameResource.Extension}";
        var defaultName = $"new {name.ToLower()}";
        var image = AssetType.FromType(gameResource.TargetType)?.Icon64;

        ShowNameDialog($"Create {name}", defaultName, null, image, (chosenName) =>
        {
            //Ensure correct extension
            if (!chosenName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                chosenName += extension;
            }

            string destPath = Path.Combine(folder.FullName, chosenName);
            if (File.Exists(destPath))
            {
                Log.Warning($"File already exists: {destPath}");
                return;
            }

            AssetSystem.CreateResource(gameResource.Extension, destPath);
        });
    }

    //Show a dialog asking the user for a name before creating a file/folder. The icon comes along because the menu this was picked from closes
    //the moment it is picked from: without it the only thing left on screen is a line of text, and thirty entries all lead to the same one.
    private static void ShowNameDialog(string title, string defaultName, string icon, Pixmap image,
        Action<string> onCreate)
    {
        EditorModal.Prompt(null, title, null, name =>
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            onCreate(name);
        }, okay: "Create", initial: defaultName, icon: icon ?? "note_add", image: image);
    }
}
