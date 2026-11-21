using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexUsageBubble {
 public sealed partial class BubbleWindow : Window {
  internal const double NormalHeight=136;
  internal bool Paused { get { return polling.Paused; } }
  readonly bool autoRefresh;
  readonly DispatcherTimer clock = new DispatcherTimer();
  readonly DispatcherTimer pollTimer = new DispatcherTimer();
  readonly DispatcherTimer actionTimer = new DispatcherTimer();
  readonly ResetActionCoordinator resetActions;
  ResetActionWindow actionWindow;
  readonly AdaptivePolling polling = new AdaptivePolling();
  readonly Func<CancellationToken,Task<UsageSnapshot>> readUsage;
  readonly Func<TimeSpan> monotonicNow;
  readonly Func<DateTimeOffset> utcNow;
  readonly TextBlock state = new TextBlock(), age = new TextBlock();
  readonly UsageRow five, week;
  readonly Grid normalSurface;
  readonly MinimalBubble minimalBubble;
  readonly MenuItem minimalMode = new MenuItem { Header="Minimal mode",IsCheckable=true };
  readonly System.Windows.Shapes.Ellipse resetActionDot=new System.Windows.Shapes.Ellipse {Width=8,Height=8};
  static readonly SolidColorBrush resetActionGreen=Brush("#6BD493"),resetActionGreenLight=Brush("#16834A");
  bool isMinimal, minimalCountdown,showLabelPrefixes=true,resetBackgroundBars;
  readonly MenuItem resetBars=new MenuItem {Header="Reset-time background bars",IsCheckable=true};
  readonly MenuItem snapping=new MenuItem {Header="Enable snapping",IsCheckable=true};
		readonly MenuItem closeToTray = new MenuItem { Header="Close to tray", IsCheckable=true, IsChecked=true };
		System.Windows.Forms.NotifyIcon trayIcon;
		System.Windows.Forms.ContextMenuStrip trayMenu;
		System.Windows.Forms.ToolStripMenuItem trayVisibility;
		System.Windows.Forms.ToolStripLabel trayAutoMessage;
		System.Drawing.Bitmap trayAutoMessageOnImage, trayAutoMessageOffImage;
		System.Drawing.Icon trayImage;
		bool allowExit, initialized, restoreActionWindow;
  readonly MenuItem labelPrefixes=new MenuItem {Header="Show label prefixes",IsCheckable=true,IsChecked=true};
  int themeIndex;
  int minimalDiameter=60;
  MinimalShape minimalShape=MinimalShape.Circle;
  static readonly int[] minimalSizes={40,60,80,100};
  readonly MenuItem[] sizeChoices=new MenuItem[minimalSizes.Length];
  readonly MenuItem[] shapeChoices=new MenuItem[2];
  readonly MenuItem[] themeChoices=new MenuItem[3];
  WidgetTheme Theme {get{return WidgetTheme.All[themeIndex];}}
  Point? dragStart;
  double normalWidth=272;
  readonly MenuItem pause = new MenuItem { Header="Pause refresh" }, refresh = new MenuItem { Header="Refresh now" };
  CancellationTokenSource pending;
  UsageSnapshot snapshot;
  DateTimeOffset lastRead;
  bool busy, closed, refreshQueued,loadingPosition;
  readonly DispatcherTimer placementSaveTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(250)};
  string error;
  static readonly string dataDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageBubble");
  readonly string positionFile;
  readonly WidgetPreferencesStore preferencesStore;
  readonly NativeSnapping nativeSnapping;
  internal bool SnappingEnabled {get{return snapping.IsChecked;}}
  async void RefreshNow() {
   if(busy||closed)return;pollTimer.Stop();busy=true;pending=new CancellationTokenSource();refresh.IsEnabled=false;UpdateState();
   var token=pending.Token;var requestStarted=utcNow();
   try {
    var value=await readUsage(token);
    if(closed||token.IsCancellationRequested)return;
    polling.Succeeded(value,monotonicNow());snapshot=value;lastRead=utcNow();error=null;
    resetActions.Observe(value,Paused,requestStarted);
   }catch(OperationCanceledException e){if(!closed&&!token.IsCancellationRequested)RecordFailure(e);}
   catch(Exception e){if(!closed&&!token.IsCancellationRequested)RecordFailure(e);}
   finally {
    busy=false;pending.Dispose();pending=null;
    if(!closed){
     refresh.IsEnabled=true;UpdateState();
     if(refreshQueued&&!Paused){refreshQueued=false;QueueRefresh();}
     else ArmPolling();
     ScheduleActionCheck();
    }
   }
  }
  void QueueRefresh() {Dispatcher.BeginInvoke(new Action(()=>{if(!closed&&!Paused)RefreshNow();}));}
  void RecordFailure(Exception e) {
   DiagnosticLog.Record(DiagnosticOperation.UsageRead,e);
   polling.Failed(monotonicNow());
   resetActions.ReadFailed();
   error=e is UsageException?e.Message:"Usage read failed. Open Codex and check the account sign-in.";
  }
  void ScheduleActionCheck() {
   actionTimer.Stop();
   if(!autoRefresh||closed||Paused)return;
   var due=resetActions.NextCheck;if(!due.HasValue)return;
   var delay=due.Value-utcNow();
   // Bound timer sleeps to recheck after clock changes or Windows sleep/resume.
   actionTimer.Interval=TimeSpan.FromMilliseconds(Math.Max(1,Math.Min(60000,delay.TotalMilliseconds)));
   actionTimer.Start();
  }
  void OnActionTimer() {
   actionTimer.Stop();if(closed||Paused)return;
   var due=resetActions.NextCheck;if(!due.HasValue)return;
   if(due.Value>utcNow()){ScheduleActionCheck();return;}
   if(busy)refreshQueued=true;else RefreshNow();
  }
  void ArmPolling() {
   pollTimer.Stop();
   if(!autoRefresh||closed||Paused||busy||!polling.NextPollAt.HasValue)return;
   var remaining=polling.NextPollAt.Value-monotonicNow();
   pollTimer.Interval=remaining>TimeSpan.FromMilliseconds(1)?remaining:TimeSpan.FromMilliseconds(1);
   pollTimer.Start();
  }
  internal static string Duration(TimeSpan span) {
   long seconds=(long)Math.Ceiling(Math.Max(0,span.TotalSeconds));
   if(seconds>=86400)return (seconds/86400)+"d "+((seconds%86400)/3600)+"h";
   if(seconds>=3600)return (seconds/3600)+"h "+((seconds%3600)/60)+"m";
   if(seconds>=60)return (seconds/60)+"m "+(seconds%60)+"s";
   return seconds+"s";
  }
  static string Timestamp(DateTimeOffset value) {var local=value.ToLocalTime();return local.ToString("F")+" (UTC"+local.ToString("zzz")+")";}
  void SetResetBackgroundBars(bool enabled){resetBackgroundBars=enabled;resetBars.IsChecked=enabled;UpdateState();SavePosition();}
  void SetLabelPrefix(bool show) {showLabelPrefixes=show;labelPrefixes.IsChecked=show;UpdateState();SavePosition();}
  void UpdateState() {
   UpdateResetActionIndicator();
   pause.Header=Paused?"Resume refresh":"Pause refresh";
   var now=utcNow();
   bool stale=snapshot!=null&&(error!=null||(now-lastRead).TotalSeconds>=Math.Max(180,polling.CurrentInterval.TotalSeconds+30));
   state.Text=Paused?"PAUSED":busy?"READING":error!=null?"OFFLINE":stale?"STALE":snapshot==null?"CONNECTING":"LIVE";
   state.Foreground=Theme.Color(Paused||stale||error!=null?"Warning":"Accent");
   if(snapshot!=null) {
    five.Set(snapshot.FiveHour,stale,now,Theme);week.Set(snapshot.Weekly,stale,now,Theme);
    age.Text=(Paused?"Paused · ":stale?"Last known · ":"Updated · ")+lastRead.ToLocalTime().ToString("h:mm:ss tt");
   }else {five.Set(null,false,now,Theme);week.Set(null,false,now,Theme);age.Text=error!=null?"Usage unavailable":"Reading your Codex account…";}
   var details="5-hour: "+five.Details+"\n\nWeekly: "+week.Details+"\n\nUpdated"+(stale?" (last known)":"")+": "+(snapshot!=null&&lastRead!=DateTimeOffset.MinValue?Timestamp(lastRead):"—");
   if(isMinimal) {
    minimalBubble.SetUsage(snapshot,stale,Paused,now,state.Text,minimalCountdown,showLabelPrefixes,resetBackgroundBars);minimalBubble.SetHoverDetails("5-hour: "+five.Details,"Weekly: "+week.Details);
   }
   ToolTip=null;state.ToolTip=null;age.ToolTip=details;
   System.Windows.Automation.AutomationProperties.SetHelpText(this,details);
  }
  void UpdateResetActionIndicator() {
   bool enabled=resetActions.State.Armed||resetActions.Running;
   resetActionDot.Fill=enabled?(themeIndex==2?resetActionGreenLight:resetActionGreen):Theme.Color("Muted");
   System.Windows.Automation.AutomationProperties.SetName(resetActionDot,enabled?"Reset action on":"Reset action off");
			if (trayAutoMessage != null) {
				trayAutoMessage.Image = enabled ? trayAutoMessageOnImage : trayAutoMessageOffImage;
				trayAutoMessage.AccessibleName = enabled ? "Auto-msg on" : "Auto-msg off";
			}
  }
 }
}
