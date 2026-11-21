using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace CodexUsageBubble {
 public sealed partial class BubbleWindow : Window {
		readonly DispatcherTimer trayClickTimer = new DispatcherTimer();
		AboutWindow aboutWindow;

		void CreateTrayIcon() {
			try {
				using (Stream stream = typeof(BubbleWindow).Assembly.GetManifestResourceStream("CodexUsageBubble.ring.ico")) {
					if (stream == null) {
						trayImage = (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
					}
					else {
						using (System.Drawing.Icon image = new System.Drawing.Icon(stream)) { trayImage = (System.Drawing.Icon)image.Clone(); }
					}
				}

				trayMenu = new System.Windows.Forms.ContextMenuStrip();
				trayVisibility = new System.Windows.Forms.ToolStripMenuItem("Hide widget", null, delegate { ToggleTrayVisibility(); });
				trayMenu.Items.Add(trayVisibility);
				trayAutoMessageOnImage = CreateTrayStatusDot(System.Drawing.Color.FromArgb(45, 174, 103));
				trayAutoMessageOffImage = CreateTrayStatusDot(System.Drawing.Color.FromArgb(128, 128, 128));
				trayAutoMessage = new System.Windows.Forms.ToolStripMenuItem("Auto-msg", null, delegate { ShowResetActionWindow(); }) {
					ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None
				};
				trayMenu.Items.Add(trayAutoMessage);
				trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
				trayMenu.Items.Add("About", null, delegate { ShowAboutWindow(); });
				trayMenu.Items.Add("Exit", null, delegate { ExitWidget(); });
				trayMenu.Opening += delegate { UpdateTrayMenu(); };
				trayIcon = new System.Windows.Forms.NotifyIcon {
					Icon = trayImage,
					Text = "CodexUsageWidget",
					ContextMenuStrip = trayMenu,
					Visible = true
				};
				trayClickTimer.Interval = TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime);
				trayClickTimer.Tick += delegate { RestoreFromTray(); };
				trayIcon.MouseClick += delegate(object sender, System.Windows.Forms.MouseEventArgs e) {
					// The first click also arrives during a double-click. Wait before restoring.
					if (e.Button == System.Windows.Forms.MouseButtons.Left && e.Clicks == 1) {
						trayClickTimer.Stop();
						trayClickTimer.Start();
					}
				};
				trayIcon.MouseDoubleClick += delegate(object sender, System.Windows.Forms.MouseEventArgs e) {
					if (e.Button == System.Windows.Forms.MouseButtons.Left) { ToggleTrayVisibility(); }
				};
				UpdateTrayMenu();
			}
			catch (Exception error) {
				DiagnosticLog.Record(DiagnosticOperation.TrayCreate,error);
				// Keep ordinary Close available if Windows cannot create the tray icon.
				DisposeTrayIcon();
			}
		}

		static System.Drawing.Bitmap CreateTrayStatusDot(System.Drawing.Color color) {
			System.Drawing.Bitmap image = new System.Drawing.Bitmap(16, 16);
			using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(image)) {
				graphics.Clear(System.Drawing.Color.Transparent);
				graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				using (System.Drawing.SolidBrush brush = new System.Drawing.SolidBrush(color)) {
					graphics.FillEllipse(brush, 4, 4, 8, 8);
				}
			}

			return image;
		}

		void UpdateTrayMenu() {
			if (trayVisibility == null) { return; }

			trayVisibility.Text = IsVisible ? "Hide widget" : "Show widget";
			UpdateResetActionIndicator();
		}

		void ShowAboutWindow() {
			if (aboutWindow == null) {
				aboutWindow = new AboutWindow(Theme) { Owner = this, Icon = Icon };
				aboutWindow.Closed += delegate { aboutWindow = null; };
				aboutWindow.Show();
			}

			if (aboutWindow.WindowState == WindowState.Minimized) { aboutWindow.WindowState = WindowState.Normal; }
			aboutWindow.Activate();
		}

		void ToggleTrayVisibility() {
			if (IsVisible) { HideToTray(); }
			else { RestoreFromTray(); }
		}

		void HideToTray() {
			trayClickTimer.Stop();
			if (closed || trayIcon == null || !trayIcon.Visible || !IsVisible) { return; }

			SavePosition();
			ContextMenu.IsOpen = false;
			minimalBubble.CancelHover();
			dragStart = null;
			nativeSnapping.Detach();
			restoreActionWindow = actionWindow != null && actionWindow.IsVisible;
			if (restoreActionWindow) { actionWindow.Hide(); }

			Hide();
		}

		void RestoreFromTray() {
			trayClickTimer.Stop();
			if (closed) { return; }

			Show();
			WindowState = WindowState.Normal;
			Activate();
			if (restoreActionWindow && actionWindow != null) { actionWindow.Show(); }

			restoreActionWindow = false;
		}

		internal void ExitWidget() {
			allowExit = true;
			Close();
		}

		void DisposeTrayIcon() {
			trayClickTimer.Stop();
			if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); trayIcon = null; }

			if (trayMenu != null) { trayMenu.Dispose(); trayMenu = null; }

			trayVisibility = null;
			trayAutoMessage = null;
			if (trayAutoMessageOnImage != null) { trayAutoMessageOnImage.Dispose(); trayAutoMessageOnImage = null; }

			if (trayAutoMessageOffImage != null) { trayAutoMessageOffImage.Dispose(); trayAutoMessageOffImage = null; }

			if (trayImage != null) { trayImage.Dispose(); trayImage = null; }
		}

 }
}
