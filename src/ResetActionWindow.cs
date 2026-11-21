using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexUsageBubble {
 internal sealed class ResetActionWindow : Window {
  readonly ResetActionCoordinator coordinator;
  readonly Func<UsageSnapshot> usage;
  readonly Func<bool> paused;
  readonly Func<string,CancellationToken,ChatPage> catalog;
  readonly CancellationTokenSource lifetime=new CancellationTokenSource();
  readonly List<ChatEntry> chats=new List<ChatEntry>();
  readonly HashSet<string> cursors=new HashSet<string>(StringComparer.Ordinal);
  readonly RadioButton existing=new RadioButton {Content="Existing chat",IsChecked=true,Margin=new Thickness(0,0,16,0)},newChat=new RadioButton {Content="New chat",Margin=new Thickness(0,0,16,0)},dedicatedChat=new RadioButton {Content="Dedicated chat"};
  readonly CheckBox repeat=new CheckBox {Content="Repeat after every five-hour reset",Margin=new Thickness(0,8,0,0)};
  readonly TextBox search=new TextBox(),project=new TextBox(),message=new TextBox {AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,Height=64,VerticalScrollBarVisibility=ScrollBarVisibility.Hidden,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxLength=20000};
  readonly ListBox list=new ListBox {MinHeight=54,HorizontalContentAlignment=HorizontalAlignment.Stretch};
		readonly DockPanel chatPicker = new DockPanel();
		readonly DockPanel searchRow = new DockPanel { Margin=new Thickness(0,0,0,6) };
  readonly TextBlock selectedText=new TextBlock {TextWrapping=TextWrapping.NoWrap,TextTrimming=TextTrimming.CharacterEllipsis,FontSize=11,Height=30},status=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=11},catalogStatus=new TextBlock {TextWrapping=TextWrapping.Wrap,FontSize=11};
  readonly TextBlock projectLabel=new TextBlock {Text="Project folder",Width=80,VerticalAlignment=VerticalAlignment.Center};
  readonly DockPanel projectRow=new DockPanel {Margin=new Thickness(0,8,0,0),Height=24};
  readonly WrapPanel presets=new WrapPanel();
  readonly Button reviewContinue=new Button {Content="Review or continue"};
  readonly Button arm=new Button {Content="Arm"},more=new Button {Content="Load more"},reload=new Button {Content="Reload chats"},cancel=new Button {Content="Cancel pending action"};
		readonly Button manualButton = new Button { Content="Manually set time" };
		readonly DatePicker manualDate = new DatePicker { SelectedDateFormat=DatePickerFormat.Short, Width=122 };
		readonly TextBox manualTime = new TextBox { Width=74, MaxLength=8, Margin=new Thickness(6,0,0,0), ToolTip="Local time (24-hour, HH:mm:ss)" };
		readonly TextBlock manualError = new TextBlock { TextWrapping=TextWrapping.Wrap, FontSize=11, Visibility=Visibility.Collapsed };
		readonly Border manualTimePanel = new Border { Visibility=Visibility.Collapsed, Padding=new Thickness(6), Margin=new Thickness(8,0,0,0), CornerRadius=new CornerRadius(4), BorderThickness=new Thickness(1), Width=230, VerticalAlignment=VerticalAlignment.Center, HorizontalAlignment=HorizontalAlignment.Right };
		long manualSendUnix;
  readonly DispatcherTimer clock=new DispatcherTimer {Interval=TimeSpan.FromSeconds(1)};
  ChatEntry selected;
  string cursor;
  string workMessage,newMessage;
  int displayedDestination=-1;
  bool usable,loading,closed,lifetimeDisposed;
  internal ResetActionWindow(ResetActionCoordinator coordinator,WidgetTheme theme,Func<UsageSnapshot> usage,Func<bool> paused,Func<string,CancellationToken,ChatPage> catalog=null) {
   this.coordinator=coordinator;this.usage=usage;this.paused=paused;this.catalog=catalog??ChatActions.List;
   Title="Auto Reset Messages";Width=560;Height=580;MinWidth=500;MinHeight=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;FontFamily=new FontFamily("Segoe UI");FontSize=12;
   Background=theme.Color("Surface");Foreground=theme.Color("Text");
   foreach(System.Collections.DictionaryEntry item in theme.Resources)Resources[item.Key]=item.Value;
   foreach(var type in new[]{typeof(RadioButton),typeof(CheckBox)}) {var toggleStyle=new Style(type);toggleStyle.Setters.Add(new Setter(Control.ForegroundProperty,theme.Color("Text")));Resources.Add(type,toggleStyle);}
   var inputs=new Style(typeof(TextBox));inputs.Setters.Add(new Setter(Control.BackgroundProperty,theme.Color("Track")));inputs.Setters.Add(new Setter(Control.ForegroundProperty,theme.Color("Text")));inputs.Setters.Add(new Setter(Control.BorderBrushProperty,theme.Color("Outline")));inputs.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,3,5,3)));inputs.Setters.Add(new Setter(TextBox.CaretBrushProperty,theme.Color("Text")));Resources.Add(typeof(TextBox),inputs);
   var buttons=(Style)System.Windows.Markup.XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type Button}'>
 <Setter Property='Padding' Value='8,3'/><Setter Property='Margin' Value='0,0,6,0'/>
 <Setter Property='Background' Value='{DynamicResource MenuBackground}'/><Setter Property='Foreground' Value='{DynamicResource Text}'/>
 <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='{x:Type Button}'>
  <Border x:Name='Frame' Background='{TemplateBinding Background}' BorderBrush='{DynamicResource Outline}' BorderThickness='1' CornerRadius='2' Padding='{TemplateBinding Padding}'>
   <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' RecognizesAccessKey='True'/>
  </Border>
  <ControlTemplate.Triggers>
   <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Background' Value='{DynamicResource MenuHighlight}'/></Trigger>
   <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
   <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
  </ControlTemplate.Triggers>
 </ControlTemplate></Setter.Value></Setter>
</Style>");Resources.Add(typeof(Button),buttons);
   list.Background=theme.Color("Track");list.Foreground=theme.Color("Text");list.BorderBrush=theme.Color("Outline");
   ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
   list.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='{x:Type ListBox}'>
 <Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
  <ScrollViewer Focusable='False' CanContentScroll='True' HorizontalScrollBarVisibility='{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}' VerticalScrollBarVisibility='Auto'>
   <ItemsPresenter/>
  </ScrollViewer>
 </Border>
 <ControlTemplate.Triggers>
  <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
 </ControlTemplate.Triggers>
</ControlTemplate>");
   var items=new Style(typeof(ListBoxItem));items.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(5,2,5,2)));items.Setters.Add(new Setter(FrameworkElement.HeightProperty,54.0));items.Setters.Add(new Setter(Control.ForegroundProperty,theme.Color("Text")));var selectedTrigger=new Trigger {Property=ListBoxItem.IsSelectedProperty,Value=true};selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty,theme.Color("MenuHighlight")));items.Triggers.Add(selectedTrigger);list.ItemContainerStyle=items;
   var template=new DataTemplate(typeof(ChatEntry));var row=new FrameworkElementFactory(typeof(StackPanel));
   foreach(string property in new[]{"DisplayTitle","FolderDisplay","ActivityDisplay"}) {
    var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(property));text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.NoWrap);text.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
    text.SetValue(TextBlock.FontSizeProperty,property=="DisplayTitle"?12.0:11.0);
    if(property=="DisplayTitle")text.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);
    else text.SetValue(TextBlock.ForegroundProperty,theme.Color("Secondary"));
    if(property=="FolderDisplay")text.SetBinding(TextBlock.ToolTipProperty,new System.Windows.Data.Binding("FolderDisplay"));
    row.AppendChild(text);
   }
   template.VisualTree=row;list.ItemTemplate=template;
   var root=new DockPanel {Margin=new Thickness(12)};Content=root;
   var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
			Grid statusArea = new Grid { Margin=new Thickness(0,8,0,6), MinHeight=76 };
			statusArea.ColumnDefinitions.Add(new ColumnDefinition());
			statusArea.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
			status.VerticalAlignment = VerticalAlignment.Center;
			status.MaxHeight = 112;
			status.TextTrimming = TextTrimming.CharacterEllipsis;
			statusArea.Children.Add(status);
			Grid.SetColumn(manualTimePanel, 1);
			statusArea.Children.Add(manualTimePanel);
			footer.Children.Add(statusArea);
			BuildManualTimeSelector(theme);
   var footerButtons=new DockPanel();var close=new Button {Content="Close",MinWidth=60,Margin=new Thickness(0),VerticalAlignment=VerticalAlignment.Bottom};close.Click+=delegate{Close();};DockPanel.SetDock(close,Dock.Right);footerButtons.Children.Add(close);
   var controls=new WrapPanel();foreach(var button in new[]{arm,cancel,manualButton})controls.Children.Add(button);footerButtons.Children.Add(controls);footer.Children.Add(footerButtons);
			DockPanel body = new DockPanel();
			root.Children.Add(body);
			StackPanel editor = new StackPanel();
			DockPanel.SetDock(editor, Dock.Bottom);
			body.Children.Add(editor);
			editor.Children.Add(selectedText);
			DockPanel.SetDock(projectLabel, Dock.Left);
			projectRow.Children.Add(projectLabel);
			projectRow.Children.Add(project);
			editor.Children.Add(projectRow);
			DockPanel messageHeader = new DockPanel { Margin=new Thickness(0,8,0,4) };
			DockPanel.SetDock(presets, Dock.Right);
			messageHeader.Children.Add(presets);
			messageHeader.Children.Add(new TextBlock { Text="Message", VerticalAlignment=VerticalAlignment.Center });
			editor.Children.Add(messageHeader);
			editor.Children.Add(message);
			editor.Children.Add(repeat);
			WrapPanel destination = new WrapPanel();
			destination.Children.Add(existing);
			destination.Children.Add(newChat);
			destination.Children.Add(dedicatedChat);
			DockPanel.SetDock(destination, Dock.Top);
			body.Children.Add(destination);
			TextBlock hint = new TextBlock { Text="Sends at reset + 60s, or at your manual time. Keep the widget running.\nExisting chats can receive messages while Codex stays open; active chats wait.", FontSize=11, TextWrapping=TextWrapping.Wrap, Foreground=theme.Color("Secondary"), Margin=new Thickness(0,6,0,8), ToolTip="Existing and saved Dedicated chats use the running Codex app when available. Codex handles their work and approvals. Acknowledgement confirms delivery, not completion. New chat creates a chat for each send. Background chats may lack desktop-only tools." };
			DockPanel.SetDock(hint, Dock.Top);
			body.Children.Add(hint);
			body.Children.Add(chatPicker);
			TextBlock searchLabel = new TextBlock { Text="Search", Width=48, VerticalAlignment=VerticalAlignment.Center };
			DockPanel.SetDock(searchLabel, Dock.Left);
			searchRow.Children.Add(searchLabel);
			search.ToolTip = "Search loaded chats by title, folder, or ID. Load more to include older chats.";
			StackPanel catalogButtons = new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right };
			more.Margin = new Thickness(0);
			catalogButtons.Children.Add(reload);
			catalogButtons.Children.Add(more);
			DockPanel.SetDock(catalogButtons, Dock.Right);
			catalogButtons.Margin = new Thickness(6,0,0,0);
			searchRow.Children.Add(catalogButtons);
			searchRow.Children.Add(search);
			DockPanel.SetDock(searchRow, Dock.Top);
			chatPicker.Children.Add(searchRow);
			catalogStatus.Margin = new Thickness(0,4,0,4);
			catalogStatus.Foreground = theme.Color("Secondary");
			DockPanel.SetDock(catalogStatus, Dock.Bottom);
			chatPicker.Children.Add(catalogStatus);
			chatPicker.Children.Add(list);
			Button continuation = new Button { Content="Continue" };
			continuation.Click += delegate { message.Text=ResetMessages.Continue; };
			Button acknowledgement = new Button { Content="Short acknowledgement", Margin=new Thickness(0) };
			acknowledgement.Click += delegate { message.Text=ResetMessages.Acknowledgement; };
			reviewContinue.Click += delegate { message.Text=ResetMessages.ReviewOrContinue; };
			presets.Children.Add(continuation);
			presets.Children.Add(reviewContinue);
			presets.Children.Add(acknowledgement);
   var saved=coordinator.State;dedicatedChat.IsChecked=saved.DedicatedChat;newChat.IsChecked=saved.NewChat&&!saved.DedicatedChat;existing.IsChecked=!saved.NewChat&&!saved.DedicatedChat;repeat.IsChecked=saved.Repeat;
			manualSendUnix = saved.Armed ? 0 : saved.ManualSendUnix;
   workMessage=!saved.NewChat&&!saved.DedicatedChat?saved.Message??ResetMessages.Continue:ResetMessages.Continue;
   newMessage=saved.NewChat&&!saved.DedicatedChat?saved.Message??ResetMessages.Acknowledgement:ResetMessages.Acknowledgement;
   project.Text=saved.DedicatedChat?Environment.CurrentDirectory:saved.ProjectPath??Environment.CurrentDirectory;
   if(!string.IsNullOrWhiteSpace(saved.ThreadId))selected=new ChatEntry {Id=saved.ThreadId,Title=saved.ThreadTitle??saved.ThreadId,Cwd=saved.ProjectPath};
   search.TextChanged+=delegate{Filter();};list.SelectionChanged+=delegate {var entry=list.SelectedItem as ChatEntry;if(entry!=null){selected=entry;project.Text=entry.Cwd;}UpdateStatus();};
   existing.Checked+=delegate{DestinationChanged();};newChat.Checked+=delegate{DestinationChanged();};dedicatedChat.Checked+=delegate{DestinationChanged();};
   reload.Click+=delegate{LoadChats(true);};more.Click+=delegate{LoadChats(false);};
   arm.Click+=delegate {
    try {
     if(!usable)throw new InvalidOperationException("Codex is unavailable. Reload chats after opening or installing Codex.");
     if(existing.IsChecked==true&&(selected==null||!chats.Any(chat=>chat.Id==selected.Id)))throw new InvalidOperationException("Select a chat from the current catalog before arming. Use Load more to find older chats.");
     coordinator.Arm(new ResetActionSettings {NewChat=newChat.IsChecked==true,DedicatedChat=dedicatedChat.IsChecked==true,ThreadId=selected==null?null:selected.Id,ThreadTitle=selected==null?null:selected.DisplayTitle,ProjectPath=existing.IsChecked==true?selected.Cwd:project.Text.Trim(),Message=message.Text,Repeat=repeat.IsChecked==true,ManualSendUnix=coordinator.State.Armed?coordinator.State.ManualSendUnix:manualSendUnix},usage());manualSendUnix=0;UpdateStatus();
    }catch(Exception error){MessageBox.Show(this,error.Message,"Could not arm reset action",MessageBoxButton.OK,MessageBoxImage.Information);}
   };
   cancel.Click+=delegate{coordinator.CancelPending();manualSendUnix=0;UpdateStatus();};
			manualButton.Click += delegate { ToggleManualTimeSelector(); };
   Loaded+=delegate {LoadChats(true);clock.Start();};clock.Tick+=delegate{UpdateStatus();};
   Closed+=delegate{closed=true;clock.Stop();lifetime.Cancel();if(!loading)DisposeLifetime();};DestinationChanged();
  }
		void BuildManualTimeSelector(WidgetTheme theme) {
			manualTimePanel.Background = theme.Color("Track");
			manualTimePanel.BorderBrush = theme.Color("Outline");
			manualError.Foreground = theme.Color("Warning");
			StackPanel panel = new StackPanel();
			panel.Children.Add(new TextBlock { Text="Local date / time", FontSize=11, Margin=new Thickness(0,0,0,3) });
			StackPanel timeRow = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,0,0,4) };
			timeRow.Children.Add(manualDate);
			timeRow.Children.Add(manualTime);
			System.Windows.Automation.AutomationProperties.SetName(manualTime, "Local time, 24-hour HH:mm:ss");
			panel.Children.Add(timeRow);
			WrapPanel buttons = new WrapPanel();
			Button apply = new Button { Content="Set time" };
			apply.Click += delegate { ApplyManualTime(); };
			buttons.Children.Add(apply);
			Button reset = new Button { Content="Use reset time", Margin=new Thickness(0) };
			reset.Click += delegate {
				try { SetManualOverride(0); }
				catch (Exception error) { ShowManualTimeError(error.Message); }
			};
			buttons.Children.Add(reset);
			panel.Children.Add(buttons);
			panel.Children.Add(manualError);
			manualTimePanel.Child = panel;
		}

		void ToggleManualTimeSelector() {
			if (manualTimePanel.Visibility == Visibility.Visible) {
				manualTimePanel.Visibility = Visibility.Collapsed;
				return;
			}

			ResetActionSettings saved = coordinator.State;
			long custom = saved.Armed ? saved.ManualSendUnix : manualSendUnix;
			DateTimeOffset initial = custom > 0 ? ResetActionCoordinator.FromUnix(custom) : saved.Armed && saved.DueResetUnix > 0 ? ResetActionCoordinator.SendAt(saved) : coordinator.CurrentTime.AddMinutes(1);
			DateTime local = initial.LocalDateTime;
			manualDate.SelectedDate = local.Date;
			manualTime.Text = local.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
			manualError.Visibility = Visibility.Collapsed;
			manualTimePanel.Visibility = Visibility.Visible;
		}

		void ApplyManualTime() {
			DateTime entered;
			if (!manualDate.SelectedDate.HasValue || !DateTime.TryParseExact(manualTime.Text.Trim(), new[] { "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss" }, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out entered)) {
				ShowManualTimeError("Choose a date and a time such as 14:30.");
				return;
			}

			DateTime local = DateTime.SpecifyKind(manualDate.SelectedDate.Value.Date.Add(entered.TimeOfDay), DateTimeKind.Unspecified);
			if (TimeZoneInfo.Local.IsInvalidTime(local)) {
				ShowManualTimeError("This local time is skipped by daylight saving time. Choose another time.");
				return;
			}

			DateTimeOffset time = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
			if (time <= coordinator.CurrentTime) {
				ShowManualTimeError("Choose a send time in the future.");
				return;
			}

			try { SetManualOverride(ResetActionCoordinator.Unix(time)); }
			catch (Exception error) { ShowManualTimeError(error.Message); }
		}

		void SetManualOverride(long timestamp) {
			if (coordinator.State.Armed) {
				coordinator.SetManualSendTime(timestamp > 0 ? (DateTimeOffset?)ResetActionCoordinator.FromUnix(timestamp) : null);
				manualSendUnix = 0;
			}
			else {
				manualSendUnix = timestamp;
			}

			manualTimePanel.Visibility = Visibility.Collapsed;
			UpdateStatus();
		}

		void ShowManualTimeError(string error) {
			manualError.Text = error;
			manualError.Visibility = Visibility.Visible;
		}

  async void LoadChats(bool reset) {
   if(closed||loading)return;
   var token=lifetime.Token;
   loading=true;reload.IsEnabled=more.IsEnabled=false;catalogStatus.Text="Reading local Codex chats…";
   try {
    string requested=reset?null:cursor;
    var page=await Task.Run(()=>catalog(requested,token),token);
    if(closed||token.IsCancellationRequested)return;
    if(reset){chats.Clear();cursors.Clear();}
    if(page.Cursor!=null&&(page.Cursor==requested||!cursors.Add(page.Cursor)))throw new IOException("Codex repeated a chat-list page. Reload the catalog.");
    foreach(var chat in page.Chats)if(!chats.Any(old=>old.Id==chat.Id))chats.Add(chat);
    cursor=page.Cursor;usable=true;Filter();
    if(selected!=null){var match=chats.FirstOrDefault(chat=>chat.Id==selected.Id);if(match!=null){selected=match;list.SelectedItem=match;}}
   }catch(Exception error) {if(!closed&&!token.IsCancellationRequested){DiagnosticLog.Record(DiagnosticOperation.ChatCatalogRead,error);usable=false;catalogStatus.Text="Codex chats unavailable. Open or install Codex, check sign-in, then Reload chats.";}}
   finally {
    loading=false;
    if(closed)DisposeLifetime();
    else {reload.IsEnabled=true;more.IsEnabled=usable&&cursor!=null;UpdateStatus();}
   }
  }
  void DisposeLifetime(){if(lifetimeDisposed)return;lifetime.Dispose();lifetimeDisposed=true;}
  void Filter() {
   string query=search.Text.Trim();list.ItemsSource=chats.Where(chat=>(chat.Title+"\n"+chat.Cwd+"\n"+chat.Id).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0).ToList();
   catalogStatus.Text=chats.Count+" chats loaded"+(cursor!=null?" · Load more for older chats":"");
  }
  void DestinationChanged() {
   int destination=dedicatedChat.IsChecked==true?2:newChat.IsChecked==true?1:0;
   if(destination!=displayedDestination) {
    if(displayedDestination==0)workMessage=message.Text;
    else if(displayedDestination==1)newMessage=message.Text;
    message.Text=destination==2?ResetMessages.Acknowledgement:destination==1?newMessage:workMessage;
    displayedDestination=destination;
   }
   UpdateStatus();
  }
  internal void UpdateStatus() {
   if(closed)return;
   var saved=coordinator.State;
   bool dedicated=dedicatedChat.IsChecked==true;
   selectedText.Text=dedicated?(string.IsNullOrEmpty(saved.DedicatedThreadId)?"Dedicated acknowledgement chat will be created when the action runs.":"Dedicated acknowledgement chat:\n"+saved.DedicatedThreadId):newChat.IsChecked==true?"A new chat will be created for each send.":selected==null?"No existing chat selected.":"Selected: "+selected.DisplayTitle+"\n"+selected.Id;
   bool create=newChat.IsChecked==true;project.IsEnabled=create;projectRow.Visibility=create?Visibility.Visible:Visibility.Hidden;project.Visibility=projectLabel.Visibility=create?Visibility.Visible:Visibility.Collapsed;list.IsEnabled=search.IsEnabled=existing.IsChecked==true;message.IsReadOnly=dedicated;presets.IsEnabled=!dedicated;
			presets.Visibility = dedicated ? Visibility.Hidden : Visibility.Visible;
			arm.IsEnabled = usable && !loading && !coordinator.Running && !saved.Armed;
			cancel.IsEnabled = coordinator.CanCancel;
			manualButton.IsEnabled = manualTimePanel.IsEnabled = !coordinator.Running;
			long custom = saved.Armed ? saved.ManualSendUnix : manualSendUnix;
			status.Text = "Armed: " + (saved.Armed ? "Yes" : "No");
			if (custom > 0) {
				status.Text += "\nSend at: " + ResetActionCoordinator.FromUnix(custom).ToLocalTime().ToString("ddd, MMM d - h:mm:ss tt") + " (manual)";
			}
			else if (saved.Armed && saved.DueResetUnix > 0) {
				status.Text += "\nSend at: " + ResetActionCoordinator.SendAt(saved).ToLocalTime().ToString("ddd, MMM d - h:mm:ss tt") + " (reset + 60s)";
			}
			else { status.Text += saved.Armed ? "\nSend at: Waiting for a reset" : "\nSend at: Not scheduled"; }

			status.Text += "\nLast chat: " + (saved.LastThreadId ?? "None");
			status.Text += "\n" + (paused() ? "Pending sends paused. " : "") + saved.Status;
			status.ToolTip = status.Text;
  }
 }
}
