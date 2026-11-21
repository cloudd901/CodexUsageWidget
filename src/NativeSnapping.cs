using System;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CodexUsageBubble {
 // Pure physical-coordinate geometry shared by native dragging and regression tests.
 internal static class SnapGeometry {
  internal const int Threshold=15;
  internal struct Candidate {
   internal int X,Y,Distance;
   internal bool SnapX,SnapY;
  }
  internal static Rect Follow(Rect moving,Rect previousTarget,Rect target) {
   return new Rect(moving.X+target.X-previousTarget.X,moving.Y+target.Y-previousTarget.Y,moving.Width,moving.Height);
  }
  internal static Rect Clamp(Rect moving,Rect workArea) {
   return new Rect(Math.Max(workArea.Left,Math.Min(moving.Left,workArea.Right-moving.Width)),
    Math.Max(workArea.Top,Math.Min(moving.Top,workArea.Bottom-moving.Height)),moving.Width,moving.Height);
  }
  internal static Candidate? WorkArea(Rect moving,Rect target) {
   int left=(int)(target.Left-moving.Left),right=(int)(target.Right-moving.Right);
   int top=(int)(target.Top-moving.Top),bottom=(int)(target.Bottom-moving.Bottom);
   int x=Math.Abs(left)<=Math.Abs(right)?left:right;
   int y=Math.Abs(top)<=Math.Abs(bottom)?top:bottom;
   bool sx=Math.Abs(x)<=Threshold,sy=Math.Abs(y)<=Threshold;
   if(!sx&&!sy)return null;
   return new Candidate {X=sx?x:0,Y=sy?y:0,SnapX=sx,SnapY=sy,Distance=(sx?x*x:0)+(sy?y*y:0)};
  }
  static bool Near(double a1,double a2,double b1,double b2) {return a1<=b2+Threshold&&b1<=a2+Threshold;}
  internal static Candidate? Window(Rect moving,Rect target) {
   Candidate? best=null;
   // Opposing edges dock outside; matching edges dock inside the target window.
   int[] dx={(int)(target.Left-moving.Right),(int)(target.Right-moving.Left),(int)(target.Left-moving.Left),(int)(target.Right-moving.Right)};
   int[] dy={(int)(target.Top-moving.Bottom),(int)(target.Bottom-moving.Top),(int)(target.Top-moving.Top),(int)(target.Bottom-moving.Bottom)};
   foreach(int x in dx)foreach(int y in dy)
    if(Math.Abs(x)<=Threshold&&Math.Abs(y)<=Threshold)
     Consider(ref best,new Candidate{X=x,Y=y,SnapX=true,SnapY=true,Distance=x*x+y*y});
   if(best.HasValue)return best;
   if(Near(moving.Top,moving.Bottom,target.Top,target.Bottom))
    foreach(int x in dx)if(Math.Abs(x)<=Threshold)Consider(ref best,new Candidate{X=x,SnapX=true,Distance=x*x});
   if(Near(moving.Left,moving.Right,target.Left,target.Right))
    foreach(int y in dy)if(Math.Abs(y)<=Threshold)Consider(ref best,new Candidate{Y=y,SnapY=true,Distance=y*y});
   return best;
  }
  static void Consider(ref Candidate? best,Candidate candidate) {
   if(!best.HasValue||candidate.Distance<best.Value.Distance||
      (candidate.Distance==best.Value.Distance&&candidate.SnapX&&candidate.SnapY))best=candidate;
  }
 }
 // Store edge anchors, not a stale pixel width: physical size can change at a DPI boundary.
 // Axes release independently so a corner can become an edge while dragging.
 internal sealed class SnapAttachment {
  internal bool X,Y;
  readonly double movingX,targetX,movingY,targetY;
  internal SnapAttachment(Rect moving,Rect target,bool x,bool y) {
   X=x;Y=y;
   movingX=moving.Right==target.Left||moving.Right==target.Right?1:0;
   targetX=moving.Left==target.Right||moving.Right==target.Right?1:0;
   movingY=moving.Bottom==target.Top||moving.Bottom==target.Bottom?1:0;
   targetY=moving.Top==target.Bottom||moving.Bottom==target.Bottom?1:0;
  }
  internal Rect Constrain(Rect proposed,Rect target) {
   double x=target.Left+targetX*target.Width-movingX*proposed.Width,y=target.Top+targetY*target.Height-movingY*proposed.Height;
   if(X&&Math.Abs(proposed.Left-x)>SnapGeometry.Threshold)X=false;
   if(Y&&Math.Abs(proposed.Top-y)>SnapGeometry.Threshold)Y=false;
   return new Rect(X?x:proposed.Left,Y?y:proposed.Top,proposed.Width,proposed.Height);
  }
 }
 // WM_MOVING is supported by WPF's native DragMove even on layered windows.
 // All rectangles and cursor deltas here are physical screen pixels, never WPF DIPs.
 internal sealed class NativeSnapping : IDisposable {
  [StructLayout(LayoutKind.Sequential)] internal struct Bounds {
   internal int Left,Top,Right,Bottom;
   internal Bounds(Rect r){Left=(int)Math.Round(r.Left);Top=(int)Math.Round(r.Top);Right=Left+(int)Math.Round(r.Width);Bottom=Top+(int)Math.Round(r.Height);}
   internal Rect Rect {get{return new Rect(Left,Top,Math.Max(0,Right-Left),Math.Max(0,Bottom-Top));}}
  }
  [StructLayout(LayoutKind.Sequential)] struct CursorPoint {internal int X,Y;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Auto)] struct MonitorInfo {internal int Size;internal Bounds Monitor,Work;internal uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] internal string Device;}
  delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref Bounds bounds,IntPtr data);
  [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out Bounds rect);
  [DllImport("user32.dll")] static extern bool GetCursorPos(out CursorPoint point);
  [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref Bounds rect,uint flags);
  [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
  [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  delegate bool EnumWindow(IntPtr hwnd,IntPtr parameter);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr parameter);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
  [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
  [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
  [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
  [DllImport("user32.dll",CharSet=CharSet.Auto)] static extern int GetClassName(IntPtr hwnd,System.Text.StringBuilder text,int count);
  [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr hwnd,int index);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
  [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr hwnd,out uint key,out byte alpha,out uint flags);
  [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
  delegate void WindowEvent(IntPtr hook,uint kind,IntPtr hwnd,int objectId,int childId,uint thread,uint time);
  [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WindowEvent callback,uint process,uint thread,uint flags);
  [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
  [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out Bounds value,int size);
  [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmGetCloaked(IntPtr hwnd,int attribute,out int value,int size);
  internal sealed class PhysicalScope : IDisposable {
   IntPtr previous;
   internal PhysicalScope(){try{previous=SetThreadDpiAwarenessContext(new IntPtr(-4));}catch(EntryPointNotFoundException){}}
   public void Dispose(){if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  readonly BubbleWindow owner;
  HwndSource source;
  IntPtr handle;
  bool dragging;
  CursorPoint originCursor;
  Rect originRect;
  sealed class Target {
   internal IntPtr Handle;
   internal bool Monitor;
   internal uint Process,Thread;
   internal Rect Bounds;
  }
  Target attachedTarget;
  SnapAttachment attachment;
  Vector followOverflow;
  // Movement notifications are coalesced to one update per frame. The slow timer
  // only checks for missed notifications (for example a virtual-desktop change).
  readonly DispatcherTimer tracking=new DispatcherTimer(DispatcherPriority.Background) {Interval=TimeSpan.FromMilliseconds(500)};
  readonly DispatcherTimer followFrame=new DispatcherTimer(DispatcherPriority.Render) {Interval=TimeSpan.FromMilliseconds(16)};
  readonly WindowEvent windowEvent;
  IntPtr objectHook,systemHook;
  GCHandle eventRoot;
  DispatcherOperation pendingFollow;
  long lastFollowAt;
  internal NativeSnapping(BubbleWindow window){owner=window;windowEvent=TargetEvent;owner.SourceInitialized+=delegate{handle=new WindowInteropHelper(owner).Handle;source=HwndSource.FromHwnd(handle);source.AddHook(Message);};tracking.Tick+=delegate{TrackTarget();};followFrame.Tick+=delegate{followFrame.Stop();FlushFollow();};}
  internal void BeginDrag(){using(new PhysicalScope()){Bounds r;if(GetWindowRect(handle,out r)&&GetCursorPos(out originCursor)){originRect=r.Rect;dragging=true;}}}
  internal void EndDrag(){dragging=false;}
  internal void Detach(){attachment=null;attachedTarget=null;followOverflow=new Vector();StopTracking();}
  void StartTracking() {
   StopTracking();
   // Out-of-context callbacks run on this WPF thread; only the target's
   // process/thread is observed, and no code is injected into other apps.
   if(attachedTarget.Process!=0&&attachedTarget.Thread!=0) {
    eventRoot=GCHandle.Alloc(windowEvent);
    objectHook=SetWinEventHook(0x8001,0x800B,IntPtr.Zero,windowEvent,attachedTarget.Process,attachedTarget.Thread,0);
    systemHook=SetWinEventHook(0x16,0x17,IntPtr.Zero,windowEvent,attachedTarget.Process,attachedTarget.Thread,0);
   }
   tracking.Interval=TimeSpan.FromMilliseconds(objectHook==IntPtr.Zero?50:500);
   tracking.Start();
  }
  void StopTracking() {
   tracking.Stop();followFrame.Stop();
   if(pendingFollow!=null){pendingFollow.Abort();pendingFollow=null;}
   lastFollowAt=0;
   if(objectHook!=IntPtr.Zero){UnhookWinEvent(objectHook);objectHook=IntPtr.Zero;}
   if(systemHook!=IntPtr.Zero){UnhookWinEvent(systemHook);systemHook=IntPtr.Zero;}
   if(eventRoot.IsAllocated)eventRoot.Free();
  }
  void TargetEvent(IntPtr hook,uint kind,IntPtr hwnd,int objectId,int childId,uint thread,uint time) {
   if(attachedTarget==null||dragging||hwnd!=attachedTarget.Handle||objectId!=0||childId!=0)return;
   if(hook!=objectHook&&hook!=systemHook)return;
   if(kind!=0x800B&&kind!=0x8001&&kind!=0x8003&&kind!=0x16&&kind!=0x17)return;
   // Queue one read of the latest bounds, never work inside a native callback.
   // Do not restart pending work: a burst must not postpone following.
   if(pendingFollow==null&&!followFrame.IsEnabled)
    pendingFollow=owner.Dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(FlushFollow));
  }
  void FlushFollow() {
   pendingFollow=null;
   if(attachedTarget==null||dragging)return;
   long now=System.Diagnostics.Stopwatch.GetTimestamp();
   double elapsed=(now-lastFollowAt)*1000.0/System.Diagnostics.Stopwatch.Frequency;
   double remaining=1000.0/60-elapsed;
   if(lastFollowAt!=0&&remaining>0) {
    followFrame.Interval=TimeSpan.FromMilliseconds(Math.Max(1,remaining));followFrame.Start();return;
   }
   lastFollowAt=now;TrackTarget();
  }
  bool ReadTarget(Target target,out Rect rect) {
   if(target.Monitor){var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};bool ok=GetMonitorInfo(target.Handle,ref info);rect=info.Work.Rect;return ok;}
   uint process;uint thread=GetWindowThreadProcessId(target.Handle,out process);
   if(thread!=target.Thread||process!=target.Process){rect=Rect.Empty;return false;}
   return VisibleTarget(target.Handle,out rect);
  }
  Rect KeepOnScreen(Rect proposed) {
   var rectangle=new Bounds(proposed);var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
   var monitor=MonitorFromRect(ref rectangle,2); // nearest monitor, including negative desktop coordinates
   return GetMonitorInfo(monitor,ref info)?SnapGeometry.Clamp(proposed,info.Work.Rect):proposed;
  }
  internal sealed class Placement {
   public string Device,Settings;
   public double X,Y,WorkX,WorkY;
  }
  internal sealed class DisplayArea {
   internal string Device;
   internal Rect Work;
  }
  internal static DisplayArea[] Displays() {
   var displays=new System.Collections.Generic.List<DisplayArea>();
   EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr monitor,IntPtr dc,ref Bounds bounds,IntPtr data){
    var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
    if(GetMonitorInfo(monitor,ref info))displays.Add(new DisplayArea{Device=info.Device,Work=info.Work.Rect});
    return true;
   },IntPtr.Zero);
   return displays.ToArray();
  }
  internal Placement CapturePlacement() {
   using(new PhysicalScope()) {
    Bounds rect;if(!GetWindowRect(handle,out rect))return null;
    var monitor=MonitorFromRect(ref rect,2);var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
    if(!GetMonitorInfo(monitor,ref info))return null;
    return new Placement{Device=info.Device,X=rect.Left,Y=rect.Top,WorkX=info.Work.Left,WorkY=info.Work.Top};
   }
  }
  internal static Rect RestorePlacement(Placement saved,DisplayArea[] displays,Size size) {
   if(displays.Length==0)return new Rect(saved.X,saved.Y,size.Width,size.Height);
   DisplayArea chosen=null;
   foreach(var display in displays)if(display.Device==saved.Device){chosen=display;break;}
   var proposed=new Rect(saved.X,saved.Y,size.Width,size.Height);
   if(chosen!=null)proposed.Offset(chosen.Work.Left-saved.WorkX,chosen.Work.Top-saved.WorkY);
   else {
    double best=double.MaxValue;
    foreach(var display in displays) {
     var nearest=SnapGeometry.Clamp(proposed,display.Work);
     double distance=(nearest.Location-proposed.Location).LengthSquared;
     if(distance<best){best=distance;chosen=display;}
    }
   }
   return SnapGeometry.Clamp(proposed,chosen.Work);
  }
  internal void RestorePlacement(Placement saved) {
   if(saved==null)return;
   using(new PhysicalScope()) {
    // Read current topology every time; monitor origins and work areas can change.
    var displays=Displays();
    if(source==null||source.CompositionTarget==null)return;
    var scale=source.CompositionTarget.TransformToDevice;
    var desired=RestorePlacement(saved,displays,new Size(owner.Width*scale.M11,owner.Height*scale.M22));
    SetWindowPos(handle,IntPtr.Zero,(int)Math.Round(desired.X),(int)Math.Round(desired.Y),0,0,0x15);
   }
  }
  void TrackTarget() {
   if(dragging||attachedTarget==null)return;
   if(!owner.SnappingEnabled){Detach();return;}
   using(new PhysicalScope()) {
    Rect target;Bounds bubble;
    if(!ReadTarget(attachedTarget,out target)){Detach();return;}
    if(GetWindowRect(handle,out bubble)) {
     var intended=bubble.Rect;intended.Offset(followOverflow);
     var moved=SnapGeometry.Follow(intended,attachedTarget.Bounds,target);
     var visible=KeepOnScreen(moved);
     if(visible.Location!=bubble.Rect.Location&&!SetWindowPos(handle,IntPtr.Zero,(int)visible.X,(int)visible.Y,0,0,0x15))return; // NOSIZE | NOZORDER | NOACTIVATE
     // Retain the unclamped position so returning targets recover the original offset.
     followOverflow=moved.Location-visible.Location;
    }
    attachedTarget.Bounds=target;
   }
  }
  bool VisibleTarget(IntPtr hwnd,out Rect rect) {
   rect=Rect.Empty;
   if(hwnd==handle||hwnd==GetShellWindow()||hwnd==GetDesktopWindow()||!IsWindowVisible(hwnd)||IsIconic(hwnd))return false;
   int style=GetWindowLong(hwnd,-20);if((style&(0x80|0x08000000))!=0)return false; // tool/nonactivating helpers
   uint key,flags;byte alpha;
   if((style&0x80000)!=0&&GetLayeredWindowAttributes(hwnd,out key,out alpha,out flags)&&(flags&2)!=0&&alpha==0)return false;
   var name=new System.Text.StringBuilder(256);GetClassName(hwnd,name,name.Capacity);
   string kind=name.ToString();if(kind=="Progman"||kind=="WorkerW"||kind=="Shell_TrayWnd"||kind=="Shell_SecondaryTrayWnd")return false;
   int cloaked;Bounds bounds;
   try {
    if(DwmGetCloaked(hwnd,14,out cloaked,4)==0&&cloaked!=0)return false; // other virtual desktop / hidden shell surfaces
    if(DwmGetWindowAttribute(hwnd,9,out bounds,Marshal.SizeOf(typeof(Bounds)))!=0&&!GetWindowRect(hwnd,out bounds))return false;
   }catch(DllNotFoundException){if(!GetWindowRect(hwnd,out bounds))return false;}
   rect=bounds.Rect;return rect.Width>0&&rect.Height>0;
  }
  Rect Propose(Rect proposed) {
   // Explicit dragging starts from the visible widget, rather than its off-screen anchor.
   followOverflow=new Vector();
   if(attachment!=null) {
    Rect current;
    if(ReadTarget(attachedTarget,out current)) {
     var constrained=attachment.Constrain(proposed,current);
     if(!attachedTarget.Monitor) {
      if(attachment.X&&(constrained.Top>current.Bottom+SnapGeometry.Threshold||constrained.Bottom<current.Top-SnapGeometry.Threshold))attachment.X=false;
      if(attachment.Y&&(constrained.Left>current.Right+SnapGeometry.Threshold||constrained.Right<current.Left-SnapGeometry.Threshold))attachment.Y=false;
     }
     if(attachment.X||attachment.Y) {
      constrained=new Rect(attachment.X?constrained.X:proposed.X,attachment.Y?constrained.Y:proposed.Y,proposed.Width,proposed.Height);
      var corner=attachedTarget.Monitor?SnapGeometry.WorkArea(constrained,current):SnapGeometry.Window(constrained,current);
      if(corner.HasValue&&corner.Value.SnapX&&corner.Value.SnapY) {
       constrained.Offset(attachment.X?0:corner.Value.X,attachment.Y?0:corner.Value.Y);
       attachment=new SnapAttachment(constrained,current,true,true);
      }
      attachedTarget.Bounds=current;return constrained;
     }
    }
    Detach();
   }
   SnapGeometry.Candidate? best=null;
   Target chosen=null;
   var rectangle=new Bounds(proposed);var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};
   var monitor=MonitorFromRect(ref rectangle,2);
   if(GetMonitorInfo(monitor,ref info)) {best=SnapGeometry.WorkArea(proposed,info.Work.Rect);if(best.HasValue)chosen=new Target{Handle=monitor,Monitor=true,Bounds=info.Work.Rect};}
   EnumWindows(delegate(IntPtr hwnd,IntPtr parameter) {
    Rect bounds;if(VisibleTarget(hwnd,out bounds)) {
     var candidate=SnapGeometry.Window(proposed,bounds);
     if(candidate.HasValue&&(!best.HasValue||candidate.Value.Distance<best.Value.Distance)) {
      best=candidate;uint process;uint thread=GetWindowThreadProcessId(hwnd,out process);
      chosen=new Target{Handle=hwnd,Bounds=bounds,Thread=thread,Process=process};
     }
    }
    return true;
   },IntPtr.Zero);
   if(best.HasValue) {
    proposed.Offset(best.Value.X,best.Value.Y);attachedTarget=chosen;
    attachment=new SnapAttachment(proposed,chosen.Bounds,best.Value.SnapX,best.Value.SnapY);
    if(!chosen.Monitor)StartTracking();
   }
   return proposed;
  }
  IntPtr Message(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled) {
   if(msg==0x232)EndDrag(); // no snapping or repositioning on WM_EXITSIZEMOVE
   if(msg==0x216) {
    using(new PhysicalScope()) {
     var native=(Bounds)Marshal.PtrToStructure(lp,typeof(Bounds));var proposed=native.Rect;
     CursorPoint cursor;
     if(dragging&&GetCursorPos(out cursor))proposed=new Rect(originRect.X+cursor.X-originCursor.X,originRect.Y+cursor.Y-originCursor.Y,proposed.Width,proposed.Height);
     if(owner.SnappingEnabled)proposed=Propose(proposed);
     var visible=KeepOnScreen(proposed);
     if(visible!=proposed)Detach();
     proposed=visible;
     Marshal.StructureToPtr(new Bounds(proposed),lp,false);handled=true;return new IntPtr(1);
    }
   }
   return IntPtr.Zero;
  }
  public void Dispose(){Detach();if(source!=null)source.RemoveHook(Message);}
 }
}
