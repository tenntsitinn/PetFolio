using System;
using System.Drawing;
using System.Runtime.InteropServices;

// Mirrors the client's pointer-to-anchor calculation. Saved bounds remain authoritative
// outside a drag; do not derive mascot coordinates from its large transparent HWND.
sealed class PetDragFollower {
    Point press, seed, savedAtStart, current;
    long releasedAt;
    bool pending;
    internal bool Active { get; private set; }
    internal void Begin(Point pointer, Point renderedAnchor, Point savedAnchor) {
        press=pointer;seed=renderedAnchor;savedAtStart=savedAnchor;current=seed;
        Active=true;pending=false;
    }
    internal void Move(Point pointer, Rectangle display, int width, int bottomReserve) {
        if(!Active)return;
        int height=(int)Math.Ceiling(width*208.0/192);
        current=new Point(
            Clamp(seed.X+pointer.X-press.X,display.Left,display.Right-width),
            Clamp(seed.Y+pointer.Y-press.Y,display.Top,display.Bottom-height-bottomReserve));
    }
    internal void End(long now) {
        if(!Active)return;
        Active=false;pending=true;releasedAt=now;
    }
    internal Point Resolve(Point savedAnchor,long now) {
        if(Active)return current;
        if(pending && savedAnchor==savedAtStart && now-releasedAt<2000)return current;
        pending=false;return savedAnchor;
    }
    internal void Reset() {Active=false;pending=false;}
    static int Clamp(int n,int min,int max) {return Math.Max(min,Math.Min(Math.Max(min,max),n));}
}

// Observes press/release only. Never consumes or generates mouse input. The hook
// callback stays small; painting, file reads and logging happen in the UI timer.
sealed class PetMouseObserver : IDisposable {
    [StructLayout(LayoutKind.Sequential)] struct MouseData {
        public int X,Y; public uint MouseDataValue,Flags,Time; public UIntPtr ExtraInfo;
    }
    delegate IntPtr HookProc(int code,IntPtr message,IntPtr data);
    readonly HookProc callback;
    readonly Action<Point,bool> notify;
    IntPtr hook;
    internal PetMouseObserver(Action<Point,bool> handler) {
        notify=handler;callback=OnMouse;
        hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
        if(hook==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }
    IntPtr OnMouse(int code,IntPtr message,IntPtr data) {
        int kind=message.ToInt32();
        if(code>=0 && (kind==0x201 || kind==0x202)) {
            var mouse=(MouseData)Marshal.PtrToStructure(data,typeof(MouseData));
            try {notify(new Point(mouse.X,mouse.Y),kind==0x201);}catch { }
        }
        return CallNextHookEx(hook,code,message,data);
    }
    public void Dispose() {if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}}
    [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,HookProc fn,IntPtr module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
}
