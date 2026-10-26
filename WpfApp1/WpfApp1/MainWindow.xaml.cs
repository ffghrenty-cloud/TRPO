using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace WpfApp1
{
    public partial class MainWindow : Window
    {
        private const double BoardLeft = 180;
        private const double BoardTop = 120;
        private const double BoardWidth = 1000;
        private const double BoardHeight = 600;
        private const double PixelsPerMm = 10;
        private readonly PcbProject _project = new PcbProject();
        private PcbComponent _selected;
        private bool _dragging;\n        private bool _panning;\n        private Point _panStart;\n        private double _panHorizontalStart;\n        private double _panVerticalStart;
        private bool _trackMode;
        private bool _showGrid = true;
        private Point? _trackStart;
        private double _zoom = 1.0;
        private string _currentFile;

        public MainWindow()
        {
            InitializeComponent();
            _project.Name = "Новый проект";
            _project.Components = new List<PcbComponent>();
            _project.Tracks = new List<PcbTrack>();
            RedrawBoard();
            UpdateStatus();
        }

        private void RedrawBoard()
        {
            if (BoardCanvas == null) return;
            BoardCanvas.Children.Clear();
            BoardCanvas.Background = new SolidColorBrush(Color.FromRgb(17, 26, 40));

            var board = new Rectangle
            {
                Width = BoardWidth,
                Height = BoardHeight,
                RadiusX = 5,
                RadiusY = 5,
                Fill = new SolidColorBrush(Color.FromRgb(31, 91, 70)),
                Stroke = new SolidColorBrush(Color.FromRgb(97, 178, 139)),
                StrokeThickness = 2
            };
            Canvas.SetLeft(board, BoardLeft);
            Canvas.SetTop(board, BoardTop);
            BoardCanvas.Children.Add(board);

            if (_showGrid)
            {
                for (double x = BoardLeft + 10; x < BoardLeft + BoardWidth; x += 10)
                {
                    var line = new Line { X1 = x, X2 = x, Y1 = BoardTop, Y2 = BoardTop + BoardHeight,
                        Stroke = new SolidColorBrush(Color.FromArgb(45, 215, 238, 225)), StrokeThickness = 0.5 };
                    BoardCanvas.Children.Add(line);
                }
                for (double y = BoardTop + 10; y < BoardTop + BoardHeight; y += 10)
                {
                    var line = new Line { X1 = BoardLeft, X2 = BoardLeft + BoardWidth, Y1 = y, Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromArgb(45, 215, 238, 225)), StrokeThickness = 0.5 };
                    BoardCanvas.Children.Add(line);
                }
                for (double x = BoardLeft + 50; x < BoardLeft + BoardWidth; x += 50)
                    for (double y = BoardTop + 50; y < BoardTop + BoardHeight; y += 50)
                    {
                        var dot = new Ellipse { Width = 2, Height = 2, Fill = new SolidColorBrush(Color.FromArgb(120, 218, 242, 226)) };
                        Canvas.SetLeft(dot, x - 1); Canvas.SetTop(dot, y - 1); BoardCanvas.Children.Add(dot);
                    }
            }

            AddBoardLabel("PCB • 100 × 60 mm", BoardLeft + 12, BoardTop + 8, 11, Color.FromRgb(192, 229, 210));
            AddBoardLabel("TOP COPPER", BoardLeft + 12, BoardTop + BoardHeight - 24, 10, Color.FromRgb(160, 211, 188));

            foreach (var track in _project.Tracks)
            {
                bool visible = track.Layer == "Bottom" ? IsChecked(BottomLayerCheck) : IsChecked(TopLayerCheck);
                if (!visible) continue;
                var line = new Line
                {
                    X1 = BoardLeft + track.X1 * PixelsPerMm, Y1 = BoardTop + track.Y1 * PixelsPerMm,
                    X2 = BoardLeft + track.X2 * PixelsPerMm, Y2 = BoardTop + track.Y2 * PixelsPerMm,
                    Stroke = track.Layer == "Bottom" ? new SolidColorBrush(Color.FromRgb(70, 154, 255)) : new SolidColorBrush(Color.FromRgb(244, 112, 79)),
                    StrokeThickness = Math.Max(2, track.Width * PixelsPerMm),
                    StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
                };
                BoardCanvas.Children.Add(line);
            }

            foreach (var component in _project.Components)
                DrawComponent(component);

            UpdateStatus();
        }

        private static bool IsChecked(CheckBox box)
        {
            return box != null && box.IsChecked == true;
        }

        private void AddBoardLabel(string text, double x, double y, double size, Color color)
        {
            if (!IsChecked(SilkLayerCheck)) return;
            var label = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(color), IsHitTestVisible = false };
            Canvas.SetLeft(label, x); Canvas.SetTop(label, y); BoardCanvas.Children.Add(label);
        }

        private void DrawComponent(PcbComponent component)
        {
            double x = BoardLeft + component.X * PixelsPerMm;
            double y = BoardTop + component.Y * PixelsPerMm;
            double w = component.Type == "U" ? 58 : component.Type == "J" ? 48 : component.Type == "TP" ? 24 : 34;
            double h = component.Type == "U" ? 42 : component.Type == "J" ? 32 : component.Type == "TP" ? 24 : 22;
            Brush body = component.Type == "R" ? new SolidColorBrush(Color.FromRgb(220, 167, 74)) :
                         component.Type == "C" ? new SolidColorBrush(Color.FromRgb(74, 153, 226)) :
                         component.Type == "D" ? new SolidColorBrush(Color.FromRgb(221, 91, 91)) :
                         component.Type == "U" ? new SolidColorBrush(Color.FromRgb(39, 48, 61)) :
                         component.Type == "J" ? new SolidColorBrush(Color.FromRgb(190, 198, 207)) :
                         new SolidColorBrush(Color.FromRgb(229, 192, 72));
            var shape = new Rectangle
            {
                Width = w, Height = h, RadiusX = component.Type == "TP" ? 12 : 3, RadiusY = component.Type == "TP" ? 12 : 3,
                Fill = body,
                Stroke = _selected == component ? Brushes.White : new SolidColorBrush(Color.FromRgb(238, 244, 248)),
                StrokeThickness = _selected == component ? 3 : 1.5,
                IsHitTestVisible = false
            };
            if (component.Type == "TP") shape.RadiusX = shape.RadiusY = 12;
            Canvas.SetLeft(shape, x - w / 2); Canvas.SetTop(shape, y - h / 2); BoardCanvas.Children.Add(shape);

            if (component.Type == "U")
            {
                for (int i = 0; i < 4; i++)
                {
                    AddPad(x - w / 2 - 3, y - 13 + i * 8);
                    AddPad(x + w / 2 - 3, y - 13 + i * 8);
                }
            }
            else if (component.Type != "TP")
            {
                AddPad(x - w / 2 - 3, y - 3);
                AddPad(x + w / 2 - 3, y - 3);
            }
            if (IsChecked(SilkLayerCheck))
            {
                var label = new TextBlock { Text = component.Reference, FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White, IsHitTestVisible = false };
                Canvas.SetLeft(label, x - w / 2); Canvas.SetTop(label, y + h / 2 + 3); BoardCanvas.Children.Add(label);
                var value = new TextBlock { Text = component.Value, FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(192, 211, 202)), IsHitTestVisible = false };
                Canvas.SetLeft(value, x - w / 2); Canvas.SetTop(value, y + h / 2 + 16); BoardCanvas.Children.Add(value);
            }
        }

        private void AddPad(double x, double y)
        {
            var pad = new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromRgb(235, 197, 100)),
                Stroke = new SolidColorBrush(Color.FromRgb(255, 226, 151)), StrokeThickness = 0.8, IsHitTestVisible = false };
            Canvas.SetLeft(pad, x); Canvas.SetTop(pad, y); BoardCanvas.Children.Add(pad);
        }

        private void AddComponent_Click(object sender, RoutedEventArgs e)
        {
            var item = LibraryList.SelectedItem as ListBoxItem;
            string type = item == null ? "R" : Convert.ToString(item.Tag);
            string prefix = type == "TP" ? "TP" : type;
            int index = 1;
            while (_project.Components.Any(c => c.Reference == prefix + index)) index++;
            int n = _project.Components.Count;
            var component = new PcbComponent
            {
                Reference = prefix + index,
                Type = type,
                Value = type == "R" ? "10k" : type == "C" ? "100nF" : type == "D" ? "LED" : type == "U" ? "IC" : type == "J" ? "CONN" : "Test",
                X = Math.Round(15.0 + (n % 5) * 15.0, 1),
                Y = Math.Round(15.0 + (n / 5) * 12.0, 1)
            };
            if (component.Y > 54) component.Y = 54;
            _project.Components.Add(component);
            _selected = component;
            _trackMode = false;
            _trackStart = null;
            RefreshSelection();
            RedrawBoard();
            StatusText.Text = "Добавлен компонент " + component.Reference + ". Перетащите его в нужное место.";
        }

        private void BoardCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(BoardCanvas);
            if (_trackMode)
            {
                if (!IsInsideBoard(p))
                {
                    StatusText.Text = "Начало и конец дорожки должны находиться внутри платы.";
                    return;
                }
                Point mm = ToMm(p);
                if (_trackStart == null)
                {
                    _trackStart = mm;
                    StatusText.Text = "Начальная точка задана. Щёлкните конечную точку дорожки.";
                }
                else
                {
                    if (Distance(_trackStart.Value, mm) > 0.1)
                    {
                        _project.Tracks.Add(new PcbTrack { X1 = _trackStart.Value.X, Y1 = _trackStart.Value.Y,
                            X2 = mm.X, Y2 = mm.Y, Width = 0.35, Layer = IsChecked(BottomLayerCheck) && !IsChecked(TopLayerCheck) ? "Bottom" : "Top" });
                        StatusText.Text = "Дорожка добавлена.";
                    }
                    _trackStart = null;
                    RedrawBoard();
                }
                e.Handled = true;
                return;
            }

            var hit = _project.Components.OrderBy(c => Distance(p, ToCanvas(c))).FirstOrDefault(c => Distance(p, ToCanvas(c)) < HitRadius(c));
            _selected = hit;
            if (_selected != null)
            {
                _dragging = true;
                BoardCanvas.CaptureMouse();
                RefreshSelection();
                RedrawBoard();
            }
            else
            {
                RefreshSelection();
            }
        }

        private void BoardCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            Point p = e.GetPosition(BoardCanvas);
            if (_dragging && _selected != null && e.LeftButton == MouseButtonState.Pressed && IsInsideBoard(p))
            {
                Point mm = ToMm(p);
                _selected.X = Math.Round(mm.X, 1);
                _selected.Y = Math.Round(mm.Y, 1);
                RefreshSelection();
                RedrawBoard();
            }
            if (IsInsideBoard(p))
            {
                Point mm = ToMm(p);
                if (!_dragging && !_trackMode)
                    StatusText.Text = string.Format("Координаты курсора: X {0:0.0} мм, Y {1:0.0} мм", mm.X, mm.Y);
            }
        }

        private void BoardCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            BoardCanvas.ReleaseMouseCapture();
        }

        // Обычное колесо прокручивает рабочую область; Ctrl+колесо меняет масштаб.
        private void BoardCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;

            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newZoom = Math.Max(0.4, Math.Min(3.0, _zoom * factor));
            if (Math.Abs(newZoom - _zoom) < 0.001) return;

            _zoom = newZoom;
            BoardCanvas.RenderTransform = new ScaleTransform(_zoom, _zoom);
            BoardCanvas.RenderTransformOrigin = new Point(0, 0);
            ZoomText.Text = "Масштаб: " + Math.Round(_zoom * 100) + "%";
            e.Handled = true;
        }

        // Средняя кнопка мыши позволяет перетаскивать рабочую область.
        private void BoardCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle) return;

            _panning = true;
            _panStart = e.GetPosition(BoardScrollViewer);
            _panHorizontalStart = BoardScrollViewer.HorizontalOffset;
            _panVerticalStart = BoardScrollViewer.VerticalOffset;
            BoardCanvas.CaptureMouse();
            BoardCanvas.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }

        private void BoardCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || !_panning) return;

            _panning = false;
            BoardCanvas.ReleaseMouseCapture();
            BoardCanvas.Cursor = Cursors.Arrow;
            e.Handled = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool textInputFocused = Keyboard.FocusedElement is TextBoxBase;

            if (control && e.Key == Key.S) SaveProject_Click(this, new RoutedEventArgs());
            else if (control && e.Key == Key.O) OpenProject_Click(this, new RoutedEventArgs());
            else if (control && e.Key == Key.N) NewProject_Click(this, new RoutedEventArgs());
            else if (control && e.Key == Key.E) ExportSvg_Click(this, new RoutedEventArgs());
            else if (control && e.Key == Key.D0) FitBoard_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && e.Key == Key.Delete) DeleteSelected_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && e.Key == Key.G)
            {
                _showGrid = !_showGrid;
                RedrawBoard();
                StatusText.Text = _showGrid ? "Сетка включена." : "Сетка выключена.";
            }
            else if (e.Key == Key.Escape)
            {
                _trackMode = false;
                _trackStart = null;
                _dragging = false;
                StatusText.Text = "Текущая операция отменена. Режим выбора.";
            }
            else if (!control && !textInputFocused && e.Key == Key.T)
                TrackMode_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && e.Key == Key.V)
                SelectMode_Click(this, new RoutedEventArgs());
            else return;

            e.Handled = true;
        }

        private static Point ToCanvas(PcbComponent component)
        {
            return new Point(BoardLeft + component.X * PixelsPerMm, BoardTop + component.Y * PixelsPerMm);
        }

        private static Point ToMm(Point point)
        {
            return new Point(Math.Round((point.X - BoardLeft) / PixelsPerMm, 2),
                             Math.Round((point.Y - BoardTop) / PixelsPerMm, 2));
        }

        private static double Distance(Point a, Point b)
        {
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double HitRadius(PcbComponent c)
        {
            return c.Type == "U" ? 34 : c.Type == "J" ? 30 : 24;
        }

        private static bool IsInsideBoard(Point p)
        {
            return p.X >= BoardLeft && p.X <= BoardLeft + BoardWidth &&
                   p.Y >= BoardTop && p.Y <= BoardTop + BoardHeight;
        }

        private void RefreshSelection()
        {
            if (_selected == null)
            {
                SelectedName.Text = "Ничего не выбрано";
                XBox.Text = "0"; YBox.Text = "0";
            }
            else
            {
                SelectedName.Text = _selected.Reference + "  ·  " + _selected.Value;
                XBox.Text = _selected.X.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                YBox.Text = _selected.Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private void ApplyProperties_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) return;
            double x, y;
            if (!double.TryParse(XBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out x) ||
                !double.TryParse(YBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out y) ||
                x < 0 || x > 100 || y < 0 || y > 60)
            {
                MessageBox.Show("Введите координаты в пределах платы: X от 0 до 100 мм, Y от 0 до 60 мм.",
                    "Некорректные координаты", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _selected.X = x; _selected.Y = y;
            RedrawBoard();
            StatusText.Text = "Координаты " + _selected.Reference + " обновлены.";
        }

        private void TrackMode_Click(object sender, RoutedEventArgs e)
        {
            _trackMode = true; _dragging = false; _trackStart = null;
            StatusText.Text = "Режим дорожки: щёлкните начальную, затем конечную точку внутри платы.";
        }

        private void SelectMode_Click(object sender, RoutedEventArgs e)
        {
            _trackMode = false; _trackStart = null;
            StatusText.Text = "Режим выбора: щёлкните и перетащите компонент.";
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) { StatusText.Text = "Сначала выберите компонент."; return; }
            string reference = _selected.Reference;
            _project.Components.Remove(_selected);
            _selected = null;
            RefreshSelection();
            RedrawBoard();
            StatusText.Text = "Компонент " + reference + " удалён.";
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            if (_project.Components.Count > 0 || _project.Tracks.Count > 0)
            {
                var answer = MessageBox.Show("Создать новый проект? Несохранённые изменения будут потеряны.",
                    "Новый проект", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }
            _project.Name = "Новый проект";
            _project.Components.Clear(); _project.Tracks.Clear();
            _selected = null; _currentFile = null; _trackMode = false; _trackStart = null;
            RefreshSelection(); RedrawBoard();
            StatusText.Text = "Создан новый проект.";
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            string path = _currentFile;
            if (string.IsNullOrEmpty(path))
            {
                var dialog = new SaveFileDialog { Filter = "Проект печатной платы (*.pcbproj)|*.pcbproj|XML (*.xml)|*.xml",
                    DefaultExt = ".pcbproj", FileName = "Board1.pcbproj" };
                if (dialog.ShowDialog(this) != true) return;
                path = dialog.FileName;
            }
            try
            {
                var serializer = new XmlSerializer(typeof(PcbProject));
                using (var stream = File.Create(path)) serializer.Serialize(stream, _project);
                _currentFile = path;
                StatusText.Text = "Проект сохранён: " + System.IO.Path.GetFileName(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось сохранить проект:\n" + ex.Message, "Ошибка сохранения",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Проект печатной платы (*.pcbproj;*.xml)|*.pcbproj;*.xml|Все файлы (*.*)|*.*" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var serializer = new XmlSerializer(typeof(PcbProject));
                PcbProject loaded;
                using (var stream = File.OpenRead(dialog.FileName)) loaded = (PcbProject)serializer.Deserialize(stream);
                if (loaded == null) throw new InvalidDataException("Файл не содержит проект платы.");
                _project.Name = loaded.Name ?? "Проект";
                _project.Components = loaded.Components ?? new List<PcbComponent>();
                _project.Tracks = loaded.Tracks ?? new List<PcbTrack>();
                _selected = null; _currentFile = dialog.FileName; _trackMode = false; _trackStart = null;
                RefreshSelection(); RedrawBoard();
                StatusText.Text = "Открыт проект: " + System.IO.Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось открыть проект:\n" + ex.Message, "Ошибка открытия",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RunDrc_Click(object sender, RoutedEventArgs e)
        {
            var issues = new List<string>();
            foreach (var c in _project.Components)
            {
                if (c.X < 0 || c.X > 100 || c.Y < 0 || c.Y > 60)
                    issues.Add(c.Reference + ": компонент находится за границами платы.");
            }
            for (int i = 0; i < _project.Components.Count; i++)
                for (int j = i + 1; j < _project.Components.Count; j++)
                {
                    var a = _project.Components[i]; var b = _project.Components[j];
                    double required = (a.Type == "U" || b.Type == "U") ? 5.0 : 3.0;
                    if (Distance(new Point(a.X, a.Y), new Point(b.X, b.Y)) < required)
                        issues.Add(a.Reference + " и " + b.Reference + ": возможное пересечение корпусов.");
                }
            foreach (var t in _project.Tracks)
            {
                if (t.Width < 0.25) issues.Add("Дорожка: ширина меньше минимальных 0,25 мм.");
                if (t.X1 < 0 || t.X1 > 100 || t.X2 < 0 || t.X2 > 100 ||
                    t.Y1 < 0 || t.Y1 > 60 || t.Y2 < 0 || t.Y2 > 60)
                    issues.Add("Дорожка: точка находится за границами платы.");
            }
            if (issues.Count == 0)
            {
                MessageBox.Show("Проверка завершена. Обнаруженных ошибок по базовым проверкам нет.",
                    "DRC — успешно", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusText.Text = "DRC: ошибок не обнаружено.";
            }
            else
            {
                string message = string.Join("\n", issues.Take(20));
                if (issues.Count > 20) message += "\n… и ещё " + (issues.Count - 20) + " замечаний.";
                MessageBox.Show("Найдено замечаний: " + issues.Count + "\n\n" + message,
                    "Результаты DRC", MessageBoxButton.OK, MessageBoxImage.Warning);
                StatusText.Text = "DRC: замечаний — " + issues.Count + ".";
            }
        }

        private void ExportSvg_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog { Filter = "SVG (*.svg)|*.svg", DefaultExt = ".svg", FileName = "board.svg" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                using (var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8))
                {
                    writer.WriteLine("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1000\" height=\"600\" viewBox=\"0 0 1000 600\">");
                    writer.WriteLine("<rect width=\"1000\" height=\"600\" fill=\"#1f5b46\" stroke=\"#61b28b\" stroke-width=\"2\"/>");
                    foreach (var t in _project.Tracks)
                    {
                        string color = t.Layer == "Bottom" ? "#469aff" : "#f4704f";
                        writer.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "<line x1=\"{0}\" y1=\"{1}\" x2=\"{2}\" y2=\"{3}\" stroke=\"{4}\" stroke-width=\"{5}\" stroke-linecap=\"round\"/>",
                            t.X1 * 10, t.Y1 * 10, t.X2 * 10, t.Y2 * 10, color, Math.Max(2, t.Width * 10)));
                    }
                    foreach (var c in _project.Components)
                    {
                        writer.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "<rect x=\"{0}\" y=\"{1}\" width=\"28\" height=\"18\" rx=\"2\" fill=\"#dca74a\" stroke=\"#ffffff\"/>",
                            c.X * 10 - 14, c.Y * 10 - 9));
                        writer.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "<text x=\"{0}\" y=\"{1}\" fill=\"white\" font-size=\"11\">{2}</text>",
                            c.X * 10 - 14, c.Y * 10 + 24, System.Security.SecurityElement.Escape(c.Reference)));
                    }
                    writer.WriteLine("</svg>");
                }
                StatusText.Text = "SVG экспортирован: " + System.IO.Path.GetFileName(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Не удалось экспортировать SVG:\n" + ex.Message, "Ошибка экспорта",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FitBoard_Click(object sender, RoutedEventArgs e)
        {
            _zoom = 1.0;
            BoardCanvas.RenderTransform = new ScaleTransform(1, 1);
            ZoomText.Text = "Масштаб: 100%";
            StatusText.Text = "Масштаб сброшен.";
        }

        private void ToggleGrid_Click(object sender, RoutedEventArgs e)
        {
            var item = sender as MenuItem;
            _showGrid = item == null || item.IsChecked;
            RedrawBoard();
        }

        private void LayerVisibility_Changed(object sender, RoutedEventArgs e)
        {
            if (BoardCanvas != null) RedrawBoard();
        }

        private void LibraryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Selection is consumed by AddComponent_Click.
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Altium-layout — учебный прототип редактора печатных плат.\n\n" +
                "• Добавление и перемещение компонентов\n• Прокладка прямых дорожек в два клика\n" +
                "• Переключение видимости слоёв\n• Сохранение/открытие XML-проекта\n" +
                "• Базовые проверки DRC и экспорт SVG\n\n" +
                "Проверка DRC носит учебный характер и не заменяет профессиональную проверку PCB.",
                "О программе", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Exit_Click(object sender, RoutedEventArgs e) { Close(); }

        private void UpdateStatus()
        {
            if (CountText != null)
                CountText.Text = "Компоненты: " + _project.Components.Count + " • Дорожки: " + _project.Tracks.Count;
        }
    }

    [XmlRoot("PcbProject")]
    public class PcbProject
    {
        public string Name { get; set; }
        [XmlArrayItem("Component")]
        public List<PcbComponent> Components { get; set; }
        [XmlArrayItem("Track")]
        public List<PcbTrack> Tracks { get; set; }
        public PcbProject()
        {
            Name = "Новый проект";
            Components = new List<PcbComponent>();
            Tracks = new List<PcbTrack>();
        }
    }

    public class PcbComponent
    {
        public string Reference { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public PcbComponent() { }
    }

    public class PcbTrack
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double Width { get; set; }
        public string Layer { get; set; }
        public PcbTrack() { Layer = "Top"; Width = 0.35; }
    }
}
