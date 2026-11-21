using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexUsageBubble {
 public sealed partial class BubbleWindow : Window {
  public BubbleWindow(bool autoRefresh) : this(autoRefresh,null,null,null) { }
  internal BubbleWindow(bool autoRefresh,Func<CancellationToken,Task<UsageSnapshot>> reader,Func<TimeSpan> time,Func<DateTimeOffset> wall) : this(autoRefresh,reader,time,wall,null) { }
  internal BubbleWindow(bool autoRefresh,Func<CancellationToken,Task<UsageSnapshot>> reader,Func<TimeSpan> time,Func<DateTimeOffset> wall,string settingsPath) : this(autoRefresh,reader,time,wall,settingsPath,null) { }
  internal BubbleWindow(bool autoRefresh,Func<CancellationToken,Task<UsageSnapshot>> reader,Func<TimeSpan> time,Func<DateTimeOffset> wall,string settingsPath,ResetActionCoordinator actions) {
   positionFile=settingsPath??Environment.GetEnvironmentVariable("CODEX_USAGE_BUBBLE_SETTINGS")??Path.Combine(dataDir,"position.json");
   preferencesStore=new WidgetPreferencesStore(positionFile);
   nativeSnapping=new NativeSnapping(this);
   this.autoRefresh=autoRefresh;
   foreach(System.Collections.DictionaryEntry entry in Theme.Resources)Resources[entry.Key]=entry.Value;
   var elapsed=System.Diagnostics.Stopwatch.StartNew();
   monotonicNow=time??(()=>elapsed.Elapsed);utcNow=wall??(()=>DateTimeOffset.UtcNow);
   readUsage=reader??(token=>Task.Run(()=>CodexClient.Read(token)));
   string actionFile=reader==null?Path.Combine(Path.GetDirectoryName(positionFile),"reset-action.json"):Path.Combine(Path.GetTempPath(),"CodexUsageBubble-tests",Guid.NewGuid().ToString("N")+".json");
   resetActions=actions??new ResetActionCoordinator(actionFile,utcNow,null);
   resetActions.Changed+=delegate{if(!closed)Dispatcher.BeginInvoke(new Action(()=>{ScheduleActionCheck();UpdateResetActionIndicator();}));};
   Title="CodexUsageWidget";Width=272;MinWidth=232;MaxWidth=380;Height=NormalHeight;MinHeight=NormalHeight;MaxHeight=NormalHeight;Topmost=true;ShowInTaskbar=false;ShowActivated=false;
   WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;
   FontFamily=new FontFamily("Segoe UI");FontSize=11;SetResourceReference(ForegroundProperty,"Text");
   var body=new StackPanel();
   var header=new DockPanel {Margin=new Thickness(0,0,0,5)};
   var title=new TextBlock {Text="CODEX",FontWeight=FontWeights.SemiBold,FontSize=10,Foreground=Theme.Color("Secondary"),VerticalAlignment=VerticalAlignment.Center};
   state.FontSize=9;state.HorizontalAlignment=HorizontalAlignment.Right;state.Text="CONNECTING";
   title.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");
   DockPanel.SetDock(state,Dock.Right);header.Children.Add(state);header.Children.Add(title);body.Children.Add(header);
   five=new UsageRow("5-hour");week=new UsageRow("Weekly");body.Children.Add(five.Root);body.Children.Add(week.Root);
   age.FontSize=9;age.HorizontalAlignment=HorizontalAlignment.Left;age.SetResourceReference(TextBlock.ForegroundProperty,"Muted");age.Margin=new Thickness(0,1,0,0);age.Text="Reading your Codex account…";body.Children.Add(age);
   var surface=new Grid();normalSurface=surface;minimalBubble=new MinimalBubble();
   var normalFrame=new Border {CornerRadius=new CornerRadius(19),BorderThickness=new Thickness(1),Padding=new Thickness(15,10,15,6),Child=body};
   normalFrame.SetResourceReference(Border.BackgroundProperty,"Surface");normalFrame.SetResourceReference(Border.BorderBrushProperty,"Outline");surface.Children.Add(normalFrame);
   foreach(bool leftEdge in new[]{true,false}) {
    bool fromLeft=leftEdge;
    var handle=new System.Windows.Controls.Primitives.Thumb {Width=7,HorizontalAlignment=fromLeft?HorizontalAlignment.Left:HorizontalAlignment.Right,Margin=new Thickness(0,17,0,17),Cursor=Cursors.SizeWE};
    var hitArea=new FrameworkElementFactory(typeof(Border));hitArea.SetValue(Border.BackgroundProperty,Brushes.Transparent);
    handle.Template=new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) {VisualTree=hitArea};
    handle.DragDelta+=delegate(object sender,System.Windows.Controls.Primitives.DragDeltaEventArgs e){ResizeHorizontal(e.HorizontalChange,fromLeft);e.Handled=true;};
    handle.DragCompleted+=delegate{SavePosition();};surface.Children.Add(handle);
   }
   Content=surface;
   ContextMenu=CreateContextMenu();
   ContextMenu.Items.Add(minimalMode);
   var settings=new MenuItem {Header="Settings"};
   var themes=new MenuItem {Header="Theme"};
   for(int i=0;i<themeChoices.Length;i++){int selected=i;var item=new MenuItem {Header=WidgetTheme.All[i].Name,IsCheckable=true,IsChecked=i==themeIndex};item.Click+=delegate{SetTheme(selected);};themeChoices[i]=item;themes.Items.Add(item);}
   settings.Items.Add(themes);
   using(var stream=typeof(BubbleWindow).Assembly.GetManifestResourceStream("CodexUsageBubble.ring.ico")) {
    if(stream!=null) {
     var image=System.Windows.Media.Imaging.BitmapFrame.Create(stream,System.Windows.Media.Imaging.BitmapCreateOptions.None,System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
     image.Freeze();Icon=image;
    }
   }
   var sizes=new MenuItem {Header="Minimal size"};string[] sizeNames={"Compact (40)","Small (60)","Medium (80)","Large (100)"};
   for(int i=0;i<minimalSizes.Length;i++){int selected=minimalSizes[i];var item=new MenuItem {Header=sizeNames[i],IsCheckable=true,IsChecked=selected==minimalDiameter};item.Click+=delegate{SetMinimalSize(selected);};sizeChoices[i]=item;sizes.Items.Add(item);}
   settings.Items.Add(sizes);
   var shapes=new MenuItem {Header="Minimal shape"};
   for(int i=0;i<shapeChoices.Length;i++){var selected=(MinimalShape)i;var item=new MenuItem {Header=selected.ToString(),IsCheckable=true,IsChecked=selected==minimalShape};item.Click+=delegate{SetMinimalShape(selected);};shapeChoices[i]=item;shapes.Items.Add(item);}
   settings.Items.Add(shapes);
   foreach(var item in new[]{sizes,shapes,labelPrefixes,resetBars})item.SetBinding(MenuItem.VisibilityProperty,new System.Windows.Data.Binding("IsChecked"){Source=minimalMode,Converter=new BooleanToVisibilityConverter()});
   labelPrefixes.Click+=delegate{SetLabelPrefix(labelPrefixes.IsChecked);};settings.Items.Add(labelPrefixes);
   resetBars.Checked+=delegate{SetResetBackgroundBars(true);};resetBars.Unchecked+=delegate{SetResetBackgroundBars(false);};settings.Items.Add(resetBars);
   snapping.Checked+=delegate{SavePosition();};snapping.Unchecked+=delegate{nativeSnapping.Detach();SavePosition();};settings.Items.Add(snapping);
			closeToTray.Click += delegate { SavePosition(); };
			settings.Items.Add(closeToTray);
   ContextMenu.Items.Add(settings);ContextMenu.Items.Add(new Separator {Style=(Style)ContextMenu.Resources[typeof(Separator)]});
   var afterReset=new MenuItem {Header="Auto Reset Messages",Icon=resetActionDot};
   afterReset.Click+=delegate {
    if(actionWindow==null){actionWindow=new ResetActionWindow(resetActions,Theme,()=>snapshot,()=>Paused) {Owner=this,Icon=Icon};actionWindow.Closed+=delegate{actionWindow=null;};actionWindow.Show();}
    else actionWindow.Activate();
   };
   ContextMenu.Items.Add(afterReset);
   ContextMenu.Items.Add(pause);ContextMenu.Items.Add(refresh);ContextMenu.Items.Add(new Separator {Style=(Style)ContextMenu.Resources[typeof(Separator)]});
   var close=new MenuItem {Header="Close"};close.Click+=delegate{Close();};ContextMenu.Items.Add(close);
   pause.Click+=delegate{
    if(Paused){polling.Resume(monotonicNow());resetActions.SetPaused(false);UpdateState();if(this.autoRefresh){if(busy)refreshQueued=true;else RefreshNow();}}
    else {polling.Pause();resetActions.SetPaused(true);pollTimer.Stop();actionTimer.Stop();refreshQueued=false;if(pending!=null)pending.Cancel();UpdateState();}
   };
   refresh.Click+=delegate{RefreshNow();};
   minimalMode.Click+=delegate{SetMinimalMode(!isMinimal);};
   MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){
    if(HandleMinimalDoubleClick(e.ClickCount)){dragStart=null;e.Handled=true;return;}
    if(e.ButtonState==MouseButtonState.Pressed)dragStart=e.GetPosition(this);
   };
   MouseLeftButtonUp+=delegate{dragStart=null;};
   MouseMove+=delegate(object sender,MouseEventArgs e){
    if(e.LeftButton!=MouseButtonState.Pressed){dragStart=null;return;}
    if(!dragStart.HasValue)return;var delta=e.GetPosition(this)-dragStart.Value;
    if(Math.Abs(delta.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(delta.Y)<SystemParameters.MinimumVerticalDragDistance)return;
    dragStart=null;nativeSnapping.BeginDrag();try{DragMove();SavePosition();}catch(InvalidOperationException){}finally{nativeSnapping.EndDrag();}
   };
			Loaded += delegate {
				if (initialized) { return; }

				initialized = true;
				LoadPosition();
				CreateTrayIcon();
				if (this.autoRefresh) { clock.Start(); RefreshNow(); }
			};
			Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
				if (!allowExit && closeToTray.IsChecked && trayIcon != null && trayIcon.Visible) {
					e.Cancel = true;
					HideToTray();
				}
				SavePosition();
			};
   // UI-only countdown clock: it never initiates an account request.
   clock.Interval=TimeSpan.FromSeconds(1);clock.Tick+=delegate {UpdateState();};
   pollTimer.Tick+=delegate {pollTimer.Stop();if(!closed&&!Paused){if(polling.IsDue(monotonicNow()))RefreshNow();else ArmPolling();}};
   actionTimer.Tick+=delegate {OnActionTimer();};
   ContextMenuOpening+=delegate{minimalBubble.CancelHover();UpdateResetActionIndicator();};
			IsVisibleChanged += delegate { UpdateTrayMenu(); };
   // Persist native follow/DPI moves as well as explicit drags, without writing every frame.
   placementSaveTimer.Tick+=delegate{placementSaveTimer.Stop();SavePosition();};
   LocationChanged+=delegate{if(initialized&&!loadingPosition&&!closed){placementSaveTimer.Stop();placementSaveTimer.Start();}};
   Closed+=delegate {placementSaveTimer.Stop();DisposeTrayIcon();nativeSnapping.Dispose();minimalBubble.CancelHover();closed=true;clock.Stop();pollTimer.Stop();actionTimer.Stop();resetActions.Dispose();polling.Pause();refreshQueued=false;if(pending!=null)pending.Cancel();};
   ApplyTheme();UpdateState();
  }
  void SetTheme(int index) {
   if(index<0||index>=WidgetTheme.All.Length)return;
   themeIndex=index;ApplyTheme();UpdateState();SavePosition();
  }
  void ApplyTheme() {
   foreach(System.Collections.DictionaryEntry entry in Theme.Resources){Resources[entry.Key]=entry.Value;ContextMenu.Resources[entry.Key]=entry.Value;}
   for(int i=0;i<themeChoices.Length;i++)themeChoices[i].IsChecked=i==themeIndex;
   minimalBubble.SetTheme(Theme);
  }
  internal bool HandleMinimalDoubleClick(int count) {
   if(!isMinimal||count!=2)return false;
   minimalCountdown=!minimalCountdown;UpdateState();SavePosition();return true;
  }
  internal void ResizeHorizontal(double delta,bool fromLeft) {
   if(isMinimal||double.IsNaN(delta)||double.IsInfinity(delta))return;
   nativeSnapping.Detach();
   double oldWidth=Width;
   Width=Math.Max(MinWidth,Math.Min(MaxWidth,oldWidth+(fromLeft?-delta:delta)));
   if(fromLeft&&!double.IsNaN(Left))Left+=oldWidth-Width;
  }
  internal void SetMinimalMode(bool enabled) {
   if(isMinimal==enabled)return;
   if(enabled)normalWidth=Width;
   minimalBubble.CancelHover();isMinimal=enabled;minimalMode.IsChecked=enabled;
   ApplyWindowLayout();UpdateState();SavePosition();
  }
  void SetMinimalSize(int diameter) {
   if(Array.IndexOf(minimalSizes,diameter)<0)return;
   minimalDiameter=diameter;minimalBubble.SetDiameter(diameter);
   for(int i=0;i<sizeChoices.Length;i++)sizeChoices[i].IsChecked=minimalSizes[i]==diameter;
   if(isMinimal)ApplyWindowLayout();
   UpdateState();SavePosition();
  }
  void SetMinimalShape(MinimalShape shape) {
   if(shape!=MinimalShape.Circle&&shape!=MinimalShape.Square)return;
   minimalShape=shape;minimalBubble.SetShape(shape);
   for(int i=0;i<shapeChoices.Length;i++)shapeChoices[i].IsChecked=i==(int)shape;
   UpdateState();SavePosition();
  }
  void ApplyWindowLayout() {
   var placement=IsLoaded?nativeSnapping.CapturePlacement():null;
   nativeSnapping.Detach();
   bool enabled=isMinimal;
   MinWidth=0;MaxWidth=double.PositiveInfinity;MinHeight=0;MaxHeight=double.PositiveInfinity;
   Width=enabled?minimalDiameter:normalWidth;Height=enabled?minimalDiameter:NormalHeight;
   MinWidth=enabled?minimalDiameter:232;MaxWidth=enabled?minimalDiameter:380;
   MinHeight=Height;MaxHeight=Height;
   Content=enabled?(object)minimalBubble:normalSurface;
   if(IsLoaded&&!loadingPosition)nativeSnapping.RestorePlacement(placement);
  }
  // Replace every system template, including popup items, so OS light-theme
  // gutters, checkbox tiles and cyan selection borders cannot leak through.
  static ContextMenu CreateContextMenu() {
   return (ContextMenu)System.Windows.Markup.XamlReader.Parse(@"
<ContextMenu xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
 Background='{DynamicResource MenuBackground}' Foreground='{DynamicResource Text}' BorderBrush='{DynamicResource Outline}'
 FontFamily='Segoe UI' FontSize='11' MinWidth='168' Padding='5'
 SnapsToDevicePixels='True' UseLayoutRounding='True' HasDropShadow='False'>
 <ContextMenu.Resources>
  <Style TargetType='{x:Type MenuItem}'>
   <Setter Property='Foreground' Value='{DynamicResource Text}'/>
   <Setter Property='Background' Value='Transparent'/>
   <Setter Property='Padding' Value='8,5'/>
   <Setter Property='MinHeight' Value='27'/>
   <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type MenuItem}'>
      <Grid SnapsToDevicePixels='True'>
       <Border x:Name='MenuRow' Background='{TemplateBinding Background}' CornerRadius='4' Padding='{TemplateBinding Padding}'>
        <Grid>
         <Grid.ColumnDefinitions><ColumnDefinition Width='18'/><ColumnDefinition Width='*'/><ColumnDefinition Width='15'/></Grid.ColumnDefinitions>
         <ContentPresenter Grid.Column='0' ContentSource='Icon' HorizontalAlignment='Left' VerticalAlignment='Center'/>
         <Path x:Name='CheckMark' Grid.Column='0' Data='M 1,5 L 4,8 L 10,1' Stroke='{DynamicResource Accent}' StrokeThickness='1.7' StrokeStartLineCap='Round' StrokeEndLineCap='Round' Width='11' Height='10' HorizontalAlignment='Left' VerticalAlignment='Center' Visibility='Collapsed'/>
         <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center' Margin='0,0,8,0'/>
         <Path x:Name='SubmenuArrow' Grid.Column='2' Data='M 1,1 L 5,5 L 1,9' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.3' Width='6' Height='10' HorizontalAlignment='Right' VerticalAlignment='Center' Visibility='Collapsed'/>
        </Grid>
       </Border>
       <Popup x:Name='PART_Popup' Placement='Right' PlacementTarget='{Binding RelativeSource={RelativeSource TemplatedParent}}' HorizontalOffset='2' VerticalOffset='-5'
        IsOpen='{Binding IsSubmenuOpen,RelativeSource={RelativeSource TemplatedParent}}' AllowsTransparency='True' Focusable='False' PopupAnimation='None'>
        <Border x:Name='SubmenuFrame' Background='{DynamicResource MenuBackground}' BorderBrush='{DynamicResource Outline}' BorderThickness='1' CornerRadius='8' Padding='5' MinWidth='168' SnapsToDevicePixels='True'>
         <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/>
        </Border>
       </Popup>
      </Grid>
      <ControlTemplate.Triggers>
       <Trigger Property='IsChecked' Value='True'><Setter TargetName='CheckMark' Property='Visibility' Value='Visible'/></Trigger>
       <Trigger Property='HasItems' Value='True'><Setter TargetName='SubmenuArrow' Property='Visibility' Value='Visible'/></Trigger>
       <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='MenuRow' Property='Background' Value='{DynamicResource MenuHighlight}'/></Trigger>
       <Trigger Property='IsSubmenuOpen' Value='True'><Setter TargetName='MenuRow' Property='Background' Value='{DynamicResource MenuHighlight}'/></Trigger>
       <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='{DynamicResource DisabledText}'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>
  <Style TargetType='{x:Type Separator}'>
   <Setter Property='Template'>
    <Setter.Value><ControlTemplate TargetType='{x:Type Separator}'><Border Height='1' Background='{DynamicResource MenuSeparator}' Margin='8,4'/></ControlTemplate></Setter.Value>
   </Setter>
  </Style>
 </ContextMenu.Resources>
 <ContextMenu.Template>
  <ControlTemplate TargetType='{x:Type ContextMenu}'>
   <Border x:Name='MenuFrame' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8' Padding='{TemplateBinding Padding}' SnapsToDevicePixels='True'>
    <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle' KeyboardNavigation.TabNavigation='Cycle'/>
   </Border>
  </ControlTemplate>
 </ContextMenu.Template>
</ContextMenu>");
  }
  static SolidColorBrush Brush(string hex) {var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();return b;}
  sealed class UsageRow {
   internal readonly StackPanel Root=new StackPanel {Margin=new Thickness(0,0,0,4)};
   readonly TextBlock percent=new TextBlock(), reset=new TextBlock();
   readonly Border fill=new Border {CornerRadius=new CornerRadius(3),HorizontalAlignment=HorizontalAlignment.Left};
   readonly Grid track=new Grid {Height=5,Margin=new Thickness(0,4,0,3)};
   internal UsageRow(string name) {
    var labels=new DockPanel();percent.HorizontalAlignment=HorizontalAlignment.Right;percent.FontWeight=FontWeights.SemiBold;DockPanel.SetDock(percent,Dock.Right);labels.Children.Add(percent);var caption=new TextBlock {Text=name};caption.SetResourceReference(TextBlock.ForegroundProperty,"Secondary");labels.Children.Add(caption);Root.Children.Add(labels);
    var trackBody=new Border {CornerRadius=new CornerRadius(3)};trackBody.SetResourceReference(Border.BackgroundProperty,"Track");track.Children.Add(trackBody);track.Children.Add(fill);Root.Children.Add(track);
    reset.FontSize=9;reset.SetResourceReference(TextBlock.ForegroundProperty,"Muted");Root.Children.Add(reset);
    track.SizeChanged+=delegate {if(current!=null)fill.Width=Math.Max(0,track.ActualWidth*current.Remaining/100);};
   }
   internal string Details {get;private set;}
   void SetDetails(string value){Details=value;track.ToolTip=value;}
   UsageWindow current;
   internal void Set(UsageWindow value,bool stale,DateTimeOffset now,WidgetTheme theme) {
    current=value;
    if(value==null){percent.Text="—";reset.Text="Reset unavailable";fill.Width=0;SetDetails("—\nResets: —");return;}
    bool expired=value.IsExpired(now);
    percent.Text=expired?"—":value.Remaining.ToString("0.#")+"% left";
    fill.Width=expired?0:Math.Max(0,track.ActualWidth*value.Remaining/100);
    fill.Background=theme.Color(stale||expired?"Muted":value.Remaining<=15?"Critical":value.Remaining<=35?"Warning":"Accent");
    reset.Text=expired?"Reset passed · waiting for live read":!value.HasReset?"Reset time unavailable":"Resets in "+Duration(value.Reset-now)+" · "+value.Reset.ToLocalTime().ToString("ddd h:mm tt");
    string resetInfo=!value.HasReset?"Resets: —":(expired?"Reset timestamp: ":"Resets: ")+Timestamp(value.Reset);
    SetDetails(value.Remaining.ToString("0.#")+"% remaining"+(stale||expired?" (last known)":"")+"\n"+resetInfo);
   }
  }
  }
}
