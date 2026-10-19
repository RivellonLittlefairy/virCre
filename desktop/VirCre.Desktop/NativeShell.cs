using System.Runtime.InteropServices;

namespace VirCre.Desktop;

internal static class NativeShell {
    public static string CopyFile(string source,string directory)=>InSta(()=>{
        Directory.CreateDirectory(directory);
        var stem=Path.GetFileNameWithoutExtension(source);var extension=Path.GetExtension(source);
        var destination=Path.Combine(directory,Path.GetFileName(source));
        for(var i=2;File.Exists(destination);i++) destination=Path.Combine(directory,stem+$" ({i})"+extension);
        var operation=new FileOperation();IShellItem? item=null,folder=null;
        try {
            item=CreateItem(source);folder=CreateItem(directory);
            var copy=(IFileOperation)operation;
            copy.SetOperationFlags(0x040C); // Silent, rename on collision, no error dialog; never overwrite.
            copy.CopyItem(item,folder,Path.GetFileName(destination),IntPtr.Zero);
            copy.PerformOperations();copy.GetAnyOperationsAborted(out var aborted);
            if(aborted) throw new IOException("Windows 取消了快捷方式复制。");
            if(!File.Exists(destination)) throw new IOException("Windows 未在桌面创建快捷方式。");
            return destination;
        } finally {
            if(folder!=null) Marshal.FinalReleaseComObject(folder);
            if(item!=null) Marshal.FinalReleaseComObject(item);
            Marshal.FinalReleaseComObject(operation);
        }
    });
    public static void OpenParentAndSelect(string path)=>InSta(()=>{
        IntPtr folder=IntPtr.Zero,item=IntPtr.Zero;
        try {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(Path.GetDirectoryName(path)!,IntPtr.Zero,out folder,0,out _));
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path,IntPtr.Zero,out item,0,out _));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(folder,1,[ILFindLastID(item)],0));
            return true;
        } finally {if(item!=IntPtr.Zero)Marshal.FreeCoTaskMem(item);if(folder!=IntPtr.Zero)Marshal.FreeCoTaskMem(folder);}
    });
    private static IShellItem CreateItem(string path) {
        var id=typeof(IShellItem).GUID;
        Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path,IntPtr.Zero,ref id,out var item));return item;
    }
    private static T InSta<T>(Func<T> action) {
        if(Thread.CurrentThread.GetApartmentState()==ApartmentState.STA) return action();
        var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>{try {result.SetResult(action());}catch(Exception error){result.SetException(error);}}) {IsBackground=true,Name="virCre Shell"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return result.Task.GetAwaiter().GetResult();
    }
    [ComImport,Guid("3AD05575-8857-4850-9277-11B85BDB8E09"),ClassInterface(ClassInterfaceType.None)] private class FileOperation { }
    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem {
        void BindToHandler(IntPtr context,ref Guid handler,ref Guid id,out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind,out IntPtr name);
        void GetAttributes(uint mask,out uint attributes);
        void Compare(IShellItem item,uint hint,out int order);
    }
    [ComImport,Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation {
        void Advise(IntPtr sink,out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
        void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
        void SetOwnerWindow(IntPtr window);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void RenameItem(IShellItem item,[MarshalAs(UnmanagedType.LPWStr)] string name,IntPtr sink);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object items,[MarshalAs(UnmanagedType.LPWStr)] string name);
        void MoveItem(IShellItem item,IShellItem folder,[MarshalAs(UnmanagedType.LPWStr)] string name,IntPtr sink);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object items,IShellItem folder);
        void CopyItem(IShellItem item,IShellItem folder,[MarshalAs(UnmanagedType.LPWStr)] string name,IntPtr sink);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object items,IShellItem folder);
        void DeleteItem(IShellItem item,IntPtr sink);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object items);
        void NewItem(IShellItem folder,uint attributes,[MarshalAs(UnmanagedType.LPWStr)] string name,[MarshalAs(UnmanagedType.LPWStr)] string template,IntPtr sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid id,out IShellItem item);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHParseDisplayName(string path,IntPtr context,out IntPtr pidl,uint mask,out uint attributes);
    [DllImport("shell32.dll")] private static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr folder,uint count,[MarshalAs(UnmanagedType.LPArray,SizeParamIndex=1)] IntPtr[] children,uint flags);
}
