using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Serialization;

namespace PCBStudio
{
    public partial class MainWindow : Window
    {
        private const double Scale = 10.0, BoardW = 100.0, BoardH = 60.0;
        private readonly PcbProject project = new PcbProject();
        private PcbComponent selected;
        private string mode = "select";
        private Point? trackStart;
        private bool dragging, showGrid = true;
        private Point dragOffset;
        private readonly Stack<string> undo = new Stack<string>();
        private double zoom = 1.0;

        public MainWindow() { InitializeComponent(); Redraw(); UpdateUi(); }

        private Point ToBoard(Point p) => new Point(p.X / Scale, p.Y / Scale);
        private Point ToCanvas(Point p) => new Point(p.X * Scale, p.Y * Scale);
        private Point MouseBoard(MouseEventArgs e) => ToBoard(e.GetPosition(BoardCanvas));
        private Point Snap(Point p) => showGrid ? new Point(Math.Round(p.X * 2) / 2, Math.Round(p.Y * 2) / 2) : p;

        private void SaveUndo() { undo.Push(Serialize()); if (undo.Count > 50) { var a = undo.ToArray(); undo.Clear(); foreach (var s in a.Take(50).Reverse()) undo.Push(s); } }
        private string Serialize() { var xs = new XmlSerializer(typeof(PcbProject)); using (var sw = new StringWriter(CultureInfo.InvariantCulture)) { xs.Serialize(sw, project); return sw.ToString(); } }
        private void Restore(string xml) { var xs = new XmlSerializer(typeof(PcbProject)); using (var sr = new StringReader(xml)) { var p = (PcbProject)xs.Deserialize(sr); project.Name=p.Name ?? "Новая плата"; project.Components=p.Components ?? new List<PcbComponent>(); project.Tracks=p.Tracks ?? new List<PcbTrack>(); project.Holes=p.Holes ?? new List<PcbHole>(); } selected=null; trackStart=null; Redraw(); UpdateUi(); }
        private void Undo_Click(object s, RoutedEventArgs e) { if (undo.Count == 0) { StatusText.Text="История изменений пуста."; return; } Restore(undo.Pop()); StatusText.Text="Последнее действие отменено."; }
        private void SetMode(string m, string message) { mode=m; trackStart=null; dragging=false; BoardCanvas.Cursor=m=="select"?Cursors.Arrow:Cursors.Cross; StatusText.Text=message; BoardCanvas.Focus(); }
        private void SelectMode_Click(object s,RoutedEventArgs e)=>SetMode("select","Режим выбора. Щёлкните компонент, чтобы выделить его.");
        private void TrackMode_Click(object s,RoutedEventArgs e)=>SetMode("track","Режим дорожки: щёлкните начальную, затем конечную точку.");
        private void HoleMode_Click(object s,RoutedEventArgs e)=>SetMode("hole","Режим отверстия: щёлкните по плате для добавления отверстия.");
        private void AddComponent_Click(object s,RoutedEventArgs e)
        {
            string type = TypeBox.SelectedIndex==0?"R":TypeBox.SelectedIndex==1?"C":TypeBox.SelectedIndex==2?"U":TypeBox.SelectedIndex==3?"J":"D";
            string reference=string.IsNullOrWhiteSpace(RefBox.Text)?type+(project.Components.Count+1):RefBox.Text.Trim();
            if(project.Components.Any(c=>c.Reference.Equals(reference,StringComparison.OrdinalIgnoreCase))) { StatusText.Text="Обозначение уже используется."; return; }
            SaveUndo(); var c=new PcbComponent { Reference=reference, Type=type, Value=ValueBox.Text.Trim(), X=50, Y=30 };
            project.Components.Add(c); selected=c; RefBox.Text=type+(project.Components.Count+1); SetMode("place","Щёлкните по плате, чтобы установить "+reference+". Esc отменяет размещение."); Redraw(); UpdateUi();
        }
        private void Board_MouseDown(object sender,MouseButtonEventArgs e)
        {
            BoardCanvas.Focus(); Point p=Snap(MouseBoard(e)); if(p.X<0||p.X>BoardW||p.Y<0||p.Y>BoardH)return;
            if(mode=="place") { if(selected==null)return; selected.X=p.X; selected.Y=p.Y; if(!Inside(selected)){StatusText.Text="Компонент выходит за край платы.";return;} SetMode("select","Компонент установлен."); Redraw(); UpdateUi(); e.Handled=true; return; }
            if(mode=="hole") { SaveUndo(); project.Holes.Add(new PcbHole{X=p.X,Y=p.Y,Diameter=3.2}); Redraw(); UpdateUi(); StatusText.Text="Монтажное отверстие добавлено."; e.Handled=true; return; }
            if(mode=="track") { if(trackStart==null){trackStart=p; StatusText.Text="Начало дорожки: "+Fmt(p.X)+", "+Fmt(p.Y)+" мм. Выберите конец."; } else { SaveUndo(); project.Tracks.Add(new PcbTrack{X1=trackStart.Value.X,Y1=trackStart.Value.Y,X2=p.X,Y2=p.Y,Width=SelectedWidth(),Layer=SelectedLayer()}); trackStart=null; Redraw(); UpdateUi(); StatusText.Text="Дорожка добавлена."; } e.Handled=true;return; }
            var hit=project.Components.AsEnumerable().Reverse().FirstOrDefault(c=>Math.Abs(p.X-c.X)<=HalfW(c)+0.5 && Math.Abs(p.Y-c.Y)<=HalfH(c)+0.5);
            selected=hit;
            if(hit!=null){dragging=true;dragOffset=new Point(hit.X-p.X,hit.Y-p.Y);BoardCanvas.CaptureMouse();StatusText.Text=hit.Reference+" · "+hit.Value+" · "+Fmt(hit.X)+", "+Fmt(hit.Y)+" мм";} else {StatusText.Text="Ничего не выбрано."; }
            Redraw(); e.Handled=true;
        }
        private void Board_MouseMove(object sender,MouseEventArgs e)
        {
            Point p=MouseBoard(e);
            if(dragging&&selected!=null&&e.LeftButton==MouseButtonState.Pressed){Point q=Snap(new Point(p.X+dragOffset.X,p.Y+dragOffset.Y));selected.X=q.X;selected.Y=q.Y;Redraw();}
            else if(p.X>=0&&p.X<=BoardW&&p.Y>=0&&p.Y<=BoardH) { if(mode=="place"&&selected!=null){selected.X=Snap(p).X;selected.Y=Snap(p).Y;Redraw();} StatusText.Text="X: "+Fmt(p.X)+" мм   Y: "+Fmt(p.Y)+" мм"; }
        }
        private void Board_MouseUp(object sender,MouseButtonEventArgs e)
        {
            if(dragging){dragging=false;BoardCanvas.ReleaseMouseCapture();if(selected!=null&&!Inside(selected)){StatusText.Text="Компонент выходит за границы платы."; } SaveUndo();UpdateUi();Redraw();}
        }
        private void Board_MouseWheel(object sender,MouseWheelEventArgs e) { zoom=Math.Max(.5,Math.Min(2.0,zoom*(e.Delta>0?1.1:1/1.1))); BoardCanvas.LayoutTransform=new ScaleTransform(zoom,zoom); e.Handled=true; }
        private static double HalfW(PcbComponent c)=>c.Type=="U"?3.0:c.Type=="J"?2.5:1.7;
        private static double HalfH(PcbComponent c)=>c.Type=="U"?2.2:c.Type=="J"?1.6:1.2;
        private bool Inside(PcbComponent c)=>c.X-HalfW(c)>=0&&c.X+HalfW(c)<=BoardW&&c.Y-HalfH(c)>=0&&c.Y+HalfH(c)<=BoardH;
        private double SelectedWidth(){var i=WidthBox.SelectedIndex;return i==0?.25:i==2?.5:i==3?.8:.35;}
        private string SelectedLayer()=>LayerBox.SelectedIndex==1?"Bottom":"Top";
        private static string Fmt(double n)=>n.ToString("0.##",CultureInfo.InvariantCulture);

        private void Redraw()
        {
            BoardCanvas.Children.Clear();
            BoardCanvas.Background=new SolidColorBrush(Color.FromRgb(23,61,50));
            BoardCanvas.Children.Add(new Rectangle{Width=BoardW*Scale,Height=BoardH*Scale,Fill=new SolidColorBrush(Color.FromRgb(27,77,62)),Stroke=new SolidColorBrush(Color.FromRgb(57,208,176)),StrokeThickness=2});
            if(showGrid) for(int x=0;x<=BoardW;x+=5) BoardCanvas.Children.Add(new Line{X1=x*Scale,Y1=0,X2=x*Scale,Y2=BoardH*Scale,Stroke=new SolidColorBrush(Color.FromArgb(45,180,220,205)),StrokeThickness=.5});
            if(showGrid) for(int y=0;y<=BoardH;y+=5) BoardCanvas.Children.Add(new Line{X1=0,Y1=y*Scale,X2=BoardW*Scale,Y2=y*Scale,Stroke=new SolidColorBrush(Color.FromArgb(45,180,220,205)),StrokeThickness=.5});
            foreach(var t in project.Tracks){var line=new Line{X1=t.X1*Scale,Y1=t.Y1*Scale,X2=t.X2*Scale,Y2=t.Y2*Scale,Stroke=t.Layer=="Bottom"?Brushes.MediumPurple:Brushes.Orange,StrokeThickness=Math.Max(1,t.Width*Scale),StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};BoardCanvas.Children.Add(line);}
            foreach(var h in project.Holes){double d=Math.Max(1,h.Diameter*Scale);var outer=new Ellipse{Width=d,Height=d,Fill=Brushes.DarkSlateGray,Stroke=Brushes.Gold,StrokeThickness=2};Canvas.SetLeft(outer,h.X*Scale-d/2);Canvas.SetTop(outer,h.Y*Scale-d/2);BoardCanvas.Children.Add(outer);var inner=new Ellipse{Width=d*.35,Height=d*.35,Fill=Brushes.Black};Canvas.SetLeft(inner,h.X*Scale-d*.175);Canvas.SetTop(inner,h.Y*Scale-d*.175);BoardCanvas.Children.Add(inner);}
            foreach(var c in project.Components){double w=HalfW(c)*2*Scale,h=HalfH(c)*2*Scale;var shape=new Rectangle{Width=w,Height=h,RadiusX=3,RadiusY=3,Fill=c==selected?new SolidColorBrush(Color.FromRgb(61,119,129)):new SolidColorBrush(Color.FromRgb(44,55,67)),Stroke=c==selected?Brushes.White:Brushes.LightGray,StrokeThickness=c==selected?2:1,RenderTransform=new RotateTransform(c.Rotation,w/2,h/2)};Canvas.SetLeft(shape,c.X*Scale-w/2);Canvas.SetTop(shape,c.Y*Scale-h/2);BoardCanvas.Children.Add(shape);var label=new TextBlock{Text=c.Reference,Foreground=Brushes.White,FontSize=12,FontWeight=FontWeights.SemiBold};label.Measure(new Size(100,30));Canvas.SetLeft(label,c.X*Scale-label.DesiredSize.Width/2);Canvas.SetTop(label,c.Y*Scale-h/2-17);BoardCanvas.Children.Add(label);var value=new TextBlock{Text=c.Value,Foreground=new SolidColorBrush(Color.FromRgb(177,202,195)),FontSize=10};value.Measure(new Size(100,25));Canvas.SetLeft(value,c.X*Scale-value.DesiredSize.Width/2);Canvas.SetTop(value,c.Y*Scale+h/2+1);BoardCanvas.Children.Add(value);}
        }
        private void UpdateUi(){ProjectLabel.Text=project.Name;StatsText.Text=project.Components.Count+" компонентов · "+project.Tracks.Count+" дорожек · "+project.Holes.Count+" отверстий";NameBox.Text=project.Name;}
        private void Rotate_Click(object s,RoutedEventArgs e){if(selected==null){StatusText.Text="Сначала выберите компонент.";return;}SaveUndo();selected.Rotation=(selected.Rotation+90)%360;Redraw();StatusText.Text=selected.Reference+" повёрнут на "+selected.Rotation+"°.";}
        private void Delete_Click(object s,RoutedEventArgs e){if(selected==null){StatusText.Text="Сначала выберите компонент.";return;}SaveUndo();project.Components.Remove(selected);selected=null;Redraw();UpdateUi();StatusText.Text="Компонент удалён.";}
        private void Grid_Click(object s,RoutedEventArgs e){showGrid=!showGrid;Redraw();StatusText.Text=showGrid?"Сетка включена.":"Сетка выключена.";}
        private void Fit_Click(object s,RoutedEventArgs e){zoom=1;BoardCanvas.LayoutTransform=null;BoardScroll.ScrollToHome();StatusText.Text="Плата отображается в масштабе 100%.";}
        private void New_Click(object s,RoutedEventArgs e){if(MessageBox.Show("Создать новый проект? Несохранённые изменения будут потеряны.","Новый проект",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;project.Name="Новая плата";project.Components.Clear();project.Tracks.Clear();project.Holes.Clear();selected=null;undo.Clear();Redraw();UpdateUi();StatusText.Text="Создан новый проект.";}
        private void Save_Click(object s,RoutedEventArgs e){project.Name=string.IsNullOrWhiteSpace(NameBox.Text)?"Новая плата":NameBox.Text.Trim();var d=new SaveFileDialog{Filter="PCB Studio (*.pcb)|*.pcb|XML (*.xml)|*.xml",DefaultExt=".pcb",FileName=project.Name};if(d.ShowDialog()!=true)return;File.WriteAllText(d.FileName,Serialize(),Encoding.UTF8);UpdateUi();StatusText.Text="Проект сохранён: "+d.FileName;}
        private void Open_Click(object s,RoutedEventArgs e){var d=new OpenFileDialog{Filter="PCB Studio (*.pcb;*.xml)|*.pcb;*.xml|Все файлы (*.*)|*.*"};if(d.ShowDialog()!=true)return;try{Restore(File.ReadAllText(d.FileName));StatusText.Text="Проект открыт: "+d.FileName;}catch(Exception ex){MessageBox.Show("Не удалось открыть проект:\n"+ex.Message,"Ошибка",MessageBoxButton.OK,MessageBoxImage.Error);}}
        private void Export_Click(object s,RoutedEventArgs e){var d=new SaveFileDialog{Filter="SVG (*.svg)|*.svg",DefaultExt=".svg",FileName=project.Name+".svg"};if(d.ShowDialog()!=true)return;var b=new StringBuilder();b.AppendLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"600\" viewBox=\"0 0 1000 600\">");b.AppendLine("<rect width=\"1000\" height=\"600\" fill=\"#1b4d3e\" stroke=\"#39d0b0\" stroke-width=\"3\"/>");foreach(var t in project.Tracks)b.AppendLine("<line x1=\""+Fmt(t.X1*10)+"\" y1=\""+Fmt(t.Y1*10)+"\" x2=\""+Fmt(t.X2*10)+"\" y2=\""+Fmt(t.Y2*10)+"\" stroke=\""+(t.Layer=="Bottom"?"#9370db":"#ffa500")+"\" stroke-width=\""+Fmt(t.Width*10)+"\" stroke-linecap=\"round\"/>");foreach(var c in project.Components)b.AppendLine("<g transform=\"translate("+Fmt(c.X*10)+" "+Fmt(c.Y*10)+") rotate("+c.Rotation+")\"><rect x=\""+Fmt(-HalfW(c)*10)+"\" y=\""+Fmt(-HalfH(c)*10)+"\" width=\""+Fmt(HalfW(c)*20)+"\" height=\""+Fmt(HalfH(c)*20)+"\" fill=\"#2c3743\" stroke=\"#ffffff\"/><text text-anchor=\"middle\" y=\"0\" fill=\"white\" font-size=\"12\">"+Esc(c.Reference)+"</text></g>");foreach(var h in project.Holes)b.AppendLine("<circle cx=\""+Fmt(h.X*10)+"\" cy=\""+Fmt(h.Y*10)+"\" r=\""+Fmt(h.Diameter*5)+"\" fill=\"#222\" stroke=\"#ffd700\" stroke-width=\"2\"/>");b.AppendLine("</svg>");File.WriteAllText(d.FileName,b.ToString(),Encoding.UTF8);StatusText.Text="SVG экспортирован: "+d.FileName;}
        private static string Esc(string s)=>(s??"").Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;").Replace("\"","&quot;");
        private void Drc_Click(object s,RoutedEventArgs e){var issues=new List<string>();foreach(var c in project.Components){if(!Inside(c))issues.Add(c.Reference+": корпус за границей платы.");if(project.Components.Any(o=>o!=c&&Math.Abs(o.X-c.X)<HalfW(c)+HalfW(o)&&Math.Abs(o.Y-c.Y)<HalfH(c)+HalfH(o)))issues.Add(c.Reference+": возможное пересечение корпусов.");foreach(var h in project.Holes)if(Math.Abs(c.X-h.X)<HalfW(c)+h.Diameter/2&&Math.Abs(c.Y-h.Y)<HalfH(c)+h.Diameter/2)issues.Add(c.Reference+": слишком близко к монтажному отверстию.");}for(int i=0;i<project.Holes.Count;i++){var h=project.Holes[i];if(h.Diameter<=0||h.X-h.Diameter/2<0||h.X+h.Diameter/2>BoardW||h.Y-h.Diameter/2<0||h.Y+h.Diameter/2>BoardH)issues.Add("Отверстие "+(i+1)+": неверный диаметр или положение.");for(int j=i+1;j<project.Holes.Count;j++){var o=project.Holes[j];if(Math.Sqrt(Math.Pow(h.X-o.X,2)+Math.Pow(h.Y-o.Y,2))<(h.Diameter+o.Diameter)/2+.5)issues.Add("Отверстия "+(i+1)+" и "+(j+1)+": малый зазор.");}}string report=issues.Count==0?"Ошибок размещения не обнаружено.":string.Join("\n",issues.Distinct().Take(30));MessageBox.Show(report,"Проверка DRC",MessageBoxButton.OK,issues.Count==0?MessageBoxImage.Information:MessageBoxImage.Warning);StatusText.Text=issues.Count==0?"DRC: ошибок не найдено.":"DRC: найдено замечаний — "+issues.Count+".";}
        private void Window_PreviewKeyDown(object sender,KeyEventArgs e){bool ctrl=(Keyboard.Modifiers&ModifierKeys.Control)!=0;if(ctrl&&e.Key==Key.S)Save_Click(this,new RoutedEventArgs());else if(ctrl&&e.Key==Key.O)Open_Click(this,new RoutedEventArgs());else if(ctrl&&e.Key==Key.N)New_Click(this,new RoutedEventArgs());else if(ctrl&&e.Key==Key.Z)Undo_Click(this,new RoutedEventArgs());else if(!ctrl&&e.Key==Key.V)SelectMode_Click(this,new RoutedEventArgs());else if(!ctrl&&e.Key==Key.T)TrackMode_Click(this,new RoutedEventArgs());else if(!ctrl&&e.Key==Key.H)HoleMode_Click(this,new RoutedEventArgs());else if(!ctrl&&e.Key==Key.R)Rotate_Click(this,new RoutedEventArgs());else if(!ctrl&&e.Key==Key.Delete)Delete_Click(this,new RoutedEventArgs());else if(!ctrl&&e.Key==Key.Escape)SelectMode_Click(this,new RoutedEventArgs());else if(!ctrl&&e.Key==Key.F)Fit_Click(this,new RoutedEventArgs());else return;e.Handled=true;}
    }
    [XmlRoot("PcbProject")] public class PcbProject { public string Name{get;set;} public List<PcbComponent> Components{get;set;} public List<PcbTrack> Tracks{get;set;} public List<PcbHole> Holes{get;set;} public PcbProject(){Name="Новая плата";Components=new List<PcbComponent>();Tracks=new List<PcbTrack>();Holes=new List<PcbHole>();} }
    public class PcbComponent { public string Reference{get;set;} public string Type{get;set;} public string Value{get;set;} public double X{get;set;} public double Y{get;set;} public int Rotation{get;set;} }
    public class PcbTrack { public double X1{get;set;} public double Y1{get;set;} public double X2{get;set;} public double Y2{get;set;} public double Width{get;set;} public string Layer{get;set;} }
    public class PcbHole { public double X{get;set;} public double Y{get;set;} public double Diameter{get;set;} }
}