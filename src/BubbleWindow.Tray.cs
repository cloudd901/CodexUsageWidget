using System;
using System.IO;
using System.Windows;

namespace CodexUsageBubble {
 public sealed partial class BubbleWindow : Window {
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
				trayVisibility = new System.Windows.Forms.ToolStripMenuItem("Hide widget", null, delegate {
					if (IsVisible) { HideToTray(); }
					else { RestoreFromTray(); }
				});
				trayMenu.Items.Add(trayVisibility);
				trayAutoMessageOnImage = CreateTrayStatusDot(System.Drawing.Color.FromArgb(45, 174, 103));
				trayAutoMessageOffImage = CreateTrayStatusDot(System.Drawing.Color.FromArgb(128, 128, 128));
				// A label cannot be selected and keeps its dot colored instead of disabled-gray rendering.
				trayAutoMessage = new System.Windows.Forms.ToolStripLabel("Auto-msg") {
					IsLink = false,
					ImageScaling = System.Windows.Forms.ToolStripItemImageScaling.None,
					AccessibleRole = System.Windows.Forms.AccessibleRole.StaticText
				};
				trayMenu.Items.Add(trayAutoMessage);
				trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
				trayMenu.Items.Add("Exit", null, delegate { ExitWidget(); });
				trayMenu.Opening += delegate { UpdateTrayMenu(); };
				trayIcon = new System.Windows.Forms.NotifyIcon {
					Icon = trayImage,
					Text = "CodexUsageWidget",
					ContextMenuStrip = trayMenu,
					Visible = true
				};
				trayIcon.MouseClick += delegate(object sender, System.Windows.Forms.MouseEventArgs e) {
					if (e.Button == System.Windows.Forms.MouseButtons.Left) { RestoreFromTray(); }
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

		void HideToTray() {
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
