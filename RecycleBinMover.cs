using System;
using System.Runtime.InteropServices;

namespace DeleteDuplicates
{
    internal sealed class RecycleResult
    {
        public bool Succeeded;
        public int HResult;
        public string Destination;
        public string Message;

        public static RecycleResult Success(string destination)
        {
            return new RecycleResult
            {
                Succeeded = true,
                HResult = ShellInterop.S_OK,
                Destination = string.IsNullOrEmpty(destination) ? "Recycle Bin" : destination
            };
        }

        public static RecycleResult Failure(int hresult, string message)
        {
            return new RecycleResult
            {
                Succeeded = false,
                HResult = hresult,
                Message = message
            };
        }
    }

    /// <summary>
    /// Moves one file at a time to the Recycle Bin through IFileOperation.
    /// One operation per file keeps the deletion order observable and gives a
    /// single unambiguous result per path.
    /// </summary>
    internal static class RecycleBinMover
    {
        private const uint FOF_SILENT = 0x0004;
        private const uint FOF_NOCONFIRMATION = 0x0010;
        private const uint FOF_NOERRORUI = 0x0400;
        private const uint FOF_NO_CONNECTED_ELEMENTS = 0x2000;

        private const uint FOFX_RECYCLEONDELETE = 0x00080000;
        private const uint FOFX_EARLYFAILURE = 0x00100000;
        private const uint FOFX_ADDUNDORECORD = 0x20000000;

        public const uint DeleteFlags =
            FOF_SILENT |
            FOF_NOCONFIRMATION |
            FOF_NOERRORUI |
            FOF_NO_CONNECTED_ELEMENTS |
            FOFX_RECYCLEONDELETE |
            FOFX_EARLYFAILURE |
            FOFX_ADDUNDORECORD;

        public static RecycleResult RecycleOne(string fullPath, Func<bool> validateBeforeDelete)
        {
            IFileOperation operation = null;
            IShellItem item = null;

            try
            {
                Type type = Type.GetTypeFromCLSID(ShellInterop.CLSID_FileOperation);

                if (type == null)
                    return RecycleResult.Failure(unchecked((int)0x80040154), "CLSID_FileOperation is unavailable");

                operation = (IFileOperation)Activator.CreateInstance(type);

                int hr = operation.SetOperationFlags(DeleteFlags);

                if (hr < 0)
                    return RecycleResult.Failure(hr, "SetOperationFlags failed");

                hr = operation.SetOwnerWindow(IntPtr.Zero);

                if (hr < 0)
                    return RecycleResult.Failure(hr, "SetOwnerWindow failed");

                int itemHr;

                try
                {
                    item = ShellInterop.CreateShellItem(fullPath, out itemHr);
                }
                catch (Exception exception)
                {
                    return RecycleResult.Failure(Marshal.GetHRForException(exception),
                        "SHCreateItemFromParsingName failed: " + exception.Message);
                }

                if (item == null)
                {
                    return RecycleResult.Failure(itemHr,
                        "SHCreateItemFromParsingName failed: " +
                        new System.ComponentModel.Win32Exception(itemHr).Message);
                }

                var sink = new DeleteProgressSink(validateBeforeDelete);

                hr = operation.DeleteItem(item, sink);

                if (hr < 0)
                    return RecycleResult.Failure(hr, "DeleteItem could not be queued");

                int performHr = operation.PerformOperations();

                bool aborted;
                int abortedHr = operation.GetAnyOperationsAborted(out aborted);

                if (performHr < 0)
                    return RecycleResult.Failure(performHr, "PerformOperations failed");

                if (abortedHr < 0)
                    return RecycleResult.Failure(abortedHr, "GetAnyOperationsAborted failed");

                if (aborted)
                {
                    return RecycleResult.Failure(sink.DeleteHResult,
                        "The shell reported that the operation was aborted");
                }

                if (!sink.PostDeleteCalled)
                {
                    return RecycleResult.Failure(unchecked((int)0x80004005),
                        "The shell never reported a delete result for this item");
                }

                if (sink.DeleteHResult < 0)
                    return RecycleResult.Failure(sink.DeleteHResult, "Deletion failed");

                if (!sink.RecycledItemPresent)
                {
                    // No Recycle Bin item means the shell deleted permanently,
                    // which this program treats as a failed operation.
                    return RecycleResult.Failure(sink.DeleteHResult,
                        "Shell did not report a Recycle Bin destination");
                }

                return RecycleResult.Success(sink.RecycleBinDisplayName);
            }
            catch (COMException comException)
            {
                return RecycleResult.Failure(comException.ErrorCode, comException.Message);
            }
            catch (Exception exception)
            {
                return RecycleResult.Failure(Marshal.GetHRForException(exception), exception.Message);
            }
            finally
            {
                if (item != null && Marshal.IsComObject(item))
                    Marshal.ReleaseComObject(item);

                if (operation != null && Marshal.IsComObject(operation))
                    Marshal.ReleaseComObject(operation);
            }
        }
    }
}
