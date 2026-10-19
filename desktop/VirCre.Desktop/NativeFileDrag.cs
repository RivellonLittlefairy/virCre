using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace VirCre.Desktop;

/// <summary>Standard OLE file copying. Explorer receives CF_HDROP, without opening .lnk through WinRT StorageFile.</summary>
internal static class NativeFileDrag {
    private const int FormatError=unchecked((int)0x80040064);
    private static FORMATETC FileFormat=>new() {cfFormat=15,dwAspect=DVASPECT.DVASPECT_CONTENT,lindex=-1,tymed=TYMED.TYMED_HGLOBAL};
    public static IDataObject CreateDataObject(string path)=>new FileDataObject(System.IO.Path.GetFullPath(path));
    public static object CreateDropSource(Func<bool> cancelled)=>new DropSource(cancelled);
    public static int ContinueDrag(bool escape,uint keys)=>escape?0x40101:(keys&1)==0?0x40100:0;
    public static bool Start(string path,Func<bool> cancelled) {
        if(!File.Exists(path)) throw new FileNotFoundException("快捷方式已失效，请重试。",path);
        var initialized=OleInitialize(IntPtr.Zero);Marshal.ThrowExceptionForHR(initialized);
        try {
            var result=DoDragDrop(CreateDataObject(path),new DropSource(cancelled),1,out var effect);
            Marshal.ThrowExceptionForHR(result);
            return result==0x40100 && (effect&1)!=0;
        } finally {OleUninitialize();}
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    private sealed class FileDataObject(string path) : IDataObject {
        public int QueryGetData(ref FORMATETC format)=>format.cfFormat==15 && (format.tymed&TYMED.TYMED_HGLOBAL)!=0 && format.dwAspect==DVASPECT.DVASPECT_CONTENT && format.lindex==-1?0:FormatError;
        public void GetData(ref FORMATETC format,out STGMEDIUM medium) {
            Marshal.ThrowExceptionForHR(QueryGetData(ref format));
            var names=Encoding.Unicode.GetBytes(path+"\0\0");
            var memory=GlobalAlloc(0x42,(UIntPtr)(20+names.Length));
            if(memory==IntPtr.Zero) throw new OutOfMemoryException();
            try {
                var pointer=GlobalLock(memory);
                if(pointer==IntPtr.Zero) throw new OutOfMemoryException();
                try {
                    Marshal.WriteInt32(pointer,0,20); // DROPFILES.pFiles
                    Marshal.WriteInt32(pointer,16,1); // DROPFILES.fWide
                    Marshal.Copy(names,0,IntPtr.Add(pointer,20),names.Length);
                } finally {GlobalUnlock(memory);}
                medium=new STGMEDIUM {tymed=TYMED.TYMED_HGLOBAL,unionmember=memory,pUnkForRelease=null};
            } catch {GlobalFree(memory);throw;}
        }
        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)=>direction==DATADIR.DATADIR_GET?new FormatEnumerator():throw new COMException("Unsupported data direction",unchecked((int)0x80004001));
        public int GetCanonicalFormatEtc(ref FORMATETC input,out FORMATETC output) {output=input;output.ptd=IntPtr.Zero;return 0x40130;}
        public void GetDataHere(ref FORMATETC format,ref STGMEDIUM medium)=>throw new COMException("Unsupported medium",FormatError);
        public void SetData(ref FORMATETC format,ref STGMEDIUM medium,bool release)=>throw new COMException("Read-only drag data",unchecked((int)0x80004001));
        public int DAdvise(ref FORMATETC format,ADVF flags,IAdviseSink sink,out int connection) {connection=0;return unchecked((int)0x80040003);}
        public void DUnadvise(int connection)=>throw new COMException("Advises unsupported",unchecked((int)0x80040003));
        public int EnumDAdvise(out IEnumSTATDATA enumerator) {enumerator=null!;return unchecked((int)0x80040003);}
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    private sealed class FormatEnumerator : IEnumFORMATETC {
        private int index;
        public int Next(int count,FORMATETC[] formats,int[] fetched) {
            var returned=count>0 && index==0?1:0;
            if(returned==1) {formats[0]=FileFormat;index=1;}
            if(fetched is {Length:>0}) fetched[0]=returned;
            return returned==count?0:1;
        }
        public int Skip(int count) {if(count<=0)return 0;var previous=index;index=1;return previous==0 && count==1?0:1;}
        public int Reset() {index=0;return 0;}
        public void Clone(out IEnumFORMATETC clone) {clone=new FormatEnumerator {index=index};}
    }
    [ComVisible(true),ClassInterface(ClassInterfaceType.None)]
    private sealed class DropSource(Func<bool> cancelled) : IDropSource {
        public int QueryContinueDrag(bool escape,uint keys)=>ContinueDrag(escape||cancelled(),keys);
        public int GiveFeedback(uint effect)=>0x40102;
    }
    [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);
    [DllImport("ole32.dll")] private static extern void OleUninitialize();
    [DllImport("ole32.dll")] private static extern int DoDragDrop([MarshalAs(UnmanagedType.Interface)] IDataObject data,[MarshalAs(UnmanagedType.Interface)] IDropSource source,uint effects,out uint effect);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags,UIntPtr size);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
}

// COM must be able to query this interface from outside the managed assembly.
[ComVisible(true),Guid("00000121-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IDropSource {
    [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool escape,uint keys);
    [PreserveSig] int GiveFeedback(uint effect);
}
