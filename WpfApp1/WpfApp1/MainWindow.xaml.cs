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
        private bool _dragging;
        private bool _panning;
        private Point _panStart;
        private double _panHorizontalStart;
        private double _panVerticalStart;
        private bool _trackMode;
        private bool _placingComponent;
        private PcbComponent _placementPreview;
        private bool _showGrid = true;
        private Point? _trackStart;
        private Line _trackPreviewLine;
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

            if (_placingComponent && _placementPreview != null)
                DrawComponent(_placementPreview);

            if (_trackStart.HasValue)
            {
                _trackPreviewLine = new Line
                {
                    X1 = BoardLeft + _trackStart.Value.X * PixelsPerMm,
                    Y1 = BoardTop + _trackStart.Value.Y * PixelsPerMm,
                    X2 = BoardLeft + _trackStart.Value.X * PixelsPerMm,
                    Y2 = BoardTop + _trackStart.Value.Y * PixelsPerMm,
                    Stroke = new SolidColorBrush(Color.FromArgb(210, 255, 231, 125)),
                    StrokeThickness = 3,
                    StrokeDashArray = new DoubleCollection { 3, 2 },
                    IsHitTestVisible = false
                };
                BoardCanvas.Children.Add(_trackPreviewLine);
            }

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
            bool isPlacementPreview = _placingComponent && ReferenceEquals(component, _placementPreview);
            var shape = new Rectangle
            {
                Width = w, Height = h, RadiusX = component.Type == "TP" ? 12 : 3, RadiusY = component.Type == "TP" ? 12 : 3,
                Opacity = isPlacementPreview ? 0.65 : 1.0,
                Fill = body,
                Stroke = _selected == component ? Brushes.White : new SolidColorBrush(Color.FromRgb(238, 244, 248)),
                StrokeThickness = _selected == component ? 3 : 1.5,
                IsHitTestVisible = false
            };
            if (component.Type == "TP") shape.RadiusX = shape.RadiusY = 12;
            shape.RenderTransformOrigin = new Point(0.5, 0.5);
            shape.RenderTransform = new RotateTransform(component.Rotation);
            Canvas.SetLeft(shape, x - w / 2); Canvas.SetTop(shape, y - h / 2); BoardCanvas.Children.Add(shape);

            if (component.Type == "U")
            {
                for (int i = 0; i < 4; i++)
                {
                    AddRotatedPad(component, x - w / 2 - 3, y - 15 + i * 8, x, y);
                    AddRotatedPad(component, x + w / 2 - 3, y - 15 + i * 8, x, y);
                }
            }
            else if (component.Type != "TP")
            {
                AddRotatedPad(component, x - w / 2 - 3, y - 3, x, y);
                AddRotatedPad(component, x + w / 2 - 3, y - 3, x, y);
            }
            if (IsChecked(SilkLayerCheck))
            {
                var label = new TextBlock { Text = component.Reference, FontSize = 11, FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White, IsHitTestVisible = false };
                label.RenderTransformOrigin = new Point(0.5, 0.5);
                label.RenderTransform = new RotateTransform(component.Rotation);
                Canvas.SetLeft(label, x - w / 2); Canvas.SetTop(label, y + h / 2 + 3); BoardCanvas.Children.Add(label);
                var value = new TextBlock { Text = component.Value, FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(192, 211, 202)), IsHitTestVisible = false,
                    RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(component.Rotation) };
                Canvas.SetLeft(value, x - w / 2); Canvas.SetTop(value, y + h / 2 + 16); BoardCanvas.Children.Add(value);
            }
        }

        private void AddRotatedPad(PcbComponent component, double x, double y, double cx, double cy)
        {
            double radians = component.Rotation * Math.PI / 180.0;
            double dx = x + 3 - cx, dy = y + 3 - cy;
            double rx = dx * Math.Cos(radians) - dy * Math.Sin(radians);
            double ry = dx * Math.Sin(radians) + dy * Math.Cos(radians);
            AddPad(cx + rx - 3, cy + ry - 3);
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
            while (_project.Components.Any(component => component.Reference == prefix + index)) index++;

            int n = _project.Components.Count;
            _placementPreview = new PcbComponent
            {
                Reference = prefix + index,
                Type = type,
                Value = type == "R" ? "10k" : type == "C" ? "100nF" : type == "D" ? "LED" : type == "U" ? "IC" : type == "J" ? "CONN" : "Test",
                X = Math.Min(95, Math.Max(5, 15.0 + (n % 5) * 15.0)),
                Y = Math.Min(55, Math.Max(5, 15.0 + (n / 5) * 12.0))
            };
            _placingComponent = true;
            _trackMode = false;
            _trackStart = null;
            _selected = _placementPreview;
            RefreshSelection();
            RedrawBoard();
            StatusText.Text = "Размещение " + _placementPreview.Reference + ": переместите курсор и щёлкните, чтобы установить. R — поворот, Esc — отмена.";
            BoardCanvas.Cursor = Cursors.Cross;
        }

        private void LibraryList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            AddComponent_Click(sender, new RoutedEventArgs());
        }

        private void BoardCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(BoardCanvas);
            if (_placingComponent && _placementPreview != null)
            {
                if (!IsInsideBoard(p))
                {
                    StatusText.Text = "Наведите курсор внутрь платы, чтобы разместить компонент.";
                    return;
                }

                Point placement = SnapComponentCenter(ToMm(p), _placementPreview);
                _placementPreview.X = placement.X;
                _placementPreview.Y = placement.Y;
                _project.Components.Add(_placementPreview);
                _selected = _placementPreview;
                string placedReference = _placementPreview.Reference;
                _placementPreview = null;
                _placingComponent = false;
                BoardCanvas.Cursor = Cursors.Arrow;
                RefreshSelection();
                RedrawBoard();
                StatusText.Text = placedReference + " установлен. Можно перетащить его или добавить следующий компонент.";
                e.Handled = true;
                return;
            }

            Point ignored = p;
            if (_trackMode)
            {
                if (!IsInsideBoard(p))
                {
                    StatusText.Text = "Начало и конец дорожки должны находиться внутри платы.";
                    return;
                }
                Point mm = SnapPoint(ToMm(p));
                if (_trackStart == null)
                {
                    _trackStart = mm;
                    RedrawBoard();
                    StatusText.Text = string.Format("Начало трассы: X {0:0.00}, Y {1:0.00} мм. Щёлкните конечную точку.", mm.X, mm.Y);
                }
                else
                {
                    if (Distance(_trackStart.Value, mm) > 0.1)
                    {
                        _project.Tracks.Add(new PcbTrack
                        {
                            X1 = _trackStart.Value.X, Y1 = _trackStart.Value.Y,
                            X2 = mm.X, Y2 = mm.Y,
                            Width = GetSelectedTrackWidth(),
                            Layer = GetSelectedTrackLayer()
                        });
                        StatusText.Text = "Дорожка добавлена (" + GetSelectedTrackWidth().ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                            " мм, " + GetSelectedTrackLayer() + "). Можно прокладывать следующую.";
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
            if (_panning && e.MiddleButton == MouseButtonState.Pressed)
            {
                Point current = e.GetPosition(BoardScrollViewer);
                BoardScrollViewer.ScrollToHorizontalOffset(_panHorizontalStart + _panStart.X - current.X);
                BoardScrollViewer.ScrollToVerticalOffset(_panVerticalStart + _panStart.Y - current.Y);
                return;
            }

            Point p = e.GetPosition(BoardCanvas);
            if (_placingComponent && _placementPreview != null && IsInsideBoard(p))
            {
                Point mm = SnapComponentCenter(ToMm(p), _placementPreview);
                _placementPreview.X = mm.X;
                _placementPreview.Y = mm.Y;
                RefreshSelection();
                RedrawBoard();
            }
            else if (_dragging && _selected != null && e.LeftButton == MouseButtonState.Pressed && IsInsideBoard(p))
            {
                Point mm = SnapComponentCenter(ToMm(p), _selected);
                _selected.X = mm.X;
                _selected.Y = mm.Y;
                RefreshSelection();
                RedrawBoard();
            }
            if (IsInsideBoard(p))
            {
                Point mm = SnapPoint(ToMm(p));
                if (_trackMode && _trackStart.HasValue && _trackPreviewLine != null)
                {
                    _trackPreviewLine.X2 = BoardLeft + mm.X * PixelsPerMm;
                    _trackPreviewLine.Y2 = BoardTop + mm.Y * PixelsPerMm;
                }
                else if (!_dragging && !_trackMode && !_placingComponent)
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
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                {
                    BoardScrollViewer.ScrollToHorizontalOffset(BoardScrollViewer.HorizontalOffset - e.Delta);
                    e.Handled = true;
                }
                return;
            }

            double factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
            double newZoom = Math.Max(0.4, Math.Min(3.0, _zoom * factor));
            if (Math.Abs(newZoom - _zoom) < 0.001) return;

            _zoom = newZoom;
            BoardCanvas.LayoutTransform = new ScaleTransform(_zoom, _zoom);
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
            bool textInputFocused = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

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
                _placingComponent = false;
                _placementPreview = null;
                _selected = _project.Components.Contains(_selected) ? _selected : null;
                BoardCanvas.Cursor = Cursors.Arrow;
                RefreshSelection();
                RedrawBoard();
                StatusText.Text = "Операция отменена. Режим выбора.";
            }
            else if (!control && e.Key == Key.R && _placingComponent && _placementPreview != null)
            {
                _placementPreview.Rotation = (_placementPreview.Rotation + 90) % 360;
                RefreshSelection();
                RedrawBoard();
                StatusText.Text = "Поворот предпросмотра: " + _placementPreview.Rotation + "°. Щёлкните для установки.";
            }
            else if (!control && !textInputFocused && e.Key == Key.T)
                TrackMode_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && e.Key == Key.V)
                SelectMode_Click(this, new RoutedEventArgs());
            else return;

            e.Handled = true;
        }

        private Point SnapPoint(Point mm)
        {
            if (SnapCheck == null || SnapCheck.IsChecked != true) return mm;

            Point best = mm;
            double bestDistance = 0.8;
            foreach (var component in _project.Components)
            {
                double halfWidth = component.Type == "U" ? 2.9 : component.Type == "J" ? 2.4 :
                    component.Type == "TP" ? 0 : 1.7;
                if (component.Type == "U")
                {
                    for (int i = 0; i < 4; i++)
                    {
                        ConsiderSnap(mm, RotatePadMm(component, -2.9, -1.2 + i * 0.8), ref best, ref bestDistance);
                        ConsiderSnap(mm, RotatePadMm(component, 2.9, -1.2 + i * 0.8), ref best, ref bestDistance);
                    }
                }
                else if (component.Type != "TP")
                {
                    ConsiderSnap(mm, RotatePadMm(component, -halfWidth, 0), ref best, ref bestDistance);
                    ConsiderSnap(mm, RotatePadMm(component, halfWidth, 0), ref best, ref bestDistance);
                }
            }

            if (bestDistance < 0.8) return best;
            return new Point(Math.Round(mm.X * 2.0) / 2.0, Math.Round(mm.Y * 2.0) / 2.0);
        }

        private static Point RotatePadMm(PcbComponent component, double dx, double dy)
        {
            double radians = component.Rotation * Math.PI / 180.0;
            return new Point(component.X + dx * Math.Cos(radians) - dy * Math.Sin(radians),
                component.Y + dx * Math.Sin(radians) + dy * Math.Cos(radians));
        }

        private static void ConsiderSnap(Point source, Point target, ref Point best, ref double bestDistance)
        {
            double distance = Distance(source, target);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = target;
            }
        }

        private double GetSelectedTrackWidth()
        {
            var item = TrackWidthComboBox == null ? null : TrackWidthComboBox.SelectedItem as ComboBoxItem;
            double width;
            return item != null && double.TryParse(Convert.ToString(item.Tag), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out width) ? width : 0.35;
        }

        private string GetSelectedTrackLayer()
        {
            var item = TrackLayerComboBox == null ? null : TrackLayerComboBox.SelectedItem as ComboBoxItem;
            return item == null ? "Top" : Convert.ToString(item.Tag);
        }

        private void RotateSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null)
            {
                StatusText.Text = "Сначала выберите компонент для поворота.";
                return;
            }

            _selected.Rotation = (_selected.Rotation + 90) % 360;
            RedrawBoard();
            StatusText.Text = _selected.Reference + " повёрнут на " + _selected.Rotation + "°.";
        }

        private static Point SnapComponentCenter(Point mm, PcbComponent component)
        {
            double halfWidth = component.Type == "U" ? 2.9 : component.Type == "J" ? 2.4 : component.Type == "TP" ? 1.2 : 1.7;
            double halfHeight = component.Type == "U" ? 2.1 : component.Type == "J" ? 1.6 : component.Type == "TP" ? 1.2 : 1.1;
            if (component.Rotation % 180 != 0) { double temp = halfWidth; halfWidth = halfHeight; halfHeight = temp; }
            double x = Math.Max(halfWidth, Math.Min(100 - halfWidth, mm.X));
            double y = Math.Max(halfHeight, Math.Min(60 - halfHeight, mm.Y));
            if (SnapCheck != null && SnapCheck.IsChecked == true)
            {
                x = Math.Round(x * 2.0) / 2.0;
                y = Math.Round(y * 2.0) / 2.0;
            }
            return new Point(x, y);
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
            bool hasSelection = _selected != null;
            SelectedName.Text = hasSelection ? _selected.Reference + "  ·  " + _selected.Value : "Ничего не выбрано";
            XBox.Text = hasSelection ? _selected.X.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "0";
            YBox.Text = hasSelection ? _selected.Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) : "0";
            ReferenceBox.Text = hasSelection ? _selected.Reference : "";
            ValueBox.Text = hasSelection ? _selected.Value : "";
            ReferenceBox.IsEnabled = hasSelection;
            ValueBox.IsEnabled = hasSelection;
            XBox.IsEnabled = hasSelection;
            YBox.IsEnabled = hasSelection;
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
            string reference = ReferenceBox.Text.Trim();
            string value = ValueBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(value))
            {
                MessageBox.Show("Заполните обозначение и значение компонента.", "Не хватает данных",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (_project.Components.Any(component => component != _selected &&
                string.Equals(component.Reference, reference, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("Обозначение " + reference + " уже используется.", "Дубликат обозначения",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _selected.X = x; _selected.Y = y;
            _selected.Reference = reference; _selected.Value = value;
            RefreshSelection();
            RedrawBoard();
            StatusText.Text = "Свойства " + _selected.Reference + " обновлены.";
        }

        private void TrackMode_Click(object sender, RoutedEventArgs e)
        {
            _trackMode = true; _dragging = false; _trackStart = null; _placingComponent = false; _placementPreview = null;
            BoardCanvas.Cursor = Cursors.Cross;
            RedrawBoard();
            StatusText.Text = "Режим дорожки: щёлкните начальную, затем конечную точку внутри платы.";
        }

        private void SelectMode_Click(object sender, RoutedEventArgs e)
        {
            _trackMode = false; _trackStart = null; _placingComponent = false; _placementPreview = null;
            BoardCanvas.Cursor = Cursors.Arrow;
            RedrawBoard();
            StatusText.Text = "Режим выбора: щёлкните и перетащите компонент.";
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) { StatusText.Text = "Сначала выберите компонент."; return; }
            string reference = _selected.Reference;
            _project.Components.Remove(_selected);
            _selected = null;
            _placingComponent = false;
            _placementPreview = null;
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
            _placingComponent = false; _placementPreview = null; BoardCanvas.Cursor = Cursors.Arrow;
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
                _placingComponent = false; _placementPreview = null; BoardCanvas.Cursor = Cursors.Arrow;
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
                double halfWidth = c.Type == "U" ? 2.9 : c.Type == "J" ? 2.4 : c.Type == "TP" ? 1.2 : 1.7;
                double halfHeight = c.Type == "U" ? 2.1 : c.Type == "J" ? 1.6 : c.Type == "TP" ? 1.2 : 1.1;
                if (c.Rotation % 180 != 0) { double temp = halfWidth; halfWidth = halfHeight; halfHeight = temp; }
                if (c.X - halfWidth < 0 || c.X + halfWidth > 100 || c.Y - halfHeight < 0 || c.Y + halfHeight > 60)
                    issues.Add(c.Reference + ": корпус или контактная площадка выходит за границы платы.");
            }
            for (int i = 0; i < _project.Components.Count; i++)
                for (int j = i + 1; j < _project.Components.Count; j++)
                {
                    var a = _project.Components[i]; var b = _project.Components[j];
                    double aw = a.Type == "U" ? 5.8 : a.Type == "J" ? 4.8 : a.Type == "TP" ? 2.4 : 3.4;
                    double ah = a.Type == "U" ? 4.2 : a.Type == "J" ? 3.2 : a.Type == "TP" ? 2.4 : 2.2;
                    double bw = b.Type == "U" ? 5.8 : b.Type == "J" ? 4.8 : b.Type == "TP" ? 2.4 : 3.4;
                    double bh = b.Type == "U" ? 4.2 : b.Type == "J" ? 3.2 : b.Type == "TP" ? 2.4 : 2.2;
                    if (a.Rotation % 180 != 0) { double temp = aw; aw = ah; ah = temp; }
                    if (b.Rotation % 180 != 0) { double temp = bw; bw = bh; bh = temp; }
                    if (Math.Abs(a.X - b.X) < (aw + bw) / 2.0 && Math.Abs(a.Y - b.Y) < (ah + bh) / 2.0)
                        issues.Add(a.Reference + " и " + b.Reference + ": корпуса перекрываются.");
                }
            for (int i = 0; i < _project.Tracks.Count; i++)
            {
                var t = _project.Tracks[i];
                if (t.Width < 0.25) issues.Add("Дорожка " + (i + 1) + ": ширина меньше минимальных 0,25 мм.");
                if (t.X1 < 0 || t.X1 > 100 || t.X2 < 0 || t.X2 > 100 ||
                    t.Y1 < 0 || t.Y1 > 60 || t.Y2 < 0 || t.Y2 > 60)
                    issues.Add("Дорожка " + (i + 1) + ": точка находится за границами платы.");
                if (Distance(new Point(t.X1, t.Y1), new Point(t.X2, t.Y2)) < 0.1)
                    issues.Add("Дорожка " + (i + 1) + ": нулевая или слишком малая длина.");
                for (int j = i + 1; j < _project.Tracks.Count; j++)
                {
                    var other = _project.Tracks[j];
                    if (t.Layer != other.Layer) continue;
                    double gap = SegmentDistance(t, other) - (t.Width + other.Width) / 2.0;
                    if (gap < 0.20 && !SegmentsShareEndpoint(t, other))
                        issues.Add("Дорожки " + (i + 1) + " и " + (j + 1) + ": зазор " +
                            Math.Max(0, gap).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                            " мм меньше 0,20 мм.");
                }
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

        private static bool SegmentsShareEndpoint(PcbTrack a, PcbTrack b)
        {
            const double epsilon = 0.05;
            return Distance(new Point(a.X1, a.Y1), new Point(b.X1, b.Y1)) < epsilon ||
                   Distance(new Point(a.X1, a.Y1), new Point(b.X2, b.Y2)) < epsilon ||
                   Distance(new Point(a.X2, a.Y2), new Point(b.X1, b.Y1)) < epsilon ||
                   Distance(new Point(a.X2, a.Y2), new Point(b.X2, b.Y2)) < epsilon;
        }

        private static double SegmentDistance(PcbTrack a, PcbTrack b)
        {
            if (SegmentsIntersect(a, b)) return 0;
            return Math.Min(Math.Min(PointSegmentDistance(a.X1, a.Y1, b), PointSegmentDistance(a.X2, a.Y2, b)),
                Math.Min(PointSegmentDistance(b.X1, b.Y1, a), PointSegmentDistance(b.X2, b.Y2, a)));
        }

        private static double PointSegmentDistance(double x, double y, PcbTrack t)
        {
            double dx = t.X2 - t.X1, dy = t.Y2 - t.Y1;
            double lengthSquared = dx * dx + dy * dy;
            if (lengthSquared < 1e-9) return Distance(new Point(x, y), new Point(t.X1, t.Y1));
            double u = Math.Max(0, Math.Min(1, ((x - t.X1) * dx + (y - t.Y1) * dy) / lengthSquared));
            return Distance(new Point(x, y), new Point(t.X1 + u * dx, t.Y1 + u * dy));
        }

        private static bool SegmentsIntersect(PcbTrack a, PcbTrack b)
        {
            double o1 = Orientation(a.X1, a.Y1, a.X2, a.Y2, b.X1, b.Y1);
            double o2 = Orientation(a.X1, a.Y1, a.X2, a.Y2, b.X2, b.Y2);
            double o3 = Orientation(b.X1, b.Y1, b.X2, b.Y2, a.X1, a.Y1);
            double o4 = Orientation(b.X1, b.Y1, b.X2, b.Y2, a.X2, a.Y2);
            return o1 * o2 < 0 && o3 * o4 < 0;
        }

        private static double Orientation(double ax, double ay, double bx, double by, double cx, double cy)
        {
            return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
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
            BoardCanvas.LayoutTransform = new ScaleTransform(1, 1);
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
        public int Rotation { get; set; }
        public PcbComponent() { Rotation = 0; }
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
