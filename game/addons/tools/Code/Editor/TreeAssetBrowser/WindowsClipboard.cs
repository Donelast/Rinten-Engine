using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Editor.TreeBrowser;

//Reads the file list (CF_HDROP) that Windows Explorer puts on the clipboard when you
//Copy/Cut files. The engine clipboard only exposes text, so we go straight to the Win32 API.
internal static class WindowsClipboard
{
    private const uint CF_HDROP = 15;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, IntPtr lpszFile, uint cch);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint DragQueryFile(IntPtr hDrop, uint iFile, StringBuilder lpszFile, uint cch);

    public static bool HasFiles()
    {
        try
        {
            return IsClipboardFormatAvailable(CF_HDROP);
        }
        catch
        {
            return false;
        }
    }

    public static List<string> GetFiles()
    {
        var result = new List<string>();

        try
        {
            if (!IsClipboardFormatAvailable(CF_HDROP))
                return result;

            if (!OpenClipboard(IntPtr.Zero))
                return result;

            try
            {
                var hDrop = GetClipboardData(CF_HDROP);
                if (hDrop == IntPtr.Zero)
                    return result;

                uint count = DragQueryFile(hDrop, 0xFFFFFFFF, IntPtr.Zero, 0);
                for (uint i = 0; i < count; i++)
                {
                    uint length = DragQueryFile(hDrop, i, IntPtr.Zero, 0);
                    var sb = new StringBuilder((int)length + 1);
                    if (DragQueryFile(hDrop, i, sb, (uint)sb.Capacity) > 0)
                        result.Add(sb.ToString());
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            //P/Invoke unavailable or clipboard access failed - just paste nothing
        }

        return result;
    }
}
