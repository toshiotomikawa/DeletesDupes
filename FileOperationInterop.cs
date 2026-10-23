using System;
using System.Runtime.InteropServices;

namespace DeleteDuplicates
{
    // NOTE: COM method order must match the native vtable exactly. Methods that
    // this program never calls are still declared, with pointer-sized
    // placeholders where the real parameter type is irrelevant here.

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out IntPtr ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [ComImport]
    [Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();

        [PreserveSig] int FinishOperations(int hrResult);

        [PreserveSig] int PreRenameItem(uint dwFlags, IShellItem psiItem,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig] int PostRenameItem(uint dwFlags, IShellItem psiItem,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrRename, IShellItem psiNewlyCreated);

        [PreserveSig] int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig] int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrMove, IShellItem psiNewlyCreated);

        [PreserveSig] int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig] int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrCopy, IShellItem psiNewlyCreated);

        [PreserveSig] int PreDeleteItem(uint dwFlags, IShellItem psiItem);

        [PreserveSig] int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem psiNewlyCreated);

        [PreserveSig] int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig] int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName,
            [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName,
            uint dwFileAttributes, int hrNew, IShellItem psiNewItem);

        [PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);

        [PreserveSig] int ResetTimer();

        [PreserveSig] int PauseTimer();

        [PreserveSig] int ResumeTimer();
    }

    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        [PreserveSig] int Advise(IFileOperationProgressSink pfops, out uint pdwCookie);

        [PreserveSig] int Unadvise(uint dwCookie);

        [PreserveSig] int SetOperationFlags(uint dwOperationFlags);

        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

        [PreserveSig] int SetProgressDialog(IntPtr popd);

        [PreserveSig] int SetProperties(IntPtr pproparray);

        [PreserveSig] int SetOwnerWindow(IntPtr hwndOwner);

        [PreserveSig] int ApplyPropertiesToItem(IShellItem psiItem);

        [PreserveSig] int ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);

        [PreserveSig] int RenameItem(IShellItem psiItem,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink pfopsItem);

        [PreserveSig] int RenameItems([MarshalAs(UnmanagedType.IUnknown)] object pUnkItems,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig] int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink pfopsItem);

        [PreserveSig] int MoveItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems,
            IShellItem psiDestinationFolder);

        [PreserveSig] int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder,
            [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName, IFileOperationProgressSink pfopsItem);

        [PreserveSig] int CopyItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems,
            IShellItem psiDestinationFolder);

        [PreserveSig] int DeleteItem(IShellItem psiItem, IFileOperationProgressSink pfopsItem);

        [PreserveSig] int DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);

        [PreserveSig] int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes,
            [MarshalAs(UnmanagedType.LPWStr)] string pszName,
            [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName,
            IFileOperationProgressSink pfopsItem);

        [PreserveSig] int PerformOperations();

        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    internal static class ShellInterop
    {
        public static readonly Guid CLSID_FileOperation =
            new Guid("3ad05575-8857-4850-9277-11b85bdb8e09");

        public static readonly Guid IID_IShellItem =
            new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        public const uint SIGDN_NORMALDISPLAY = 0x00000000;
        public const uint SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000;
        public const uint SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000;

        public const int S_OK = 0;

        // The out parameter is typed explicitly. Returning a plain object
        // marshalled as UnmanagedType.Interface would make the CLR request
        // IDispatch, which shell items do not implement.
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        public static IShellItem CreateShellItem(string fullPath, out int hresult)
        {
            Guid riid = IID_IShellItem;
            IShellItem item;

            hresult = SHCreateItemFromParsingName(fullPath, IntPtr.Zero, ref riid, out item);

            return hresult < 0 ? null : item;
        }

        public static string GetDisplayName(IShellItem item, uint form)
        {
            if (item == null)
                return null;

            IntPtr buffer;

            if (item.GetDisplayName(form, out buffer) != S_OK || buffer == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
            }
        }
    }

    /// <summary>
    /// Captures the authoritative per-item deletion result. PerformOperations
    /// can succeed while an individual item failed, so hrDelete and the
    /// newly created Recycle Bin item are what actually decide success.
    /// </summary>
    [ClassInterface(ClassInterfaceType.None)]
    [ComVisible(true)]
    internal sealed class DeleteProgressSink : IFileOperationProgressSink
    {
        private const uint RecycleIfPossible = 0x80;
        private const int Abort = unchecked((int)0x80004004);
        private readonly Func<bool> _validate;

        public DeleteProgressSink(Func<bool> validate) { _validate = validate; }

        public bool PostDeleteCalled;
        public int DeleteHResult = unchecked((int)0x80004005); // E_FAIL until told otherwise
        public bool RecycledItemPresent;
        public string RecycleBinDisplayName;

        public int StartOperations() { return ShellInterop.S_OK; }

        public int FinishOperations(int hrResult) { return ShellInterop.S_OK; }

        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName)
        {
            return ShellInterop.S_OK;
        }

        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName,
            int hrRename, IShellItem psiNewlyCreated)
        {
            return ShellInterop.S_OK;
        }

        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder,
            string pszNewName)
        {
            return ShellInterop.S_OK;
        }

        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder,
            string pszNewName, int hrMove, IShellItem psiNewlyCreated)
        {
            return ShellInterop.S_OK;
        }

        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder,
            string pszNewName)
        {
            return ShellInterop.S_OK;
        }

        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder,
            string pszNewName, int hrCopy, IShellItem psiNewlyCreated)
        {
            return ShellInterop.S_OK;
        }

        public int PreDeleteItem(uint dwFlags, IShellItem psiItem)
        {
            // Absence of the recycle flag must cancel BEFORE deletion. The
            // flag is a request, not proof that every Shell provider can recycle.
            if ((dwFlags & RecycleIfPossible) == 0 || _validate == null)
                return Abort;
            try { return _validate() ? ShellInterop.S_OK : Abort; }
            catch (Exception) { return Abort; }
        }

        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem psiNewlyCreated)
        {
            PostDeleteCalled = true;
            DeleteHResult = hrDelete;

            // psiNewlyCreated is the item inside the Recycle Bin. It is null
            // when the shell deleted the file permanently instead.
            RecycledItemPresent = psiNewlyCreated != null;

            if (psiNewlyCreated != null)
            {
                RecycleBinDisplayName =
                    ShellInterop.GetDisplayName(psiNewlyCreated, ShellInterop.SIGDN_DESKTOPABSOLUTEPARSING) ??
                    ShellInterop.GetDisplayName(psiNewlyCreated, ShellInterop.SIGDN_NORMALDISPLAY) ??
                    "Recycle Bin";
            }

            return ShellInterop.S_OK;
        }

        public int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName)
        {
            return ShellInterop.S_OK;
        }

        public int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, string pszNewName,
            string pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem psiNewItem)
        {
            return ShellInterop.S_OK;
        }

        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar) { return ShellInterop.S_OK; }

        public int ResetTimer() { return ShellInterop.S_OK; }

        public int PauseTimer() { return ShellInterop.S_OK; }

        public int ResumeTimer() { return ShellInterop.S_OK; }
    }
}
