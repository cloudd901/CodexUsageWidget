using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CodexUsageBubble {
 internal sealed class WidgetTheme {
  internal readonly string Name;
  internal readonly ResourceDictionary Resources=new ResourceDictionary();
  static readonly string[] keys={"Surface","Outline","Text","Secondary","Muted","Track","Accent","Weekly","Warning","Critical","MenuBackground","MenuHighlight","MenuSeparator","DisabledText"};
  WidgetTheme(string name,params string[] colors) {
   Name=name;
   for(int i=0;i<keys.Length;i++){var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(colors[i]);brush.Freeze();Resources.Add(keys[i],brush);}
  }
  internal SolidColorBrush Color(string key){return (SolidColorBrush)Resources[key];}
  internal static readonly WidgetTheme[] All={
   new WidgetTheme("Midnight","#181C25","#343C4B","#E7EAF0","#B9C1D0","#8993A5","#303744","#7DDDC3","#A6B5F5","#E4B46F","#ED907F","#202530","#323C4C","#343D4C","#788294"),
   new WidgetTheme("Ocean","#101F2B","#2C4E65","#E4F2FA","#AFCDE0","#83A0B5","#254052","#61D5F5","#B5A3FF","#F0BD69","#FF928B","#142B3B","#25475F","#30516A","#718FA4"),
   new WidgetTheme("Light","#F5F7FA","#CBD5E1","#17212F","#405369","#63758A","#DCE4EC","#087F70","#5B55B2","#956300","#BA3C3C","#FFFFFF","#E4EDF6","#D7E0EA","#8A96A6")
  };
 }
 internal enum MinimalShape { Circle, Square }
 // A single lightweight drawing surface: no animation loop or separate polling.
 internal sealed class MinimalBubble : FrameworkElement {
  internal const double Diameter=62;
  const double Center=Diameter/2, Radius=Center-5;
  static readonly Geometry topTrack=Arc(true,1), bottomTrack=Arc(false,1);
  static readonly Geometry topSquareTrack=SquareTrack(true),bottomSquareTrack=SquareTrack(false);
  static readonly Typeface typeface=new Typeface(new FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal);
  internal string TopText {get;private set;}
  internal string BottomText {get;private set;}
  internal MinimalShape Shape {get;private set;}
  internal Geometry TopTrack {get{return Shape==MinimalShape.Square?topSquareTrack:topTrack;}}
  internal Geometry BottomTrack {get{return Shape==MinimalShape.Square?bottomSquareTrack:bottomTrack;}}
  internal Geometry TopArc {get;private set;}
  internal Geometry BottomArc {get;private set;}
  internal double? TopResetProgress {get;private set;}
  internal double? BottomResetProgress {get;private set;}
  static readonly Geometry resetClip=new EllipseGeometry(new Point(Center,Center),Center-1.5,Center-1.5);
  static readonly Geometry squareResetClip=new RectangleGeometry(new Rect(1.5,1.5,Diameter-3,Diameter-3),4.5,4.5);
  double? topValue,bottomValue;
  bool muted;
  WidgetTheme theme=WidgetTheme.All[0];
  double displayDiameter=Diameter;
  double labelFontSize=9;
  readonly VisualCollection hoverRegions;
  readonly ToolTip topTip=new ToolTip(),bottomTip=new ToolTip();
  readonly Border topHover=new Border {Background=Brushes.Transparent},bottomHover=new Border {Background=Brushes.Transparent};
  internal Rect TopLabelBounds {get{return LabelBounds(TopText,16);}}
  internal Rect BottomLabelBounds {get{return LabelBounds(BottomText,35);}}
  internal void SetDiameter(double value){displayDiameter=value;CancelHover();InvalidateMeasure();InvalidateVisual();}
  internal void SetTheme(WidgetTheme value){theme=value;InvalidateVisual();}
  internal void SetShape(MinimalShape value) {
   if(Shape==value)return;
   Shape=value;CancelHover();
   TopArc=topValue.HasValue?UsageArc(true,topValue.Value/100):null;
   BottomArc=bottomValue.HasValue?UsageArc(false,bottomValue.Value/100):null;
   InvalidateVisual();
  }
  string status;
  internal MinimalBubble() {
   TopText="5h —";BottomText="wk —";ToolTipService.SetIsEnabled(this,false);
   hoverRegions=new VisualCollection(this);hoverRegions.Add(topHover);hoverRegions.Add(bottomHover);AddLogicalChild(topHover);AddLogicalChild(bottomHover);
   topHover.ToolTip=topTip;bottomHover.ToolTip=bottomTip;topTip.PlacementTarget=topHover;bottomTip.PlacementTarget=bottomHover;
   ToolTipService.SetIsEnabled(topHover,true);ToolTipService.SetIsEnabled(bottomHover,true);
   topHover.MouseLeave+=delegate{topTip.IsOpen=false;};bottomHover.MouseLeave+=delegate{bottomTip.IsOpen=false;};
   MouseLeave+=delegate{CancelHover();};MouseDown+=delegate{CancelHover();};
  }
  protected override int VisualChildrenCount {get{return hoverRegions==null?0:hoverRegions.Count;}}
  protected override Visual GetVisualChild(int index){return hoverRegions[index];}
  protected override System.Collections.IEnumerator LogicalChildren {get{return hoverRegions.GetEnumerator();}}
  internal void SetHoverDetails(string fiveHour,string weekly){topTip.Content=fiveHour;bottomTip.Content=weekly;}
  internal void CancelHover(){topTip.IsOpen=false;bottomTip.IsOpen=false;}
  FormattedText FormatLabel(string text,Brush brush){return new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,typeface,labelFontSize,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip);}
  double LabelY(double y){return y-(labelFontSize-9)*0.65;}
  Rect LabelBounds(string text,double y){var formatted=FormatLabel(text,Brushes.Black);double scale=displayDiameter/Diameter;return new Rect((Center-formatted.Width/2)*scale,LabelY(y)*scale,formatted.Width*scale,formatted.Height*scale);}
  internal void SetUsage(UsageSnapshot value,bool stale,bool paused,DateTimeOffset now,string state,bool countdown=false,bool showPrefix=true,bool resetBars=false) {
   var top=Remaining(value==null?null:value.FiveHour,now);var bottom=Remaining(value==null?null:value.Weekly,now);
   var topText=Label("5h",value==null?null:value.FiveHour,top,now,countdown,showPrefix);
   var bottomText=Label("wk",value==null?null:value.Weekly,bottom,now,countdown,showPrefix);
   double? topReset=resetBars?ResetProgress(value==null?null:value.FiveHour,now,300):null;
   double? bottomReset=resetBars?ResetProgress(value==null?null:value.Weekly,now,10080):null;
   double fontSize=showPrefix?9:12;
   bool labelsChanged=topText!=TopText||bottomText!=BottomText||fontSize!=labelFontSize;
   bool dim=stale||paused;
   if(top==topValue&&bottom==bottomValue&&dim==muted&&state==status&&!labelsChanged&&topReset==TopResetProgress&&bottomReset==BottomResetProgress)return;
   if(top!=topValue||bottom!=bottomValue){TopArc=top.HasValue?UsageArc(true,top.Value/100):null;BottomArc=bottom.HasValue?UsageArc(false,bottom.Value/100):null;}
   topValue=top;bottomValue=bottom;muted=dim;status=state;TopText=topText;BottomText=bottomText;labelFontSize=fontSize;TopResetProgress=topReset;BottomResetProgress=bottomReset;
   AutomationProperties.SetName(this,(countdown?"Codex reset countdowns. ":"Codex usage remaining. ")+TopText+"; "+BottomText+". "+(!showPrefix?"Label prefixes hidden. ":""));
   if(labelsChanged)InvalidateMeasure();InvalidateVisual();
  }
		static double? ResetProgress(UsageWindow window, DateTimeOffset now, int minutes) {
			if (!Remaining(window, now).HasValue || !window.HasReset) {
				return null;
			}

			return Math.Max(0, Math.Min(1, 1 - (window.Reset - now).TotalMinutes / minutes));
		}

		static string Label(string prefix, UsageWindow window, double? remaining, DateTimeOffset now, bool countdown, bool show) {
			if (!remaining.HasValue) {
				return (show ? prefix + " " : "") + "\u2014";
			}

			string text = countdown ? (!window.HasReset ? "\u2014" : Countdown(window.Reset - now)) : remaining.Value.ToString("0.#") + "%";
			return (show ? prefix + " " : "") + text;
		}

  static string Countdown(TimeSpan remaining) {
   long seconds=(long)Math.Ceiling(Math.Max(0,remaining.TotalSeconds));
   if(seconds>=86400)return (seconds/86400)+"d"+((seconds%86400)/3600).ToString("00")+"h";
   if(seconds>=3600)return (seconds/3600)+"h"+((seconds%3600)/60).ToString("00")+"m";
   return (seconds/60)+":"+(seconds%60).ToString("00");
  }
  static double? Remaining(UsageWindow window,DateTimeOffset now) {
   if(window==null||window.IsExpired(now)||double.IsNaN(window.Remaining)||double.IsInfinity(window.Remaining)||window.Remaining<0||window.Remaining>100)return null;
   return window.Remaining;
  }
  static Point PointAt(double angle) {double radians=angle*Math.PI/180;return new Point(Center+Radius*Math.Cos(radians),Center-Radius*Math.Sin(radians));}
  static Geometry Arc(bool top,double fraction) {
   if(fraction<=0)return null;
   fraction=Math.Min(1,fraction);
   var g=new StreamGeometry();
   using(var c=g.Open()) {
    c.BeginFigure(PointAt(top?170:190),false,false);
    c.ArcTo(PointAt(top?170-160*fraction:190+160*fraction),new Size(Radius,Radius),0,false,top?SweepDirection.Clockwise:SweepDirection.Counterclockwise,true,false);
   }
   g.Freeze();return g;
  }
  static Geometry SquareTrack(bool top) {
   var g=new StreamGeometry();
   Func<double,double> y=value=>top?value:Diameter-value;
   var sweep=top?SweepDirection.Clockwise:SweepDirection.Counterclockwise;
   using(var c=g.Open()) {
    c.BeginFigure(new Point(5,y(27)),false,false);
    c.LineTo(new Point(5,y(8)),true,false);
    c.ArcTo(new Point(8,y(5)),new Size(3,3),0,false,sweep,true,false);
    c.LineTo(new Point(54,y(5)),true,false);
    c.ArcTo(new Point(57,y(8)),new Size(3,3),0,false,sweep,true,false);
    c.LineTo(new Point(57,y(27)),true,false);
   }
   g.Freeze();return g;
  }
  Geometry UsageArc(bool top,double fraction) {
   if(Shape==MinimalShape.Circle)return Arc(top,fraction);
   if(fraction<=0)return null;
   var track=top?topSquareTrack:bottomSquareTrack;
   if(fraction>=1)return track;
   var measurable=PathGeometry.CreateFromGeometry(track).Clone();foreach(var f in measurable.Figures)f.IsFilled=true;
   var figure=measurable.GetFlattenedPathGeometry(0.02,ToleranceType.Absolute).Figures[0];
   var points=new System.Collections.Generic.List<Point>();points.Add(figure.StartPoint);
   foreach(var segment in figure.Segments) {
    var line=segment as LineSegment;if(line!=null)points.Add(line.Point);
    var poly=segment as PolyLineSegment;if(poly!=null)points.AddRange(poly.Points);
   }
   double length=0;for(int i=1;i<points.Count;i++)length+=(points[i]-points[i-1]).Length;
   double remaining=length*fraction;var g=new StreamGeometry();
   using(var c=g.Open()) {
    c.BeginFigure(points[0],false,false);
    for(int i=1;i<points.Count&&remaining>0;i++) {
     var delta=points[i]-points[i-1];double step=delta.Length;
     if(step<=0)continue;
     c.LineTo(points[i-1]+delta*Math.Min(1,remaining/step),true,false);remaining-=step;
    }
   }
   g.Freeze();return g;
  }
  static Pen Stroke(Brush brush,double thickness) {var p=new Pen(brush,thickness){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};p.Freeze();return p;}
  Brush UsageColor(double? value,bool top) {return theme.Color(muted||!value.HasValue?"Muted":value.Value<=15?"Critical":value.Value<=35?"Warning":top?"Accent":"Weekly");}
  protected override Size MeasureOverride(Size availableSize) {topHover.Measure(TopLabelBounds.Size);bottomHover.Measure(BottomLabelBounds.Size);return new Size(displayDiameter,displayDiameter);}
  protected override Size ArrangeOverride(Size finalSize){topHover.Arrange(TopLabelBounds);bottomHover.Arrange(BottomLabelBounds);return finalSize;}
  protected override void OnRender(DrawingContext dc) {
   base.OnRender(dc);
   double scale=displayDiameter/Diameter;dc.PushTransform(new ScaleTransform(scale,scale));
   if(Shape==MinimalShape.Square)dc.DrawRoundedRectangle(theme.Color("Surface"),Stroke(theme.Color("Outline"),1),new Rect(1,1,Diameter-2,Diameter-2),5,5);
   else dc.DrawEllipse(theme.Color("Surface"),Stroke(theme.Color("Outline"),1),new Point(Center,Center),Center-1,Center-1);
   // Translucent independent time fills, behind all quota arcs and labels.
   dc.PushClip(Shape==MinimalShape.Square?squareResetClip:resetClip);dc.PushOpacity(muted?0.10:0.18);
   if(TopResetProgress.HasValue&&TopResetProgress.Value>0)dc.DrawRectangle(theme.Color("Accent"),null,new Rect(0,0,Diameter*TopResetProgress.Value,Center));
   if(BottomResetProgress.HasValue&&BottomResetProgress.Value>0)dc.DrawRectangle(theme.Color("Weekly"),null,new Rect(0,Center,Diameter*BottomResetProgress.Value,Center));
   dc.Pop();dc.Pop();
   var trackPen=Stroke(theme.Color("Track"),3);
   dc.DrawGeometry(null,trackPen,TopTrack);dc.DrawGeometry(null,trackPen,BottomTrack);
   if(TopArc!=null)dc.DrawGeometry(null,Stroke(UsageColor(topValue,true),3),TopArc);
   if(BottomArc!=null)dc.DrawGeometry(null,Stroke(UsageColor(bottomValue,false),3),BottomArc);
   DrawText(dc,TopText,16,UsageColor(topValue,true));DrawText(dc,BottomText,35,UsageColor(bottomValue,false));
   var dot=theme.Color(status=="PAUSED"||status=="OFFLINE"||status=="STALE"?"Warning":status=="LIVE"?"Accent":"Muted");
   dc.DrawEllipse(dot,null,new Point(Center,Center),1,1);
   dc.Pop();
  }
  void DrawText(DrawingContext dc,string text,double y,Brush brush) {
   var formatted=FormatLabel(text,brush);
   dc.DrawText(formatted,new Point(Center-formatted.Width/2,LabelY(y)));
  }
 }
}
