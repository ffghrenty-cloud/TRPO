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
        private bool _placingHole;
        private Point? _holePreviewPoint;
        private bool _showGrid = true;
        private Point? _trackStart;
        private Line _trackPreviewLine;
        private double _zoom = 1.0;
        private readonly double[] _gridPresets = new double[] { 1.0, 0.5, 0.1 };
        private PcbComponent _clipboardComponent;
        private readonly Stack<string> _undoStack = new Stack<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();
        private int _gridPresetIndex = 1;
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
                for (double x = BoardLeft + _gridPresets[_gridPresetIndex] * PixelsPerMm; x < BoardLeft + BoardWidth; x += _gridPresets[_gridPresetIndex] * PixelsPerMm)
                {
                    var line = new Line { X1 = x, X2 = x, Y1 = BoardTop, Y2 = BoardTop + BoardHeight,
                        Stroke = new SolidColorBrush(Color.FromArgb(45, 215, 238, 225)), StrokeThickness = 0.5 };
                    BoardCanvas.Children.Add(line);
                }
                for (double y = BoardTop + _gridPresets[_gridPresetIndex] * PixelsPerMm; y < BoardTop + BoardHeight; y += _gridPresets[_gridPresetIndex] * PixelsPerMm)
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

            foreach (var hole in _project.Holes ?? new List<PcbHole>())
                DrawHole(hole.X, hole.Y, hole.Diameter, false);

            if (_placingHole && _holePreviewPoint.HasValue)
                DrawHole(_holePreviewPoint.Value.X, _holePreviewPoint.Value.Y, 3.2, true);

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

        private void DrawHole(double xMm, double yMm, double diameterMm, bool preview)
        {
            double diameter = Math.Max(1.0, diameterMm * PixelsPerMm);
            double cx = BoardLeft + xMm * PixelsPerMm;
            double cy = BoardTop + yMm * PixelsPerMm;
            var ring = new Ellipse
            {
                Width = diameter + 8, Height = diameter + 8,
                Fill = new SolidColorBrush(Color.FromArgb(preview ? (byte)80 : (byte)255, 220, 171, 74)),
                Stroke = preview ? Brushes.White : new SolidColorBrush(Color.FromRgb(255, 210, 112)),
                StrokeThickness = 1.5, IsHitTestVisible = false
            };
            Canvas.SetLeft(ring, cx - (diameter + 8) / 2);
            Canvas.SetTop(ring, cy - (diameter + 8) / 2);
            BoardCanvas.Children.Add(ring);
            var bore = new Ellipse
            {
                Width = diameter, Height = diameter,
                Fill = new SolidColorBrush(Color.FromRgb(17, 26, 40)),
                Stroke = new SolidColorBrush(Color.FromRgb(8, 12, 18)), StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(bore, cx - diameter / 2);
            Canvas.SetTop(bore, cy - diameter / 2);
            BoardCanvas.Children.Add(bore);
            BoardCanvas.Children.Add(new Line { X1 = cx - diameter / 2 - 4, X2 = cx + diameter / 2 + 4,
                Y1 = cy, Y2 = cy, Stroke = Brushes.White, StrokeThickness = 0.7, IsHitTestVisible = false });
            BoardCanvas.Children.Add(new Line { X1 = cx, X2 = cx,
                Y1 = cy - diameter / 2 - 4, Y2 = cy + diameter / 2 + 4,
                Stroke = Brushes.White, StrokeThickness = 0.7, IsHitTestVisible = false });
            if (!preview)
            {
                var label = new TextBlock { Text = diameterMm.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " mm",
                    FontSize = 9, Foreground = Brushes.White, IsHitTestVisible = false };
                Canvas.SetLeft(label, cx + diameter / 2 + 5);
                Canvas.SetTop(label, cy - 7);
                BoardCanvas.Children.Add(label);
            }
        }

        private void AddMountingHole_Click(object sender, RoutedEventArgs e)
        {
            _placingHole = true;
            _holePreviewPoint = null;
            _placingComponent = false;
            _placementPreview = null;
            _trackMode = false;
            _trackStart = null;
            _selected = null;
            BoardCanvas.Cursor = Cursors.Cross;
            Mouse.OverrideCursor = Cursors.Cross;
            RefreshSelection();
            RedrawBoard();
            StatusText.Text = "Монтажное отверстие Ø3,2 мм: щёлкните внутри платы для установки; Esc — отмена.";
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
                Stroke = isPlacementPreview ? new SolidColorBrush(Color.FromRgb(95, 240, 255)) : (_selected == component ? Brushes.White : new SolidColorBrush(Color.FromRgb(238, 244, 248))),
                StrokeThickness = isPlacementPreview ? 2.5 : (_selected == component ? 3 : 1.5),
                StrokeDashArray = isPlacementPreview ? new DoubleCollection { 3, 2 } : null,
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
            else if (component.Type == "Q")
            {
                AddRotatedPad(component, x - w / 2 - 3, y - 3, x, y);
                AddRotatedPad(component, x + w / 2 - 3, y - 3, x, y);
                AddRotatedPad(component, x - 3, y + h / 2 - 3, x, y);
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

        private void LibraryList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = ItemsControl.ContainerFromElement(LibraryList, e.OriginalSource as DependencyObject) as ListBoxItem;
            if (item == null) return;

            // Handle the library click here rather than letting ListBox begin its
            // own mouse-capture/selection gesture. Placement is a separate action:
            // the next click on the board commits the component.
            e.Handled = true;
            LibraryList.SelectedItem = item;
            AddComponent_Click(LibraryList, new RoutedEventArgs());
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
                Value = type == "R" ? "10k" : type == "C" ? "100nF" : type == "D" ? "LED" : type == "U" ? "IC" : type == "J" ? "CONN" : type == "Q" ? "NPN" : type == "L" ? "10uH" : type == "SW" ? "SPST" : type == "H" ? "2.54mm" : "Test",
                X = Math.Min(95, Math.Max(5, 15.0 + (n % 5) * 15.0)),
                Y = Math.Min(55, Math.Max(5, 15.0 + (n / 5) * 12.0))
            };
            _placingComponent = true;
            _placingHole = false;
            _holePreviewPoint = null;
            _trackMode = false;
            _trackStart = null;
            _dragging = false;
            _selected = _placementPreview;
            BoardCanvas.ReleaseMouseCapture();
            BoardCanvas.Cursor = Cursors.Cross;
            Mouse.OverrideCursor = Cursors.Cross;
            RefreshSelection();
            RedrawBoard();
            StatusText.Text = "Выбран " + _placementPreview.Reference + ". Щёлкните ЛКМ по плате, чтобы закрепить компонент в этой точке. R — поворот, Esc — отмена.";
        }

        // Handle placement in the tunnelling phase. The canvas is redrawn on
        // every mouse move, so relying on a later bubbling mouse event can lose
        // the click when WPF changes the visual child under the pointer.
        private void BoardCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            Point p = e.GetPosition(BoardCanvas);
            if (p.X < 0 || p.Y < 0 || p.X > BoardCanvas.ActualWidth || p.Y > BoardCanvas.ActualHeight)
                return;

            // Commit holes here rather than forwarding the same routed event.
            // This makes placement independent of the visual element hit on the board.
            if (_placingHole)
            {
                e.Handled = true;
                if (!IsInsideBoard(p))
                {
                    StatusText.Text = "Монтажное отверстие должно находиться внутри платы.";
                    return;
                }

                Point mm = SnapPoint(ToMm(p));
                mm.X = Math.Max(2.1, Math.Min(97.9, mm.X));
                mm.Y = Math.Max(2.1, Math.Min(57.9, mm.Y));
                string holeError;
                if (!CanPlaceHole(mm.X, mm.Y, 3.2, null, out holeError))
                {
                    StatusText.Text = holeError;
                    e.Handled = true;
                    return;
                }
                PushUndoSnapshot();
                if (_project.Holes == null) _project.Holes = new List<PcbHole>();
                _project.Holes.Add(new PcbHole { X = mm.X, Y = mm.Y, Diameter = 3.2 });
                _placingHole = false;
                _holePreviewPoint = null;
                BoardCanvas.Cursor = Cursors.Arrow;
                Mouse.OverrideCursor = null;
                RedrawBoard();
                StatusText.Text = string.Format("Монтажное отверстие Ø3,2 мм установлено: X {0:0.0}, Y {1:0.0} мм.", mm.X, mm.Y);
                return;
            }

            // Commit the component directly in PreviewMouseLeftButtonDown.
            if (_placingComponent && _placementPreview != null)
            {
                e.Handled = true;
                if (!IsInsideBoard(p))
                {
                    StatusText.Text = "Наведите курсор внутрь платы, чтобы разместить компонент.";
                    return;
                }

                Point placement = SnapComponentCenter(ToMm(p), _placementPreview);
                string placementError;
                if (!CanPlaceComponent(_placementPreview, placement.X, placement.Y, null, out placementError))
                {
                    StatusText.Text = placementError;
                    return;
                }
                PcbComponent placedComponent = _placementPreview;
                placedComponent.X = placement.X;
                placedComponent.Y = placement.Y;
                PushUndoSnapshot();
                _project.Components.Add(placedComponent);
                _selected = placedComponent;
                string placedReference = placedComponent.Reference;
                _placementPreview = null;
                _placingComponent = false;
                _dragging = false;
                BoardCanvas.ReleaseMouseCapture();
                BoardCanvas.Cursor = Cursors.Arrow;
                Mouse.OverrideCursor = null;
                RedrawBoard();
                RefreshSelection();
                UpdateStatus();
                StatusText.Text = placedReference + " закреплён на плате: X " +
                    placement.X.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " мм, Y " + placement.Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " мм. Компонент добавлен в проект.";
                return;
            }

            // Other canvas actions (tracks and selecting existing components)
            // continue through the shared mouse handler.
            BoardCanvas_MouseLeftButtonDown(BoardCanvas, e);
            e.Handled = true;
        }

        private void BoardCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point p = e.GetPosition(BoardCanvas);
            if (_placingHole)
            {
                if (!IsInsideBoard(p))
                {
                    StatusText.Text = "Монтажное отверстие должно находиться внутри платы.";
                    e.Handled = true;
                    return;
                }
                Point mm = SnapPoint(ToMm(p));
                mm.X = Math.Max(2.1, Math.Min(97.9, mm.X));
                mm.Y = Math.Max(2.1, Math.Min(57.9, mm.Y));
                string holeError;
                if (!CanPlaceHole(mm.X, mm.Y, 3.2, null, out holeError))
                {
                    StatusText.Text = holeError;
                    e.Handled = true;
                    return;
                }
                PushUndoSnapshot();
                if (_project.Holes == null) _project.Holes = new List<PcbHole>();
                _project.Holes.Add(new PcbHole { X = mm.X, Y = mm.Y, Diameter = 3.2 });
                _placingHole = false;
                _holePreviewPoint = null;
                BoardCanvas.Cursor = Cursors.Arrow;
                RedrawBoard();
                StatusText.Text = string.Format("Монтажное отверстие Ø3,2 мм установлено: X {0:0.0}, Y {1:0.0} мм.", mm.X, mm.Y);
                e.Handled = true;
                return;
            }

            if (_placingComponent && _placementPreview != null)
            {
                if (!IsInsideBoard(p))
                {
                    StatusText.Text = "Наведите курсор внутрь платы, чтобы разместить компонент.";
                    return;
                }

                Point placement = SnapComponentCenter(ToMm(p), _placementPreview);
                string placementError;
                if (!CanPlaceComponent(_placementPreview, placement.X, placement.Y, null, out placementError))
                {
                    StatusText.Text = placementError;
                    return;
                }
                PcbComponent placedComponent = _placementPreview;
                placedComponent.X = placement.X;
                placedComponent.Y = placement.Y;
                PushUndoSnapshot();
                _project.Components.Add(placedComponent);
                _selected = placedComponent;
                string placedReference = placedComponent.Reference;

                // End placement before redrawing so mouse movement cannot keep moving
                // the newly committed component as if it were still a preview.
                _placementPreview = null;
                _placingComponent = false;
                _dragging = false;
                BoardCanvas.ReleaseMouseCapture();
                BoardCanvas.Cursor = Cursors.Arrow;
                RedrawBoard();
                RefreshSelection();
                UpdateStatus();
                StatusText.Text = placedReference + " закреплён на плате: X " +
                    placement.X.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " мм, Y " + placement.Y.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " мм. Компонент добавлен в проект.";
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
                        PushUndoSnapshot();
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
                PushUndoSnapshot();
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
            if (_placingHole && IsInsideBoard(p))
            {
                Point mm = SnapPoint(ToMm(p));
                mm.X = Math.Max(2, Math.Min(98, mm.X));
                mm.Y = Math.Max(2, Math.Min(58, mm.Y));
                _holePreviewPoint = mm;
                RedrawBoard();
            }
            else if (_placingComponent && _placementPreview != null && IsInsideBoard(p))
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
                string placementError;
                if (CanPlaceComponent(_selected, mm.X, mm.Y, _selected, out placementError))
                {
                    _selected.X = mm.X;
                    _selected.Y = mm.Y;
                    RefreshSelection();
                    RedrawBoard();
                }
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
            // Placement is committed only by PreviewMouseLeftButtonDown.
            // Replaying the press handler from MouseLeftButtonUp is unsafe:
            // it can treat a release as a second placement attempt and reset
            // the cursor/mode unexpectedly when WPF changes the hit-tested child.
            _dragging = false;
            if (!_placingComponent && !_placingHole)
                BoardCanvas.ReleaseMouseCapture();
        }

        // Обычное колесо прокручивает рабочую область; Ctrl+колесо меняет масштаб.
        private void BoardCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                BoardScrollViewer.ScrollToHorizontalOffset(BoardScrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
                return;
            }

            ChangeZoom(e.Delta > 0 ? 1.1 : 1.0 / 1.1);
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
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
            bool textInputFocused = Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase;

            if (control && e.Key == Key.S) SaveProject_Click(this, new RoutedEventArgs());
            else if (control && e.Key == Key.O) OpenProject_Click(this, new RoutedEventArgs());
            else if (control && e.Key == Key.N) NewProject_Click(this, new RoutedEventArgs());
            else if (control && (e.Key == Key.E || e.Key == Key.P)) ExportSvg_Click(this, new RoutedEventArgs());
            else if (control && e.Key == Key.D0) FitBoard_Click(this, new RoutedEventArgs());
            else if (control && e.Key == Key.G && !textInputFocused)
            {
                SnapCheck.IsChecked = SnapCheck.IsChecked != true;
                StatusText.Text = SnapCheck.IsChecked == true ? "Привязка к сетке включена." : "Привязка к сетке выключена.";
            }
            else if (control && e.Key == Key.D && !textInputFocused) DuplicateSelected();
            else if (control && e.Key == Key.C && !textInputFocused) CopySelected();
            else if (control && e.Key == Key.X && !textInputFocused) CutSelected();
            else if (control && e.Key == Key.V && !textInputFocused) PasteClipboard();
            else if (control && e.Key == Key.Z && !textInputFocused) Undo();
            else if (control && e.Key == Key.Y && !textInputFocused) Redo();
            else if (!control && !textInputFocused && e.Key == Key.Delete) DeleteSelected_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && e.Key == Key.G && shift)
            {
                _gridPresetIndex = (_gridPresetIndex + 1) % _gridPresets.Length;
                RedrawBoard();
                StatusText.Text = "Шаг сетки: " + _gridPresets[_gridPresetIndex].ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " мм.";
            }
            else if (!control && !textInputFocused && e.Key == Key.G)
            {
                _showGrid = !_showGrid;
                RedrawBoard();
                StatusText.Text = _showGrid ? "Сетка включена." : "Сетка выключена.";
            }
            else if (!control && !textInputFocused && (e.Key == Key.C))
            {
                LibrarySearchBox.Focus();
                LibrarySearchBox.SelectAll();
                StatusText.Text = "Поиск компонента: введите имя, тип или обозначение.";
            }
            else if (!control && !textInputFocused && (e.Key == Key.F || e.Key == Key.Space))
            {
                if (e.Key == Key.Space && _dragging && _selected != null)
                {
                    _selected.Rotation = (_selected.Rotation + 90) % 360;
                    RedrawBoard();
                    StatusText.Text = _selected.Reference + " повёрнут на 90°.";
                }
                else FitBoard_Click(this, new RoutedEventArgs());
            }
            else if (!control && !textInputFocused && (e.Key == Key.Add || e.Key == Key.OemPlus))
                ChangeZoom(1.1);
            else if (!control && !textInputFocused && (e.Key == Key.Subtract || e.Key == Key.OemMinus))
                ChangeZoom(1.0 / 1.1);
            else if (!control && !textInputFocused && (e.Key == Key.R))
            {
                if (_placingComponent && _placementPreview != null)
                {
                    _placementPreview.Rotation = (_placementPreview.Rotation + (shift ? 270 : 90)) % 360;
                    RefreshSelection();
                    RedrawBoard();
                    StatusText.Text = "Поворот предпросмотра: " + _placementPreview.Rotation + "°. Щёлкните для установки.";
                }
                else if (_selected != null)
                {
                    PushUndoSnapshot();
                    _selected.Rotation = (_selected.Rotation + (shift ? 270 : 90)) % 360;
                    RedrawBoard();
                    StatusText.Text = _selected.Reference + " повёрнут на " + _selected.Rotation + "°.";
                }
            }
            else if (!control && !textInputFocused && e.Key == Key.F1)
                Help_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && e.Key == Key.Tab && _placingComponent && _placementPreview != null)
            {
                ReferenceBox.Focus();
                ReferenceBox.SelectAll();
                StatusText.Text = "Редактирование свойств компонента. Изменения примените кнопкой свойств; Esc отменяет размещение.";
            }
            else if (!control && !textInputFocused && e.Key == Key.H)
                AddMountingHole_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && (e.Key == Key.T))
                TrackMode_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && (e.Key == Key.V || e.Key == Key.S))
                SelectMode_Click(this, new RoutedEventArgs());
            else if (!control && !textInputFocused && (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down))
            {
                if (_selected == null)
                {
                    double step = 60;
                    if (e.Key == Key.Left) BoardScrollViewer.ScrollToHorizontalOffset(BoardScrollViewer.HorizontalOffset - step);
                    else if (e.Key == Key.Right) BoardScrollViewer.ScrollToHorizontalOffset(BoardScrollViewer.HorizontalOffset + step);
                    else if (e.Key == Key.Up) BoardScrollViewer.ScrollToVerticalOffset(BoardScrollViewer.VerticalOffset - step);
                    else BoardScrollViewer.ScrollToVerticalOffset(BoardScrollViewer.VerticalOffset + step);
                }
                else
                {
                    double step = SnapCheck.IsChecked == true ? _gridPresets[_gridPresetIndex] : 0.5;
                    double nextX = _selected.X;
                    double nextY = _selected.Y;
                    if (e.Key == Key.Left) nextX -= step;
                    else if (e.Key == Key.Right) nextX += step;
                    else if (e.Key == Key.Up) nextY -= step;
                    else nextY += step;
                    Point next = SnapComponentCenter(new Point(nextX, nextY), _selected);
                    string moveError;
                    if (CanPlaceComponent(_selected, next.X, next.Y, _selected, out moveError))
                    {
                        PushUndoSnapshot();
                        _selected.X = next.X;
                        _selected.Y = next.Y;
                        RefreshSelection();
                        RedrawBoard();
                    }
                    else
                    {
                        StatusText.Text = moveError;
                    }
                }
            }
            else if (e.Key == Key.Escape && !textInputFocused)
            {
                _trackMode = false;
                _trackStart = null;
                _dragging = false;
                _placingComponent = false;
                _placementPreview = null;
                _placingHole = false;
                _holePreviewPoint = null;
                _selected = _project.Components.Contains(_selected) ? _selected : null;
                BoardCanvas.Cursor = Cursors.Arrow;
                Mouse.OverrideCursor = null;
                RefreshSelection();
                RedrawBoard();
                StatusText.Text = "Операция отменена. Режим выбора.";
            }
            else return;

            e.Handled = true;
        }

        private void ChangeZoom(double factor)
        {
            _zoom = Math.Max(0.25, Math.Min(3.0, _zoom * factor));
            BoardCanvas.LayoutTransform = new ScaleTransform(_zoom, _zoom);
            ZoomText.Text = "Масштаб: " + Math.Round(_zoom * 100) + "%";
        }

        private string CaptureProject()
        {
            var serializer = new XmlSerializer(typeof(PcbProject));
            using (var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture))
            {
                serializer.Serialize(writer, _project);
                return writer.ToString();
            }
        }

        private void PushUndoSnapshot()
        {
            _undoStack.Push(CaptureProject());
            while (_undoStack.Count > 50)
            {
                var retained = _undoStack.Take(50).Reverse().ToArray();
                _undoStack.Clear();
                foreach (var snapshot in retained) _undoStack.Push(snapshot);
            }
            _redoStack.Clear();
        }

        private void RestoreProject(string snapshot)
        {
            var serializer = new XmlSerializer(typeof(PcbProject));
            using (var reader = new StringReader(snapshot))
            {
                var restored = (PcbProject)serializer.Deserialize(reader);
                _project.Name = restored.Name;
                _project.Components = restored.Components ?? new List<PcbComponent>();
                _project.Tracks = restored.Tracks ?? new List<PcbTrack>();
                _project.Holes = restored.Holes ?? new List<PcbHole>();
            }
            _selected = null;
            _placingComponent = false;
            _placementPreview = null;
            _trackMode = false;
            _trackStart = null;
            BoardCanvas.Cursor = Cursors.Arrow;
            Mouse.OverrideCursor = null;
            RefreshSelection();
            RedrawBoard();
            UpdateStatus();
        }

        private void Undo()
        {
            if (_undoStack.Count == 0) { StatusText.Text = "Нет действий для отмены."; return; }
            _redoStack.Push(CaptureProject());
            RestoreProject(_undoStack.Pop());
            StatusText.Text = "Действие отменено.";
        }

        private void Redo()
        {
            if (_redoStack.Count == 0) { StatusText.Text = "Нет действий для повтора."; return; }
            _undoStack.Push(CaptureProject());
            RestoreProject(_redoStack.Pop());
            StatusText.Text = "Действие повторено.";
        }

        private void CopySelected()
        {
            if (_selected == null || !_project.Components.Contains(_selected))
            {
                StatusText.Text = "Выберите компонент для копирования.";
                return;
            }
            _clipboardComponent = new PcbComponent
            {
                Reference = _selected.Reference, Type = _selected.Type, Value = _selected.Value,
                X = _selected.X, Y = _selected.Y, Rotation = _selected.Rotation
            };
            StatusText.Text = _selected.Reference + " скопирован. Ctrl+V — вставить.";
        }

        private void CutSelected()
        {
            CopySelected();
            if (_clipboardComponent != null && _selected != null && _project.Components.Contains(_selected))
                DeleteSelected_Click(this, new RoutedEventArgs());
        }

        private void PasteClipboard()
        {
            if (_clipboardComponent == null) { StatusText.Text = "Сначала скопируйте компонент (Ctrl+C)."; return; }
            string prefix = new string(_clipboardComponent.Reference.TakeWhile(ch => !char.IsDigit(ch)).ToArray());
            if (string.IsNullOrEmpty(prefix)) prefix = _clipboardComponent.Type;
            int index = 1;
            while (_project.Components.Any(component => component.Reference == prefix + index)) index++;
            PushUndoSnapshot();
            double step = _gridPresets[_gridPresetIndex];
            var copy = new PcbComponent
            {
                Reference = prefix + index, Type = _clipboardComponent.Type, Value = _clipboardComponent.Value,
                X = Math.Max(0, Math.Min(100, _clipboardComponent.X + step)),
                Y = Math.Max(0, Math.Min(60, _clipboardComponent.Y + step)),
                Rotation = _clipboardComponent.Rotation
            };
            _project.Components.Add(copy);
            _selected = copy;
            RefreshSelection();
            RedrawBoard();
            UpdateStatus();
            StatusText.Text = copy.Reference + " вставлен.";
        }

        private void DuplicateSelected()
        {
            if (_selected == null || !_project.Components.Contains(_selected))
            {
                StatusText.Text = "Выберите компонент для дублирования.";
                return;
            }

            string prefix = new string(_selected.Reference.TakeWhile(ch => !char.IsDigit(ch)).ToArray());
            if (string.IsNullOrEmpty(prefix)) prefix = _selected.Type;
            int index = 1;
            while (_project.Components.Any(component => component.Reference == prefix + index)) index++;
            var copy = new PcbComponent
            {
                Reference = prefix + index,
                Type = _selected.Type,
                Value = _selected.Value,
                X = Math.Min(100, _selected.X + (_gridPresets[_gridPresetIndex])),
                Y = Math.Min(60, _selected.Y + (_gridPresets[_gridPresetIndex])),
                Rotation = _selected.Rotation
            };
            PushUndoSnapshot();
            _project.Components.Add(copy);
            _selected = copy;
            RefreshSelection();
            RedrawBoard();
            UpdateStatus();
            StatusText.Text = copy.Reference + " создан (Ctrl+D).";
        }

        private void LibrarySearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (LibraryList == null) return;
            string query = LibrarySearchBox.Text.Trim();
            foreach (var item in LibraryList.Items.OfType<ListBoxItem>())
            {
                string searchable = (Convert.ToString(item.Content) + " " + Convert.ToString(item.Tag)).ToLowerInvariant();
                item.Visibility = searchable.Contains(query.ToLowerInvariant()) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void LibrarySearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var first = LibraryList.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.Visibility == Visibility.Visible);
                if (first != null)
                {
                    LibraryList.SelectedItem = first;
                    AddComponent_Click(this, new RoutedEventArgs());
                    first.Focus();
                    BoardCanvas.Focus();
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                LibrarySearchBox.Clear();
                BoardCanvas.Focus();
                e.Handled = true;
            }
        }

        

        private Point SnapPoint(Point mm)
        {
            if (SnapCheck == null || SnapCheck.IsChecked != true) return mm;

            Point best = mm;
            double bestDistance = 0.8;
            foreach (var component in _project.Components)
            {
                if (component.Type == "U")
                {
                    for (int i = 0; i < 4; i++)
                    {
                        ConsiderSnap(mm, RotatePadMm(component, -2.9, -1.2 + i * 0.8), ref best, ref bestDistance);
                        ConsiderSnap(mm, RotatePadMm(component, 2.9, -1.2 + i * 0.8), ref best, ref bestDistance);
                    }
                }
                else if (component.Type == "Q")
                {
                    ConsiderSnap(mm, RotatePadMm(component, -1.2, 0), ref best, ref bestDistance);
                    ConsiderSnap(mm, RotatePadMm(component, 1.2, 0), ref best, ref bestDistance);
                    ConsiderSnap(mm, RotatePadMm(component, 0, 0.8), ref best, ref bestDistance);
                }
                else if (component.Type == "TP")
                {
                    ConsiderSnap(mm, new Point(component.X, component.Y), ref best, ref bestDistance);
                }
                else
                {
                    double halfWidth = component.Type == "J" ? 2.4 : 1.7;
                    ConsiderSnap(mm, RotatePadMm(component, -halfWidth, 0), ref best, ref bestDistance);
                    ConsiderSnap(mm, RotatePadMm(component, halfWidth, 0), ref best, ref bestDistance);
                }
            }

            if (bestDistance < 0.8) return best;
            double step = _gridPresets[_gridPresetIndex];
            return new Point(Math.Round(mm.X / step) * step, Math.Round(mm.Y / step) * step);
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

            PushUndoSnapshot();
            _selected.Rotation = (_selected.Rotation + 90) % 360;
            RedrawBoard();
            StatusText.Text = _selected.Reference + " повёрнут на " + _selected.Rotation + "°.";
        }

        private static void GetComponentHalfSize(PcbComponent component, out double halfWidth, out double halfHeight)
        {
            halfWidth = component.Type == "U" ? 2.9 : component.Type == "J" ? 2.4 : component.Type == "TP" ? 1.2 : 1.7;
            halfHeight = component.Type == "U" ? 2.1 : component.Type == "J" ? 1.6 : component.Type == "TP" ? 1.2 : 1.1;
            if (component.Rotation % 180 != 0)
            {
                double temp = halfWidth; halfWidth = halfHeight; halfHeight = temp;
            }
        }

        private bool CanPlaceComponent(PcbComponent component, double x, double y, PcbComponent ignore, out string error)
        {
            double hw, hh; GetComponentHalfSize(component, out hw, out hh);
            const double clearance = 0.2;
            if (x - hw < clearance || x + hw > 100 - clearance || y - hh < clearance || y + hh > 60 - clearance)
            { error = "Компонент не помещается внутри платы с необходимым отступом."; return false; }
            foreach (var other in _project.Components)
            {
                if (ReferenceEquals(other, ignore)) continue;
                double ow, oh; GetComponentHalfSize(other, out ow, out oh);
                if (Math.Abs(x - other.X) < hw + ow + clearance && Math.Abs(y - other.Y) < hh + oh + clearance)
                { error = "Размещение невозможно: корпус пересекается с " + other.Reference + "."; return false; }
            }
            foreach (var hole in _project.Holes ?? new List<PcbHole>())
            {
                double r = Math.Max(1.0, hole.Diameter / 2.0) + 0.5;
                if (Math.Abs(x - hole.X) < hw + r && Math.Abs(y - hole.Y) < hh + r)
                { error = "Размещение невозможно: компонент слишком близко к монтажному отверстию."; return false; }
            }
            error = null; return true;
        }

        private bool CanPlaceHole(double x, double y, double diameter, PcbHole ignore, out string error)
        {
            double r = diameter / 2.0; const double clearance = 0.5;
            if (x - r - clearance < 0 || x + r + clearance > 100 || y - r - clearance < 0 || y + r + clearance > 60)
            { error = "Для отверстия не хватает отступа от края платы."; return false; }
            foreach (var hole in _project.Holes ?? new List<PcbHole>())
            {
                if (ReferenceEquals(hole, ignore)) continue;
                if (Distance(new Point(x, y), new Point(hole.X, hole.Y)) < r + hole.Diameter / 2.0 + clearance)
                { error = "Монтажные отверстия расположены слишком близко друг к другу."; return false; }
            }
            foreach (var component in _project.Components)
            {
                double hw, hh; GetComponentHalfSize(component, out hw, out hh);
                if (Math.Abs(x - component.X) < hw + r + clearance && Math.Abs(y - component.Y) < hh + r + clearance)
                { error = "Отверстие слишком близко к компоненту " + component.Reference + "."; return false; }
            }
            error = null; return true;
        }

        private Point SnapComponentCenter(Point mm, PcbComponent component)
        {
            double halfWidth = component.Type == "U" ? 2.9 : component.Type == "J" ? 2.4 : component.Type == "TP" ? 1.2 : 1.7;
            double halfHeight = component.Type == "U" ? 2.1 : component.Type == "J" ? 1.6 : component.Type == "TP" ? 1.2 : 1.1;
            if (component.Rotation % 180 != 0) { double temp = halfWidth; halfWidth = halfHeight; halfHeight = temp; }
            double x = Math.Max(halfWidth, Math.Min(100 - halfWidth, mm.X));
            double y = Math.Max(halfHeight, Math.Min(60 - halfHeight, mm.Y));
            if (SnapCheck != null && SnapCheck.IsChecked == true)
            {
                double gridStep = _gridPresets[_gridPresetIndex];
                x = Math.Round(x / gridStep) * gridStep;
                y = Math.Round(y / gridStep) * gridStep;
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
            string placementError;
            if (!CanPlaceComponent(_selected, x, y, _selected, out placementError))
            {
                MessageBox.Show(placementError, "Недопустимое размещение",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            PushUndoSnapshot();
            _selected.X = x; _selected.Y = y;
            _selected.Reference = reference; _selected.Value = value;
            RefreshSelection();
            RedrawBoard();
            StatusText.Text = "Свойства " + _selected.Reference + " обновлены.";
        }

        private void TrackMode_Click(object sender, RoutedEventArgs e)
        {
            _trackMode = true; _dragging = false; _trackStart = null; _placingComponent = false; _placementPreview = null; _placingHole = false; _holePreviewPoint = null;
            BoardCanvas.Cursor = Cursors.Cross;
            RedrawBoard();
            StatusText.Text = "Режим дорожки: щёлкните начальную, затем конечную точку внутри платы.";
        }

        private void SelectMode_Click(object sender, RoutedEventArgs e)
        {
            _trackMode = false; _trackStart = null; _placingComponent = false; _placementPreview = null; _placingHole = false; _holePreviewPoint = null;
            BoardCanvas.Cursor = Cursors.Arrow;
            Mouse.OverrideCursor = null;
            RedrawBoard();
            StatusText.Text = "Режим выбора: щёлкните и перетащите компонент.";
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (_selected == null) { StatusText.Text = "Сначала выберите компонент."; return; }
            string reference = _selected.Reference;
            PushUndoSnapshot();
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
            if (_project.Components.Count > 0 || _project.Tracks.Count > 0 || (_project.Holes != null && _project.Holes.Count > 0))
            {
                var answer = MessageBox.Show("Создать новый проект? Несохранённые изменения будут потеряны.",
                    "Новый проект", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (answer != MessageBoxResult.Yes) return;
            }
            _project.Name = "Новый проект";
            _project.Components.Clear(); _project.Tracks.Clear();
            if (_project.Holes == null) _project.Holes = new List<PcbHole>(); else _project.Holes.Clear();
            _selected = null; _currentFile = null; _trackMode = false; _trackStart = null;
            _placingComponent = false; _placementPreview = null; _placingHole = false; _holePreviewPoint = null; BoardCanvas.Cursor = Cursors.Arrow;
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
                _project.Holes = loaded.Holes ?? new List<PcbHole>();
                _selected = null; _currentFile = dialog.FileName; _trackMode = false; _trackStart = null;
                _placingComponent = false; _placementPreview = null; _placingHole = false; _holePreviewPoint = null; BoardCanvas.Cursor = Cursors.Arrow;
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
                    foreach (var hole in _project.Holes ?? new List<PcbHole>())
                    {
                        writer.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                            "<circle cx=\"{0}\" cy=\"{1}\" r=\"{2}\" fill=\"#111a28\" stroke=\"#ffcf70\" stroke-width=\"2\"/>",
                            hole.X * 10, hole.Y * 10, hole.Diameter * 5));
                    }
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
            double viewportWidth = BoardScrollViewer.ViewportWidth;
            double viewportHeight = BoardScrollViewer.ViewportHeight;
            if (double.IsNaN(viewportWidth) || viewportWidth < 100) viewportWidth = BoardScrollViewer.ActualWidth;
            if (double.IsNaN(viewportHeight) || viewportHeight < 100) viewportHeight = BoardScrollViewer.ActualHeight;
            if (viewportWidth > 100 && viewportHeight > 100)
                _zoom = Math.Max(0.25, Math.Min(1.0, Math.Min(viewportWidth / BoardCanvas.Width, viewportHeight / BoardCanvas.Height) * 0.96));
            else
                _zoom = 1.0;

            BoardCanvas.LayoutTransform = new ScaleTransform(_zoom, _zoom);
            ZoomText.Text = "Масштаб: " + Math.Round(_zoom * 100) + "%";
            BoardScrollViewer.ScrollToHorizontalOffset(0);
            BoardScrollViewer.ScrollToVerticalOffset(0);
            StatusText.Text = "Вся плата вписана в рабочую область.";
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

        private void DuplicateSelected_Click(object sender, RoutedEventArgs e) { DuplicateSelected(); }
        private void Undo_Click(object sender, RoutedEventArgs e) { Undo(); }
        private void Redo_Click(object sender, RoutedEventArgs e) { Redo(); }
        private void Copy_Click(object sender, RoutedEventArgs e) { CopySelected(); }
        private void Cut_Click(object sender, RoutedEventArgs e) { CutSelected(); }
        private void Paste_Click(object sender, RoutedEventArgs e) { PasteClipboard(); }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("PCB Studio — справка по горячим клавишам\n\n" +
                "Размещение и редактирование:\n" +
                "• Выберите компонент слева, наведите курсор на плату и щёлкните ЛКМ один раз.\n" +
                "• R — повернуть на 90°, Shift+R — повернуть на -90°. Esc — отмена действия.\n" +
                "• H — режим монтажного отверстия Ø3,2 мм; щёлкните по плате для установки.\n" +
                "• T — трассировка дорожки (два щелчка), V или S — выбор.\n" +
                "• Delete — удалить; Ctrl+D — дублировать; Ctrl+C/X/V — копировать/вырезать/вставить.\n" +
                "• Ctrl+Z / Ctrl+Y — отмена/повтор поддерживаемых изменений; стрелки — сдвиг компонента.\n\n" +
                "Вид и сетка:\n" +
                "• Колесо — масштаб; Shift+колесо — горизонтальная прокрутка; средняя кнопка — панорама.\n" +
                "• F или Space — уместить плату; + / - — масштаб; G — сетка.\n" +
                "• Shift+G — шаг сетки; Ctrl+G — привязка к сетке; C — поиск компонента.\n\n" +
                "Файлы: Ctrl+N — новый; Ctrl+O — открыть; Ctrl+S — сохранить; Ctrl+E или Ctrl+P — экспорт SVG.\n\n" +
                "Это учебный прототип. DRC и трассировка не заменяют профессиональную проверку PCB.",
                "Справка PCB Studio", MessageBoxButton.OK, MessageBoxImage.Information);
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
        [XmlArrayItem("Hole")]
        public List<PcbHole> Holes { get; set; }
        public PcbProject()
        {
            Name = "Новый проект";
            Components = new List<PcbComponent>();
            Tracks = new List<PcbTrack>();
            Holes = new List<PcbHole>();
        }
    }

    public class PcbHole
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Diameter { get; set; }
        public PcbHole() { Diameter = 3.2; }
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
