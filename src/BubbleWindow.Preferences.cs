using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Web.Script.Serialization;

namespace CodexUsageBubble {
 public sealed partial class BubbleWindow : Window {
  void LoadPosition() {
   loadingPosition=true;
   var area=SystemParameters.WorkArea;Left=area.Right-Width-24;Top=area.Bottom-Height-24;
   try {
    var p=preferencesStore.Load();
    if(p.Left.HasValue&&p.Top.HasValue){Left=p.Left.Value;Top=p.Top.Value;}
    normalWidth=Width=p.NormalWidth;
    minimalCountdown=p.Countdown;showLabelPrefixes=p.ShowLabelPrefixes;labelPrefixes.IsChecked=showLabelPrefixes;
    int restoredTheme=Array.FindIndex(WidgetTheme.All,theme=>theme.Name==p.Theme);
    SetTheme(restoredTheme);
    SetMinimalSize(p.MinimalSize);
    SetResetBackgroundBars(p.ResetTimeBackground);
    snapping.IsChecked=p.Snapping;
    SetMinimalShape(p.MinimalShape=="Square"?MinimalShape.Square:MinimalShape.Circle);
    closeToTray.IsChecked=p.CloseToTray;
    if(p.MinimalMode)SetMinimalMode(true);
   }catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.PreferencesLoad,error);}
   var placement=nativeSnapping.CapturePlacement();
   try {
    string settings=File.ReadAllText(positionFile);
    var saved=new JavaScriptSerializer().Deserialize<NativeSnapping.Placement>(File.ReadAllText(positionFile+".monitor.json"));
    if(saved!=null&&saved.Settings==settings&&WidgetPreferences.Finite(saved.X)&&WidgetPreferences.Finite(saved.Y)&&WidgetPreferences.Finite(saved.WorkX)&&WidgetPreferences.Finite(saved.WorkY))placement=saved;
   }catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
   catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.MonitorPlacementLoad,error);}
   nativeSnapping.RestorePlacement(placement);
   // WPF may adjust physical size on a DPI transition. Recheck after layout settles.
   Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(()=>{
    if(closed)return;
    nativeSnapping.RestorePlacement(placement);
    try{if(preferencesStore.CanSave&&File.Exists(positionFile))SaveMonitorPlacement(File.ReadAllText(positionFile));}catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.MonitorPlacementLoad,error);}
   }));
   loadingPosition=false;UpdateState();
  }
  void SaveMonitorPlacement(string settings) {
   var placement=nativeSnapping.CapturePlacement();
   if(placement==null)return;
   placement.Settings=settings;
    AtomicFile.Write(positionFile+".monitor.json",new JavaScriptSerializer().Serialize(placement));
  }
  void SavePosition() {
   try {
    if(!IsLoaded||loadingPosition||!preferencesStore.CanSave)return;
    string settings=preferencesStore.Save(new WidgetPreferences {Left=Left,Top=Top,NormalWidth=isMinimal?normalWidth:Width,MinimalMode=isMinimal,Countdown=minimalCountdown,ShowLabelPrefixes=showLabelPrefixes,Theme=Theme.Name,MinimalSize=minimalDiameter,ResetTimeBackground=resetBackgroundBars,Snapping=snapping.IsChecked,MinimalShape=minimalShape.ToString(),CloseToTray=closeToTray.IsChecked});
    SaveMonitorPlacement(settings);
   }catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.PreferencesSave,error);}
  }
 }
}
