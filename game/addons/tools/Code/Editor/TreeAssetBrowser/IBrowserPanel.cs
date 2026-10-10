using System;
using Rinten.UI;

namespace Editor.TreeBrowser;

/// <summary>Common interface for tree-browser panel types.</summary>
public interface IBrowserPanel
{
    /// <summary>Panel root used for layout and visibility.</summary>
    Panel Root { get; }
    /// <summary>Callback when user requests to close this panel</summary>
    Action OnCloseRequested { get; set; }

    /// <summary>Callback when user requests to move this panel left</summary>
    Action OnMoveLeftRequested { get; set; }

    /// <summary>Callback when user requests to move this panel right</summary>
    Action OnMoveRightRequested { get; set; }

    /// <summary>Controls visibility of close button (hide if this is the only panel)</summary>
    bool ShowCloseButton { get; set; }

    /// <summary>Controls visibility of move left button</summary>
    bool ShowMoveLeftButton { get; set; }

    /// <summary>Controls visibility of move right button</summary>
    bool ShowMoveRightButton { get; set; }

    //There is one search bar, at the top of the browser, and it drives every panel. Each used to carry its own, which cost a row of height per panel and left you typing the same thing twice to find one file.
    /// <summary>Show only what this text matches. Empty means show everything.</summary>
    void ApplySearch(string text);

    /// <summary>Reload what this panel is showing.</summary>
    void Refresh();

    /// <summary>Re-apply the browser's asset type filter after the user changed it.</summary>
    void FilterChanged();

    /// <summary>Switch between project content and mounted games.</summary>
    void SetScope(AssetTreeScope scope);

    /// <summary>Show this section of the content. A panel with nothing to show per section - a grid, which follows wherever the tree was pointed - does nothing with it.</summary>
    void SetSection( AssetTreeSection section ) { }

    /// <summary>Opens the folder a file is in and picks it out, where this panel can.</summary>
    void Reveal(string fullPath);
}
