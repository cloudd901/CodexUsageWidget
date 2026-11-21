using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

[assembly: System.Reflection.AssemblyTitle("CodexUsageWidget")]
[assembly: System.Reflection.AssemblyProduct("CodexUsageWidget")]
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.1.0.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("1.1.0")]
[assembly: System.Reflection.AssemblyCopyright("Copyright (c) 2026 CodexUsageWidget authors")]

namespace CodexUsageBubble {
 public static class Program {
  internal static void ConfigureDpi() {
   // The manifest establishes PMv2 before WPF loads; enable .NET 4.8 rescaling.
   AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",false);
  }
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);

  [STAThread] public static void Main(string[] args) {
   ConfigureDpi();
   bool verify=args.Length>=1&&args[0]=="--startup-ready";
   bool isolated=verify&&args.Length==3&&args[2]=="--isolated";
   Guid startupId;
   if(verify) {
    if((args.Length!=2&&!isolated)||!Guid.TryParseExact(args[1],"N",out startupId))return;
    using(var ready=EventWaitHandle.OpenExisting(@"Local\CodexUsageBubble.Startup."+startupId.ToString("N"))) {
     if(isolated) {
      string settings=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"startup-"+startupId.ToString("N"),"position.json");
      RunWidget(ready,settings);
     }else RunSingleWidget(ready);
    }
   }else RunSingleWidget(null);
  }

  static void RunSingleWidget(EventWaitHandle ready) {
   bool created;
   using(var mutex=new Mutex(true,@"Local\CodexUsageBubble",out created)) {
    if(created)RunWidget(ready,null);
   }
  }

  static void RunWidget(EventWaitHandle ready,string isolatedSettings) {
   var app=new Application();
   var window=new BubbleWindow(isolatedSettings==null,null,null,null,isolatedSettings);
   if(ready!=null) {
    window.ContentRendered+=delegate {
     window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>{
      var handle=new WindowInteropHelper(window).Handle;
      if(window.IsVisible&&handle!=IntPtr.Zero&&IsWindowVisible(handle))ready.Set();
     }));
    };
   }
   app.Run(window);
  }
 }
}
