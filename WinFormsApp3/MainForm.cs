using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace WinFormsApp3;

public enum PcbLayer
{
    TopCopper,
    BottomCopper,
    BoardOutline,
    Silkscreen
}

public enum PcbTool
{
    Select,
    Track,
    Pad,
    Via,
    Component,
    BoardOutline
}

public abstract class PcbObject
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Object";
    public PcbLayer Layer { get; set; } = PcbLayer.TopCopper;
    public bool Locked { get; set; }
    public bool Selected { get; set; }

    [JsonIgnore]
    public abstract RectangleF Bounds { get; }
}

public sealed class PcbPad : PcbObject
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 1.8f;
    public float Height { get; set; } = 1.8f;
    public float HoleDiameter { get; set; } = 0;
    public bool Round { get; set; }
    public string Net { get; set; } = "";

    [JsonIgnore]
    public override RectangleF Bounds => new(X - Width / 2, Y - Height / 2, Width, Height);
}

public sealed class PcbVia : PcbObject
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Diameter { get; set; } = 1.2f;
    public float HoleDiameter { get; set; } = 0.6f;
    public string Net { get; set; } = "";

    [JsonIgnore]
    public override RectangleF Bounds => new(X - Diameter / 2, Y - Diameter / 2, Diameter, Diameter);
}

public sealed class PcbTrack : PcbObject
{
    public List<PointF> Points { get; set; } = new();
    public float Width { get; set; } = 0.3f;
    public string Net { get; set; } = "";

    [JsonIgnore]
    public override RectangleF Bounds
    {
        get
        {
            if (Points.Count == 0) return RectangleF.Empty;
            float minX = Points.Min(p => p.X);
            float minY = Points.Min(p => p.Y);
            float maxX = Points.Max(p => p.X);
            float maxY = Points.Max(p => p.Y);
            return new RectangleF(minX - Width, minY - Width, maxX - minX + Width * 2, maxY - minY + Width * 2);
        }
    }
}

public sealed class PcbComponent : PcbObject
{
    public string Reference { get; set; } = "U1";
    public string Value { get; set; } = "Component";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 10;
    public float Height { get; set; } = 6;
    public float Rotation { get; set; }
    public List<PcbPad> Pads { get; set; } = new();

    [JsonIgnore]
    public override RectangleF Bounds => new(X - Width / 2, Y - Height / 2, Width, Height);
}

public sealed class PcbBoard : PcbObject
{
    public float Width { get; set; } = 100;
    public float Height { get; set; } = 80;

    [JsonIgnore]
    public override RectangleF Bounds => new(0, 0, Width, Height);
}

public sealed class PcbProject
{
    public string Name { get; set; } = "New PCB";
    public float Grid { get; set; } = 1;
    public PcbBoard Board { get; set; } = new() { Name = "Board outline", Layer = PcbLayer.BoardOutline };
    public List<PcbObject> Objects { get; set; } = new();
}

public sealed class MainForm : Form
{
    private PcbCanvas canvas = null!;
    private ComboBox layerBox = null!;
    private ComboBox toolBox = null!;
    private NumericUpDown gridBox = null!;
    private Label statusLabel = null!;
    private Label selectionLabel = null!;
    private ListBox libraryList = null!;
    private readonly UndoManager history = new();
    private PcbProject project = new();

    private static readonly Dictionary<string, string> ToolNames = new()
    {
        [nameof(PcbTool.Select)] = "Выбор",
        [nameof(PcbTool.Track)] = "Дорожка",
        [nameof(PcbTool.Pad)] = "Площадка",
        [nameof(PcbTool.Via)] = "Переходное отверстие",
        [nameof(PcbTool.Component)] = "Компонент",
        [nameof(PcbTool.BoardOutline)] = "Контур платы"
    };

    private static readonly Dictionary<string, string> LayerNames = new()
    {
        [nameof(PcbLayer.TopCopper)] = "Верхняя медь",
        [nameof(PcbLayer.BottomCopper)] = "Нижняя медь",
        [nameof(PcbLayer.BoardOutline)] = "Контур платы",
        [nameof(PcbLayer.Silkscreen)] = "Шёлкография"
    };

    public MainForm()
    {
        Text = "PCB Editor — New PCB";
        Width = 1500;
        Height = 950;
        MinimumSize = new Size(1100, 700);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BackColor = Color.FromArgb(245, 247, 250);

        statusLabel = new Label { Text = "Готово", AutoSize = true };
        selectionLabel = new Label { Text = "Объект не выбран", AutoSize = true, Padding = new Padding(12, 0, 0, 0) };

        var menu = BuildMenu();
        Controls.Add(menu);

        var top = BuildToolbar();
        Controls.Add(top);

        // ===== ОСНОВНАЯ РАЗМЕТКА =====
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.FromArgb(236, 239, 243)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));

        // Левая панель — вкладка компонентов
        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Color.FromArgb(236, 239, 243) };
        left.Controls.Add(BuildComponentsTab());
        root.Controls.Add(left, 0, 0);

        // Центр — рабочая область платы
        canvas = new PcbCanvas(project) { Dock = DockStyle.Fill };
        canvas.StatusChanged += text => statusLabel.Text = text;
        canvas.SelectionChanged += UpdateProperties;
        canvas.ProjectChanged += () => Text = $"PCB Editor — {project.Name}";
        canvas.BeforeChange += CaptureHistory;
        root.Controls.Add(canvas, 1, 0);

        // Правая панель — кнопки инструментов вместо информации о плате
        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), BackColor = Color.FromArgb(236, 239, 243) };
        right.Controls.Add(BuildToolsPanel());
        root.Controls.Add(right, 2, 0);

        Controls.Add(root);

        var status = new StatusStrip();
        status.Items.Add(new ToolStripControlHost(statusLabel));
        status.Items.Add(new ToolStripControlHost(selectionLabel));
        Controls.Add(status);

        KeyDown += MainForm_KeyDown;

        Shown += (_, _) => canvas.Focus();
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("Файл");
        file.DropDownItems.Add("Новый", null, (_, _) => NewProject());
        file.DropDownItems.Add("Открыть...", null, (_, _) => OpenProject());
        file.DropDownItems.Add("Сохранить...", null, (_, _) => SaveProject());
        file.DropDownItems.Add("Экспорт SVG...", null, (_, _) => ExportSvg());
        file.DropDownItems.Add("Экспорт производства (Gerber/Drill/Netlist)...", null, (_, _) => ExportManufacturing());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Выход", null, (_, _) => Close());
        menu.Items.Add(file);

        var edit = new ToolStripMenuItem("Правка");
        var undo = new ToolStripMenuItem("Отменить", null, (_, _) => Undo());
        undo.ShortcutKeys = Keys.Control | Keys.Z;
        var redo = new ToolStripMenuItem("Повторить", null, (_, _) => Redo());
        redo.ShortcutKeys = Keys.Control | Keys.Y;
        edit.DropDownItems.Add(undo);
        edit.DropDownItems.Add(redo);
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Удалить", null, (_, _) => DeleteSelected());
        edit.DropDownItems.Add("Отменить действие", null, (_, _) => canvas.CancelCurrentAction());
        edit.DropDownItems.Add(new ToolStripSeparator());
        edit.DropDownItems.Add("Проверить плату (DRC)", null, (_, _) => RunDrc());
        menu.Items.Add(edit);

        var view = new ToolStripMenuItem("Вид");
        view.DropDownItems.Add("Приблизить", null, (_, _) => canvas.ZoomAtCenter(1.25f));
        view.DropDownItems.Add("Отдалить", null, (_, _) => canvas.ZoomAtCenter(0.8f));
        view.DropDownItems.Add("Показать всю плату", null, (_, _) => canvas.FitBoard());
        menu.Items.Add(view);

        var help = new ToolStripMenuItem("Справка");
        help.DropDownItems.Add("Управление", null, (_, _) =>
            MessageBox.Show(
                "Колесо мыши — масштабирование.\nСредняя кнопка — перемещение вида.\nЛевая кнопка — выбор/добавление.\nПравая кнопка или Esc — отмена текущего действия.\nCtrl+Z / Ctrl+Y — отмена / повтор.\nDelete — удалить выбранное.\n\nДорожка: ЛКМ — добавить точку, ПКМ — завершить.",
                "Управление", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add(help);

        MainMenuStrip = menu;
        return menu;
    }

    private Control BuildToolbar()
    {
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(8, 7, 8, 5),
            WrapContents = false,
            BackColor = Color.White
        };

        toolbar.Controls.Add(MakeLabel("Инструмент"));
        toolBox = new ComboBox { Width = 155, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var item in ToolNames.Values) toolBox.Items.Add(item);
        toolBox.SelectedIndex = 0;
        toolBox.SelectedIndexChanged += (_, _) =>
        {
            var key = ToolNames.First(x => x.Value == toolBox.Text).Key;
            canvas.Tool = Enum.Parse<PcbTool>(key);
            canvas.Focus();
        };
        toolbar.Controls.Add(toolBox);

        toolbar.Controls.Add(MakeLabel("Слой"));
        layerBox = new ComboBox { Width = 145, DropDownStyle = ComboBoxStyle.DropDownList };
        foreach (var item in LayerNames.Values) layerBox.Items.Add(item);
        layerBox.SelectedIndex = 0;
        layerBox.SelectedIndexChanged += (_, _) =>
        {
            var key = LayerNames.First(x => x.Value == layerBox.Text).Key;
            canvas.ActiveLayer = Enum.Parse<PcbLayer>(key);
            canvas.Invalidate();
        };
        toolbar.Controls.Add(layerBox);

        toolbar.Controls.Add(MakeLabel("Сетка, мм"));
        gridBox = new NumericUpDown
        {
            Width = 75,
            Minimum = 0.01m,
            Maximum = 10,
            DecimalPlaces = 2,
            Increment = 0.01m,
            Value = (decimal)project.Grid
        };
        gridBox.ValueChanged += (_, _) =>
        {
            // Значение хранится именно в миллиметрах и используется
            // одновременно для отображения сетки и привязки курсора.
            project.Grid = Math.Max(0.01f, (float)gridBox.Value);
            canvas.Invalidate();
        };
        toolbar.Controls.Add(gridBox);

        toolbar.Controls.Add(MakeButton("↶ Отменить", (_, _) => Undo()));
        toolbar.Controls.Add(MakeButton("↷ Повторить", (_, _) => Redo()));
        toolbar.Controls.Add(MakeButton("⌕ По плате", (_, _) => canvas.FitBoard()));
        toolbar.Controls.Add(MakeButton("✓ Проверить DRC", (_, _) => RunDrc()));

        return toolbar;
    }

    private Control BuildComponentsTab()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };

        var componentsPage = new TabPage("Компоненты");
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };

        var title = new Label
        {
            Text = "Компоненты для печатных плат",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        panel.Controls.Add(title);

        libraryList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false
        };

        foreach (var item in ComponentLibrary.Items)
            libraryList.Items.Add($"{item.DisplayName}  —  {item.DefaultValue}");

        libraryList.DoubleClick += (_, _) => AddLibraryComponent();
        panel.Controls.Add(libraryList);

        var add = new Button
        {
            Text = "Добавить выбранный компонент",
            Dock = DockStyle.Bottom,
            Height = 38
        };
        add.Click += (_, _) => AddLibraryComponent();
        panel.Controls.Add(add);

        var hint = new Label
        {
            Text = "Двойной щелчок или кнопка — выбрать компонент, затем щёлкнуть по плате.",
            Dock = DockStyle.Bottom,
            Height = 48
        };
        panel.Controls.Add(hint);

        componentsPage.Controls.Add(panel);
        tabs.TabPages.Add(componentsPage);

        var categoriesPage = new TabPage("Типы");
        var categoryList = new ListBox { Dock = DockStyle.Fill };
        categoryList.Items.AddRange(new object[]
        {
            "Пассивные: R — резисторы",
            "Пассивные: C — конденсаторы",
            "Полупроводники: D — диоды",
            "Полупроводники: LED — светодиоды",
            "Полупроводники: Q — транзисторы",
            "Микросхемы: U — DIP-корпуса",
            "Подключение: J — разъёмы"
        });
        categoriesPage.Controls.Add(categoryList);
        tabs.TabPages.Add(categoriesPage);

        return tabs;
    }

    private Control BuildToolsPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };

        var title = new Label
        {
            Text = "Инструменты платы",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        panel.Controls.Add(title);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        buttons.Controls.Add(MakeToolButton("Выбор", PcbTool.Select));
        buttons.Controls.Add(MakeToolButton("Дорожка", PcbTool.Track));
        buttons.Controls.Add(MakeToolButton("Площадка", PcbTool.Pad));
        buttons.Controls.Add(MakeToolButton("Переходное отверстие", PcbTool.Via));
        buttons.Controls.Add(MakeToolButton("Компонент", PcbTool.Component));
        buttons.Controls.Add(MakeToolButton("Контур платы", PcbTool.BoardOutline));

        buttons.Controls.Add(MakeButton("Показать всю плату", (_, _) => canvas.FitBoard()));
        buttons.Controls.Add(MakeButton("Отменить", (_, _) => Undo()));
        buttons.Controls.Add(MakeButton("Повторить", (_, _) => Redo()));
        buttons.Controls.Add(MakeButton("Проверить DRC", (_, _) => RunDrc()));
        buttons.Controls.Add(MakeButton("Отменить текущее действие", (_, _) => canvas.CancelCurrentAction()));

        panel.Controls.Add(buttons);
        return panel;
    }

    private Button MakeToolButton(string text, PcbTool tool)
    {
        var button = new Button
        {
            Text = text,
            Width = 220,
            Height = 36,
            Margin = new Padding(3)
        };

        button.Click += (_, _) =>
        {
            canvas.Tool = tool;
            if (tool != PcbTool.Component)
                canvas.CancelCurrentAction();
            canvas.Focus();
            statusLabel.Text = $"Инструмент: {text}";
        };

        return button;
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Padding = new Padding(8, 8, 3, 0)
    };

    private static Button MakeButton(string text, EventHandler handler)
    {
        var b = new Button { Text = text, AutoSize = true, Height = 31, Margin = new Padding(4, 2, 4, 2) };
        b.Click += handler;
        return b;
    }

    private void AddLibraryComponent()
    {
        if (libraryList.SelectedIndex < 0) return;
        var item = ComponentLibrary.Items[libraryList.SelectedIndex];
        canvas.BeginPlaceComponent(item);
        toolBox.SelectedIndex = ToolNames.Values.ToList().IndexOf("Компонент");
        canvas.Focus();
    }

    private void CaptureHistory()
    {
        history.Capture(project);
    }

    private void Undo()
    {
        var previous = history.Undo(project);
        if (previous == null) return;
        project = previous;
        canvas.SetProject(project);
        gridBox.Value = (decimal)Math.Clamp(project.Grid, 0.01f, 10f);
        UpdateProperties();
    }

    private void Redo()
    {
        var next = history.Redo(project);
        if (next == null) return;
        project = next;
        canvas.SetProject(project);
        gridBox.Value = (decimal)Math.Clamp(project.Grid, 0.01f, 10f);
        UpdateProperties();
    }

    private void UpdateProperties()
    {
        var selected = project.Objects.Where(o => o.Selected).ToList();
        selectionLabel.Text = selected.Count == 0
            ? "Объект не выбран"
            : selected.Count == 1
                ? $"Выбран: {selected[0].Name}"
                : $"Выбрано объектов: {selected.Count}";
    }

    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Delete) DeleteSelected();
        if (e.Control && e.KeyCode == Keys.S) SaveProject();
        if (e.Control && e.KeyCode == Keys.O) OpenProject();
        if (e.Control && e.KeyCode == Keys.N) NewProject();
        if (e.Control && e.KeyCode == Keys.Z) Undo();
        if (e.Control && e.KeyCode == Keys.Y) Redo();
        if (e.KeyCode == Keys.Escape) canvas.CancelCurrentAction();
    }

    private void NewProject()
    {
        history.Clear();
        project = new PcbProject();
        canvas.SetProject(project);
        gridBox.Value = 1;
        UpdateProperties();
    }

    private void OpenProject()
    {
        using var dialog = new OpenFileDialog { Filter = "PCB project (*.json)|*.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            var json = File.ReadAllText(dialog.FileName);
            project = JsonSerializer.Deserialize<PcbProject>(json, JsonOptions()) ?? new PcbProject();
            ProjectTools.Normalize(project);
            history.Clear();
            canvas.SetProject(project);
            gridBox.Value = (decimal)Math.Clamp(project.Grid, 0.01f, 10f);
            UpdateProperties();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка открытия"); }
    }

    private void SaveProject()
    {
        using var dialog = new SaveFileDialog { Filter = "PCB project (*.json)|*.json", FileName = "board.json" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(project, JsonOptions())); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка сохранения"); }
    }

    private void ExportSvg()
    {
        using var dialog = new SaveFileDialog { Filter = "SVG image (*.svg)|*.svg", FileName = "board.svg" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, SvgExporter.Export(project)); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка экспорта"); }
    }

    private void ExportManufacturing()
    {
        using var dialog = new FolderBrowserDialog { Description = "Выберите папку для Gerber/Excellon/Netlist" };
        if (dialog.ShowDialog() != DialogResult.OK) return;
        try
        {
            GerberExporter.ExportAll(project, dialog.SelectedPath);
            MessageBox.Show("Файлы производства экспортированы.", "Экспорт");
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ошибка экспорта"); }
    }

    private void DeleteSelected()
    {
        if (!project.Objects.Any(x => x.Selected && !x.Locked)) return;
        history.Capture(project);
        project.Objects.RemoveAll(x => x.Selected && !x.Locked);
        canvas.Invalidate();
        UpdateProperties();
    }

    private void RunDrc()
    {
        var issues = AdvancedDrc.Run(project);
        var errors = issues.Count(x => x.Severity == Severity.Error);
        var warnings = issues.Count(x => x.Severity == Severity.Warning);

        if (issues.Count == 0)
        {
            MessageBox.Show(
                "DRC завершена. Нарушений не найдено.",
                "Проверка платы",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var report = new System.Text.StringBuilder();
        report.AppendLine("Результат проверки DRC");
        report.AppendLine($"Ошибок: {errors}");
        report.AppendLine($"Предупреждений: {warnings}");
        report.AppendLine(new string('─', 55));

        foreach (var issue in issues)
            report.AppendLine(issue.ToString());

        using var dialog = new Form
        {
            Text = $"DRC — ошибок: {errors}, предупреждений: {warnings}",
            Width = 760,
            Height = 520,
            StartPosition = FormStartPosition.CenterParent
        };

        var textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10),
            Text = report.ToString()
        };

        var close = new Button
        {
            Text = "Закрыть",
            Dock = DockStyle.Bottom,
            Height = 38
        };
        close.Click += (_, _) => dialog.Close();

        dialog.Controls.Add(textBox);
        dialog.Controls.Add(close);
        dialog.ShowDialog(this);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        IncludeFields = true,
        Converters = { new PcbObjectConverter() }
    };
}

public sealed class PcbObjectConverter : JsonConverter<PcbObject>
{
    public override PcbObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        var type = root.TryGetProperty("$type", out var typeElement)
            ? typeElement.GetString()
            : null;

        return type switch
        {
            "pad" => JsonSerializer.Deserialize<PcbPad>(root.GetRawText(), options)
                     ?? throw new JsonException("Не удалось прочитать площадку."),
            "via" => JsonSerializer.Deserialize<PcbVia>(root.GetRawText(), options)
                     ?? throw new JsonException("Не удалось прочитать переходное отверстие."),
            "track" => JsonSerializer.Deserialize<PcbTrack>(root.GetRawText(), options)
                       ?? throw new JsonException("Не удалось прочитать дорожку."),
            "component" => JsonSerializer.Deserialize<PcbComponent>(root.GetRawText(), options)
                           ?? throw new JsonException("Не удалось прочитать компонент."),
            "board" => JsonSerializer.Deserialize<PcbBoard>(root.GetRawText(), options)
                       ?? throw new JsonException("Не удалось прочитать контур платы."),
            _ => throw new JsonException($"Неизвестный тип объекта PCB: {type ?? "<пусто>"}")
        };
    }

    public override void Write(Utf8JsonWriter writer, PcbObject value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", value switch
        {
            PcbPad => "pad",
            PcbVia => "via",
            PcbTrack => "track",
            PcbComponent => "component",
            PcbBoard => "board",
            _ => throw new JsonException($"Неподдерживаемый тип объекта PCB: {value.GetType().Name}")
        });

        foreach (var property in value.GetType().GetProperties())
        {
            if (property.GetCustomAttributes(typeof(JsonIgnoreAttribute), inherit: true).Length > 0)
                continue;
            if (!property.CanRead)
                continue;

            writer.WritePropertyName(property.Name);
            var propertyValue = property.GetValue(value);
            JsonSerializer.Serialize(writer, propertyValue, property.PropertyType, options);
        }

        writer.WriteEndObject();
    }
}

public sealed class ComponentDefinition
{
    public string DisplayName { get; init; } = "";
    public string ValuePrefix { get; init; } = "";
    public string DefaultValue { get; init; } = "";
    public int PinCount { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float PinPitch { get; init; } = 2.54f;
    public bool DualRow { get; init; }
}

public static class ComponentLibrary
{
    public static readonly List<ComponentDefinition> Items = new()
    {
        new() { DisplayName = "Резистор", ValuePrefix = "R", DefaultValue = "10 кОм", PinCount = 2, Width = 8, Height = 3, PinPitch = 5 },
        new() { DisplayName = "Конденсатор", ValuePrefix = "C", DefaultValue = "100 нФ", PinCount = 2, Width = 6, Height = 4, PinPitch = 3 },
        new() { DisplayName = "Светодиод", ValuePrefix = "LED", DefaultValue = "LED", PinCount = 2, Width = 6, Height = 4, PinPitch = 2.54f },
        new() { DisplayName = "Диод", ValuePrefix = "D", DefaultValue = "1N4148", PinCount = 2, Width = 7, Height = 3, PinPitch = 5 },
        new() { DisplayName = "Транзистор", ValuePrefix = "Q", DefaultValue = "BC547", PinCount = 3, Width = 6, Height = 6, PinPitch = 2.54f },
        new() { DisplayName = "Микросхема DIP-8", ValuePrefix = "U", DefaultValue = "DIP-8", PinCount = 8, Width = 10, Height = 10, PinPitch = 2.54f, DualRow = true },
        new() { DisplayName = "Операционный усилитель DIP-8", ValuePrefix = "U", DefaultValue = "LM358", PinCount = 8, Width = 10, Height = 10, PinPitch = 2.54f, DualRow = true },
        new() { DisplayName = "Кварцевый резонатор", ValuePrefix = "Y", DefaultValue = "16 МГц", PinCount = 2, Width = 6, Height = 3, PinPitch = 2.54f },
        new() { DisplayName = "Предохранитель", ValuePrefix = "F", DefaultValue = "Fuse", PinCount = 2, Width = 8, Height = 3, PinPitch = 5 },
        new() { DisplayName = "Кнопка", ValuePrefix = "SW", DefaultValue = "Button", PinCount = 4, Width = 6, Height = 6, PinPitch = 6 },
        new() { DisplayName = "Разъём 2 контакта", ValuePrefix = "J", DefaultValue = "CONN-2", PinCount = 2, Width = 6, Height = 5, PinPitch = 2.54f },
        new() { DisplayName = "Разъём 3 контакта", ValuePrefix = "J", DefaultValue = "CONN-3", PinCount = 3, Width = 6, Height = 7, PinPitch = 2.54f },
        new() { DisplayName = "Разъём 4 контакта", ValuePrefix = "J", DefaultValue = "CONN-4", PinCount = 4, Width = 6, Height = 10, PinPitch = 2.54f }
    };
}

public sealed class PcbCanvas : Panel
{
    private PcbProject project = null!;
    private Point lastMouse;
    private Point lastClientMouse;
    private readonly List<PointF> currentTrack = new();
    private float zoom = 6;
    private PointF pan = new(80, 80);
    private bool panning;
    private bool draggingSelection;
    private bool dragHistoryCaptured;
    private ComponentDefinition? pendingComponent;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PcbTool Tool { get; set; } = PcbTool.Select;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public PcbLayer ActiveLayer { get; set; } = PcbLayer.TopCopper;
    public event Action<string>? StatusChanged;
    public event Action? ProjectChanged;
    public event Action? SelectionChanged;
    public event Action? BeforeChange;

    public PcbCanvas() : this(new PcbProject()) { }

    public PcbCanvas(PcbProject project)
    {
        this.project = project;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(30, 33, 37);
        Cursor = Cursors.Cross;
        TabStop = true;
        MouseDown += Canvas_MouseDown;
        MouseMove += Canvas_MouseMove;
        MouseUp += Canvas_MouseUp;
        MouseWheel += Canvas_MouseWheel;
        Paint += Canvas_Paint;
    }

    public void SetProject(PcbProject value)
    {
        project = value;
        currentTrack.Clear();
        pendingComponent = null;
        draggingSelection = false;
        dragHistoryCaptured = false;
        Invalidate();
        ProjectChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    public void BeginPlaceComponent(ComponentDefinition definition)
    {
        pendingComponent = definition;
        Tool = PcbTool.Component;
        StatusChanged?.Invoke($"Размещение: {definition.DisplayName}. Щёлкните по плате.");
        Focus();
    }

    public void CancelCurrentAction()
    {
        currentTrack.Clear();
        pendingComponent = null;
        draggingSelection = false;
        dragHistoryCaptured = false;
        Invalidate();
        StatusChanged?.Invoke("Действие отменено");
    }

    public void ZoomAtCenter(float factor)
    {
        var center = new Point(Width / 2, Height / 2);
        ZoomAt(center, factor);
    }

    public void FitBoard()
    {
        if (project.Board.Width <= 0 || project.Board.Height <= 0) return;
        var margin = 40f;
        zoom = Math.Clamp(Math.Min((Width - margin * 2) / project.Board.Width,
                                   (Height - margin * 2) / project.Board.Height), 1.5f, 50);
        pan = new PointF(
            (Width - project.Board.Width * zoom) / 2f,
            (Height - project.Board.Height * zoom) / 2f);
        Invalidate();
    }

    private void ZoomAt(Point location, float factor)
    {
        var oldZoom = zoom;
        var world = ScreenToWorld(location, oldZoom);
        zoom = Math.Clamp(zoom * factor, 1.5f, 50);
        pan = new PointF(location.X - world.X * zoom, location.Y - world.Y * zoom);
        Invalidate();
    }

    private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Shift) != 0)
        {
            pan.X += e.Delta > 0 ? 40 : -40;
            Invalidate();
            return;
        }

        ZoomAt(e.Location, e.Delta > 0 ? 1.15f : 0.87f);
    }

    private void Canvas_MouseDown(object? sender, MouseEventArgs e)
    {
        Focus();
        lastMouse = e.Location;

        if (e.Button == MouseButtons.Middle)
        {
            panning = true;
            Cursor = Cursors.SizeAll;
            return;
        }

        var world = Snap(ScreenToWorld(e.Location));

        if (e.Button == MouseButtons.Right)
        {
            if (Tool == PcbTool.Track && currentTrack.Count >= 2)
            {
                BeforeChange?.Invoke();
                project.Objects.Add(new PcbTrack
                {
                    Name = "Дорожка",
                    Layer = ActiveLayer,
                    Width = 0.3f,
                    Points = new List<PointF>(currentTrack)
                });
                currentTrack.Clear();
                Invalidate();
                ProjectChanged?.Invoke();
            }
            else
            {
                CancelCurrentAction();
            }
            return;
        }

        if (Tool == PcbTool.Select && e.Button == MouseButtons.Left)
        {
            var hit = project.Objects.LastOrDefault(o =>
                o.Bounds.Contains(world) && (o.Layer == ActiveLayer || o is PcbComponent));

            if ((ModifierKeys & Keys.Control) == 0)
                foreach (var obj in project.Objects) obj.Selected = false;

            if (hit != null) hit.Selected = true;
            draggingSelection = hit != null;
            dragHistoryCaptured = false;
            SelectionChanged?.Invoke();
            Invalidate();
            return;
        }

        if (e.Button != MouseButtons.Left) return;

        if (Tool == PcbTool.Pad)
        {
            BeforeChange?.Invoke();
            project.Objects.Add(new PcbPad { Name = "Площадка", X = world.X, Y = world.Y, Layer = ActiveLayer });
            Invalidate();
            ProjectChanged?.Invoke();
            return;
        }

        if (Tool == PcbTool.Via)
        {
            BeforeChange?.Invoke();
            project.Objects.Add(new PcbVia { Name = "Переходное отверстие", X = world.X, Y = world.Y, Layer = ActiveLayer });
            Invalidate();
            ProjectChanged?.Invoke();
            return;
        }

        if (Tool == PcbTool.Component)
        {
            PlaceComponent(world);
            return;
        }

        if (Tool == PcbTool.Track)
        {
            currentTrack.Add(world);
            Invalidate();
            return;
        }

        if (Tool == PcbTool.BoardOutline)
        {
            BeforeChange?.Invoke();
            project.Board.Width = Math.Max(10, world.X);
            project.Board.Height = Math.Max(10, world.Y);
            Invalidate();
            ProjectChanged?.Invoke();
        }
    }

    private void PlaceComponent(PointF world)
    {
        var definition = pendingComponent ?? ComponentLibrary.Items[0];
        BeforeChange?.Invoke();

        var reference = ProjectTools.MakeReference(project, definition.ValuePrefix);
        var component = new PcbComponent
        {
            Name = definition.DisplayName,
            Reference = reference,
            Value = definition.DefaultValue,
            X = world.X,
            Y = world.Y,
            Width = definition.Width,
            Height = definition.Height,
            Layer = PcbLayer.Silkscreen
        };

        if (definition.DualRow && definition.PinCount >= 4)
        {
            var half = definition.PinCount / 2;
            for (var i = 0; i < definition.PinCount; i++)
            {
                var side = i < half ? -1 : 1;
                var index = i < half ? i : definition.PinCount - 1 - i;
                var x = world.X + side * (definition.Width / 2 - 1);
                var y = world.Y + (index - (half - 1) / 2f) * definition.PinPitch;
                component.Pads.Add(new PcbPad
                {
                    Name = (i + 1).ToString(),
                    X = x,
                    Y = y,
                    Width = 1.8f,
                    Height = 1.8f,
                    Layer = ActiveLayer
                });
            }
        }
        else
        {
            var start = (definition.PinCount - 1) / 2f;
            for (var i = 0; i < definition.PinCount; i++)
            {
                var x = world.X + (i - start) * definition.PinPitch;
                component.Pads.Add(new PcbPad
                {
                    Name = (i + 1).ToString(),
                    X = x,
                    Y = world.Y,
                    Width = 1.8f,
                    Height = 1.8f,
                    Layer = ActiveLayer
                });
            }
        }

        project.Objects.Add(component);
        pendingComponent = null;
        Invalidate();
        ProjectChanged?.Invoke();
        SelectionChanged?.Invoke();
    }

    private void Canvas_MouseMove(object? sender, MouseEventArgs e)
    {
        lastClientMouse = e.Location;
        var world = ScreenToWorld(e.Location);
        StatusChanged?.Invoke($"X: {world.X:0.##} мм   Y: {world.Y:0.##} мм   Масштаб: {zoom:0.##} px/мм");

        if (panning)
        {
            pan.X += e.X - lastMouse.X;
            pan.Y += e.Y - lastMouse.Y;
            lastMouse = e.Location;
            Invalidate();
            return;
        }

        if (Tool == PcbTool.Select && e.Button == MouseButtons.Left && draggingSelection)
        {
            var dx = (e.X - lastMouse.X) / zoom;
            var dy = (e.Y - lastMouse.Y) / zoom;
            if (Math.Abs(dx) > 0.0001f || Math.Abs(dy) > 0.0001f)
            {
                var selected = project.Objects.Where(o => o.Selected && !o.Locked).ToList();
                if (selected.Count > 0)
                {
                    if (!dragHistoryCaptured)
                    {
                        BeforeChange?.Invoke();
                        dragHistoryCaptured = true;
                    }
                    foreach (var obj in selected) MoveObject(obj, dx, dy);
                }
            }
            lastMouse = e.Location;
            Invalidate();
        }
        else if (Tool == PcbTool.Track && currentTrack.Count > 0)
        {
            Invalidate();
        }
    }

    private void Canvas_MouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Middle)
        {
            panning = false;
            Cursor = Cursors.Cross;
            return;
        }

        if (e.Button == MouseButtons.Left && draggingSelection)
        {
            draggingSelection = false;
            dragHistoryCaptured = false;
        }
    }

    private static void MoveObject(PcbObject obj, float dx, float dy)
    {
        switch (obj)
        {
            case PcbPad p: p.X += dx; p.Y += dy; break;
            case PcbVia v: v.X += dx; v.Y += dy; break;
            case PcbComponent c:
                c.X += dx; c.Y += dy;
                foreach (var pad in c.Pads) { pad.X += dx; pad.Y += dy; }
                break;
            case PcbTrack t:
                for (int i = 0; i < t.Points.Count; i++)
                    t.Points[i] = new PointF(t.Points[i].X + dx, t.Points[i].Y + dy);
                break;
        }
    }

    private void Canvas_Paint(object? sender, PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DrawGrid(e.Graphics);
        DrawBoard(e.Graphics);

        foreach (var obj in project.Objects.Where(o => o.Layer == ActiveLayer || o is PcbComponent))
            DrawObject(e.Graphics, obj);

        if (currentTrack.Count > 0)
        {
            using var pen = new Pen(Color.Yellow, Math.Max(1, 0.3f * zoom))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            var points = currentTrack.Select(WorldToScreen).ToArray();
            if (points.Length > 1) e.Graphics.DrawLines(pen, points);
            if (points.Length > 0) e.Graphics.DrawLine(pen, points[^1], lastClientMouse);
        }

        if (pendingComponent != null)
        {
            var mouse = Snap(ScreenToWorld(lastClientMouse));
            var r = WorldRectToScreen(new RectangleF(
                mouse.X - pendingComponent.Width / 2,
                mouse.Y - pendingComponent.Height / 2,
                pendingComponent.Width,
                pendingComponent.Height));
            using var pen = new Pen(Color.FromArgb(180, Color.White), 1.5f) { DashStyle = DashStyle.Dash };
            e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            using var font = new Font("Segoe UI", Math.Max(8, zoom * 1.2f));
            e.Graphics.DrawString(pendingComponent.DisplayName, font, Brushes.White, r.Location);
        }
    }

    private void DrawGrid(Graphics g)
    {
        // Сетка строится в координатах платы, поэтому шаг всегда означает
        // реальное расстояние в миллиметрах, независимо от масштаба и панорамирования.
        float gridMm = Math.Max(0.01f, project.Grid);
        float stepPx = gridMm * zoom;

        // При слишком плотной сетке линии скрываются, но привязка по-прежнему
        // работает с тем же значением в миллиметрах.
        if (stepPx < 5)
            return;

        using var minor = new Pen(Color.FromArgb(45, 49, 55), 1);
        using var major = new Pen(Color.FromArgb(65, 70, 78), 1);

        const int majorEvery = 5;
        float worldLeft = ScreenToWorld(new Point(0, 0)).X;
        float worldRight = ScreenToWorld(new Point(Width, 0)).X;
        float worldTop = ScreenToWorld(new Point(0, 0)).Y;
        float worldBottom = ScreenToWorld(new Point(0, Height)).Y;

        int firstX = (int)MathF.Floor(worldLeft / gridMm) - 1;
        int lastX = (int)MathF.Ceiling(worldRight / gridMm) + 1;
        int firstY = (int)MathF.Floor(worldTop / gridMm) - 1;
        int lastY = (int)MathF.Ceiling(worldBottom / gridMm) + 1;

        for (int i = firstX; i <= lastX; i++)
        {
            float worldX = i * gridMm;
            float screenX = worldX * zoom + pan.X;
            var pen = i % majorEvery == 0 ? major : minor;
            g.DrawLine(pen, screenX, 0, screenX, Height);
        }

        for (int i = firstY; i <= lastY; i++)
        {
            float worldY = i * gridMm;
            float screenY = worldY * zoom + pan.Y;
            var pen = i % majorEvery == 0 ? major : minor;
            g.DrawLine(pen, 0, screenY, Width, screenY);
        }
    }

    private void DrawBoard(Graphics g)
    {
        var rect = WorldRectToScreen(project.Board.Bounds);
        using var fill = new SolidBrush(Color.FromArgb(35, 90, 55));
        using var outline = new Pen(Color.FromArgb(100, 220, 130), 2);
        g.FillRectangle(fill, rect);
        g.DrawRectangle(outline, rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void DrawObject(Graphics g, PcbObject obj)
    {
        Color color = obj.Layer switch
        {
            PcbLayer.TopCopper => Color.Orange,
            PcbLayer.BottomCopper => Color.DeepSkyBlue,
            PcbLayer.Silkscreen => Color.White,
            PcbLayer.BoardOutline => Color.Lime,
            _ => Color.Gray
        };
        if (obj.Selected) color = Color.Yellow;

        switch (obj)
        {
            case PcbPad pad:
                var r = WorldRectToScreen(pad.Bounds);
                using (var brush = new SolidBrush(color))
                {
                    if (pad.Round) g.FillEllipse(brush, r);
                    else g.FillRectangle(brush, r);
                }
                if (pad.HoleDiameter > 0)
                {
                    using var hole = new SolidBrush(BackColor);
                    g.FillEllipse(hole, WorldRectToScreen(new RectangleF(
                        pad.X - pad.HoleDiameter / 2, pad.Y - pad.HoleDiameter / 2,
                        pad.HoleDiameter, pad.HoleDiameter)));
                }
                break;

            case PcbVia via:
                var vr = WorldRectToScreen(via.Bounds);
                using (var brush = new SolidBrush(color)) g.FillEllipse(brush, vr);
                using (var hole = new SolidBrush(BackColor))
                    g.FillEllipse(hole, WorldRectToScreen(new RectangleF(
                        via.X - via.HoleDiameter / 2, via.Y - via.HoleDiameter / 2,
                        via.HoleDiameter, via.HoleDiameter)));
                break;

            case PcbTrack track:
                if (track.Points.Count > 1)
                {
                    using var pen = new Pen(color, Math.Max(1, track.Width * zoom))
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round
                    };
                    g.DrawLines(pen, track.Points.Select(WorldToScreen).ToArray());
                }
                break;

            case PcbComponent component:
                var cr = WorldRectToScreen(component.Bounds);
                using (var pen = new Pen(color, 1.5f))
                    g.DrawRectangle(pen, cr.X, cr.Y, cr.Width, cr.Height);

                using (var font = new Font("Segoe UI", Math.Max(7, zoom * 1.5f)))
                using (var valueFont = new Font("Segoe UI", Math.Max(6, zoom)))
                {
                    g.DrawString(component.Reference, font, Brushes.White, cr.Location);
                    g.DrawString(component.Value, valueFont, Brushes.Gainsboro, new PointF(cr.X, cr.Bottom + 2));
                }

                foreach (var pad in component.Pads) DrawObject(g, pad);
                break;
        }
    }

    private PointF Snap(PointF point)
    {
        float grid = Math.Max(0.05f, project.Grid);
        return new PointF(MathF.Round(point.X / grid) * grid, MathF.Round(point.Y / grid) * grid);
    }

    private PointF ScreenToWorld(Point p) => ScreenToWorld(p, zoom);
    private PointF ScreenToWorld(Point p, float currentZoom) =>
        new((p.X - pan.X) / currentZoom, (p.Y - pan.Y) / currentZoom);
    private PointF WorldToScreen(PointF p) => new(p.X * zoom + pan.X, p.Y * zoom + pan.Y);
    private RectangleF WorldRectToScreen(RectangleF r) =>
        new(r.X * zoom + pan.X, r.Y * zoom + pan.Y, r.Width * zoom, r.Height * zoom);
}

public static class DrcChecker
{
    public static List<string> Check(PcbProject project)
    {
        var errors = new List<string>();
        foreach (var obj in project.Objects)
        {
            if (obj is PcbPad pad && pad.HoleDiameter >= Math.Min(pad.Width, pad.Height))
                errors.Add($"{pad.Name}: отверстие слишком большое для площадки.");
            if (obj is PcbVia via && via.HoleDiameter >= via.Diameter)
                errors.Add($"{via.Name}: отверстие больше диаметра переходного отверстия.");
            if (obj.Bounds.Left < 0 || obj.Bounds.Top < 0 || obj.Bounds.Right > project.Board.Width || obj.Bounds.Bottom > project.Board.Height)
                errors.Add($"{obj.Name}: объект выходит за границы платы.");
        }

        var tracks = project.Objects.OfType<PcbTrack>().ToList();
        for (int i = 0; i < tracks.Count; i++)
            for (int j = i + 1; j < tracks.Count; j++)
            {
                if (tracks[i].Layer != tracks[j].Layer) continue;
                if (tracks[i].Bounds.IntersectsWith(tracks[j].Bounds) && tracks[i].Net != tracks[j].Net)
                    errors.Add($"Возможное короткое замыкание между {tracks[i].Name} и {tracks[j].Name}.");
            }
        return errors.Distinct().ToList();
    }
}

public static class SvgExporter
{
    public static string Export(PcbProject project)
    {
        var svg = new System.Text.StringBuilder();
        svg.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{project.Board.Width}mm\" height=\"{project.Board.Height}mm\" viewBox=\"0 0 {project.Board.Width} {project.Board.Height}\">");
        svg.AppendLine("<rect width=\"100%\" height=\"100%\" fill=\"#164b2d\"/>");
        foreach (var obj in project.Objects)
        {
            switch (obj)
            {
                case PcbPad p:
                    svg.AppendLine($"<circle cx=\"{p.X}\" cy=\"{p.Y}\" r=\"{p.Width / 2}\" fill=\"#ff9900\"/>");
                    break;
                case PcbVia v:
                    svg.AppendLine($"<circle cx=\"{v.X}\" cy=\"{v.Y}\" r=\"{v.Diameter / 2}\" fill=\"#00bfff\"/>");
                    svg.AppendLine($"<circle cx=\"{v.X}\" cy=\"{v.Y}\" r=\"{v.HoleDiameter / 2}\" fill=\"#164b2d\"/>");
                    break;
                case PcbTrack t when t.Points.Count > 1:
                    var points = string.Join(" ", t.Points.Select(p => $"{p.X},{p.Y}"));
                    svg.AppendLine($"<polyline points=\"{points}\" fill=\"none\" stroke=\"#ff9900\" stroke-width=\"{t.Width}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                    break;
                case PcbComponent c:
                    svg.AppendLine($"<rect x=\"{c.X - c.Width / 2}\" y=\"{c.Y - c.Height / 2}\" width=\"{c.Width}\" height=\"{c.Height}\" fill=\"none\" stroke=\"white\" stroke-width=\"0.3\"/>");
                    svg.AppendLine($"<text x=\"{c.X}\" y=\"{c.Y}\" fill=\"white\" font-size=\"2\">{c.Reference}</text>");
                    break;
            }
        }
        svg.AppendLine("</svg>");
        return svg.ToString();
    }
}

// ============================================================================
// Professional PCB extensions
// ============================================================================

public enum PadShape { Circle, Rectangle, Oval, RoundedRectangle, Octagon }
public enum DrillType { None, Plated, NonPlated }
public enum Severity { Info, Warning, Error }

public sealed class NetDefinition
{
    public string Name { get; set; } = "GND";
    public string ClassName { get; set; } = "Default";
    public float Clearance { get; set; } = 0.2f;
    public float Width { get; set; } = 0.25f;
    public float ViaDiameter { get; set; } = 0.8f;
    public float ViaDrill { get; set; } = 0.4f;
}

public sealed class DesignRules
{
    public float MinimumTrackWidth { get; set; } = 0.15f;
    public float MinimumClearance { get; set; } = 0.15f;
    public float MinimumDrill { get; set; } = 0.2f;
    public float MinimumAnnularRing { get; set; } = 0.15f;
    public float BoardMargin { get; set; } = 0.25f;
    public float MinimumCopperToEdge { get; set; } = 0.25f;
    public bool RequireNetNames { get; set; }
}

public sealed class DrcIssue
{
    public Severity Severity { get; init; }
    public string Code { get; init; } = "DRC";
    public string Message { get; init; } = "";
    public Guid? ObjectId { get; init; }
    public override string ToString() => $"[{Severity}] {Code}: {Message}";
}

public static class PcbGeometry
{
    public static float Distance(PointF a, PointF b)
    {
        var dx = a.X - b.X; var dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static float DistanceToSegment(PointF p, PointF a, PointF b)
    {
        var vx = b.X - a.X; var vy = b.Y - a.Y;
        var wx = p.X - a.X; var wy = p.Y - a.Y;
        var len2 = vx * vx + vy * vy;
        if (len2 <= 0.000001f) return Distance(p, a);
        var t = Math.Clamp((wx * vx + wy * vy) / len2, 0, 1);
        return Distance(p, new PointF(a.X + t * vx, a.Y + t * vy));
    }

    public static float DistanceToPolyline(PointF p, IReadOnlyList<PointF> points)
    {
        if (points.Count == 0) return float.MaxValue;
        if (points.Count == 1) return Distance(p, points[0]);
        var best = float.MaxValue;
        for (var i = 1; i < points.Count; i++)
            best = MathF.Min(best, DistanceToSegment(p, points[i - 1], points[i]));
        return best;
    }

    public static bool IsPointInsideBoard(PointF p, PcbBoard board, float margin = 0)
        => p.X >= margin && p.Y >= margin && p.X <= board.Width - margin && p.Y <= board.Height - margin;

    public static RectangleF Inflate(RectangleF r, float amount)
        => new(r.X - amount, r.Y - amount, r.Width + amount * 2, r.Height + amount * 2);

    public static PointF Rotate(PointF point, PointF origin, float degrees)
    {
        var rad = degrees * MathF.PI / 180f;
        var c = MathF.Cos(rad); var s = MathF.Sin(rad);
        var x = point.X - origin.X; var y = point.Y - origin.Y;
        return new PointF(origin.X + x * c - y * s, origin.Y + x * s + y * c);
    }

    public static IEnumerable<(PointF A, PointF B)> Segments(IEnumerable<PointF> points)
    {
        var list = points.ToList();
        for (var i = 1; i < list.Count; i++) yield return (list[i - 1], list[i]);
    }
}

public static class AdvancedDrc
{
    public static List<DrcIssue> Run(PcbProject project, DesignRules? rules = null)
    {
        rules ??= new DesignRules();
        var result = new List<DrcIssue>();

        if (project.Board.Width <= 0 || project.Board.Height <= 0)
            result.Add(new()
            {
                Severity = Severity.Error,
                Code = "BOARD_SIZE",
                Message = "Размер платы должен быть положительным."
            });

        var copper = GetCopperObjects(project).ToList();

        foreach (var obj in copper)
        {
            switch (obj)
            {
                case PcbPad pad:
                    CheckPad(pad, rules, result);
                    break;

                case PcbVia via:
                    if (via.Diameter <= 0 || via.HoleDiameter <= 0 || via.HoleDiameter >= via.Diameter)
                        result.Add(Issue(
                            Severity.Error,
                            "VIA_SIZE",
                            "Диаметр отверстия переходного отверстия должен быть меньше внешнего диаметра.",
                            via));

                    if (via.HoleDiameter < rules.MinimumDrill)
                        result.Add(Issue(
                            Severity.Error,
                            "VIA_DRILL",
                            $"Диаметр отверстия {via.HoleDiameter:0.###} мм меньше минимального {rules.MinimumDrill:0.###} мм.",
                            via));

                    if (via.Diameter - via.HoleDiameter < rules.MinimumAnnularRing * 2)
                        result.Add(Issue(
                            Severity.Error,
                            "VIA_RING",
                            "Недостаточное кольцо меди переходного отверстия.",
                            via));
                    break;

                case PcbTrack track:
                    if (track.Points.Count < 2)
                        result.Add(Issue(
                            Severity.Error,
                            "TRACK_POINTS",
                            "Дорожка должна иметь минимум две точки.",
                            track));

                    if (track.Width < rules.MinimumTrackWidth)
                        result.Add(Issue(
                            Severity.Error,
                            "TRACK_WIDTH",
                            $"Ширина дорожки {track.Width:0.###} мм меньше минимальной {rules.MinimumTrackWidth:0.###} мм.",
                            track));

                    if (rules.RequireNetNames && string.IsNullOrWhiteSpace(track.Net))
                        result.Add(Issue(
                            Severity.Warning,
                            "TRACK_NET",
                            "Дорожка не имеет имени цепи.",
                            track));
                    break;
            }

            if (!obj.Bounds.IsEmpty &&
                (obj.Bounds.Left < rules.BoardMargin ||
                 obj.Bounds.Top < rules.BoardMargin ||
                 obj.Bounds.Right > project.Board.Width - rules.BoardMargin ||
                 obj.Bounds.Bottom > project.Board.Height - rules.BoardMargin))
            {
                result.Add(Issue(
                    Severity.Error,
                    "EDGE",
                    "Медный объект слишком близко к краю платы или выходит за её границы.",
                    obj));
            }
        }

        // Проверка минимального зазора между медными объектами.
        for (var i = 0; i < copper.Count; i++)
        {
            for (var j = i + 1; j < copper.Count; j++)
            {
                var a = copper[i];
                var b = copper[j];

                if (a.Layer != b.Layer)
                    continue;

                var netA = GetNet(a);
                var netB = GetNet(b);

                if (!string.IsNullOrWhiteSpace(netA) &&
                    !string.IsNullOrWhiteSpace(netB) &&
                    netA.Equals(netB, StringComparison.OrdinalIgnoreCase))
                    continue;

                var distance = CopperDistance(a, b);
                if (distance < rules.MinimumClearance)
                {
                    result.Add(new DrcIssue
                    {
                        Severity = Severity.Error,
                        Code = "CLEARANCE",
                        Message = $"Недостаточный зазор между {a.Name} и {b.Name}: {distance:0.###} мм. Требуется не менее {rules.MinimumClearance:0.###} мм.",
                        ObjectId = a.Id
                    });
                }
            }
        }

        return result
            .GroupBy(x => (x.Code, x.Message, x.ObjectId))
            .Select(x => x.First())
            .ToList();
    }

    private static void CheckPad(PcbPad pad, DesignRules rules, List<DrcIssue> result)
    {
        if (pad.Width <= 0 || pad.Height <= 0)
            result.Add(Issue(
                Severity.Error,
                "PAD_SIZE",
                "Площадка имеет недопустимый размер.",
                pad));

        if (pad.HoleDiameter > 0)
        {
            if (pad.HoleDiameter < rules.MinimumDrill)
                result.Add(Issue(
                    Severity.Error,
                    "DRILL",
                    $"Диаметр отверстия {pad.HoleDiameter:0.###} мм меньше минимального {rules.MinimumDrill:0.###} мм.",
                    pad));

            var ring = (MathF.Min(pad.Width, pad.Height) - pad.HoleDiameter) / 2f;
            if (ring < rules.MinimumAnnularRing)
                result.Add(Issue(
                    Severity.Error,
                    "ANNULAR",
                    $"Кольцо меди вокруг отверстия {ring:0.###} мм меньше минимального {rules.MinimumAnnularRing:0.###} мм.",
                    pad));
        }

        if (rules.RequireNetNames && string.IsNullOrWhiteSpace(pad.Net))
            result.Add(Issue(
                Severity.Warning,
                "UNNAMED_NET",
                "Площадка не имеет имени цепи.",
                pad));
    }

    private static IEnumerable<PcbObject> GetCopperObjects(PcbProject project)
    {
        foreach (var obj in project.Objects)
        {
            if (obj is PcbPad or PcbVia or PcbTrack)
                yield return obj;

            // Контакты компонентов тоже являются медными площадками.
            if (obj is PcbComponent component)
            {
                foreach (var pad in component.Pads)
                    yield return pad;
            }
        }
    }

    private static float CopperDistance(PcbObject a, PcbObject b)
    {
        if (a is PcbTrack ta && b is PcbTrack tb)
            return PolylineDistance(ta.Points, tb.Points) - ta.Width / 2f - tb.Width / 2f;

        if (a is PcbTrack trackA)
            return DistanceObjectToTrack(b, trackA) - GetCopperRadius(b) - trackA.Width / 2f;

        if (b is PcbTrack trackB)
            return DistanceObjectToTrack(a, trackB) - GetCopperRadius(a) - trackB.Width / 2f;

        return CenterDistance(a, b) - GetCopperRadius(a) - GetCopperRadius(b);
    }

    private static float DistanceObjectToTrack(PcbObject obj, PcbTrack track)
    {
        var point = GetCenter(obj);
        return PcbGeometry.DistanceToPolyline(point, track.Points);
    }

    private static float PolylineDistance(IReadOnlyList<PointF> a, IReadOnlyList<PointF> b)
    {
        if (a.Count == 0 || b.Count == 0)
            return float.MaxValue;

        var best = float.MaxValue;

        foreach (var pa in a)
            best = MathF.Min(best, PcbGeometry.DistanceToPolyline(pa, b));

        foreach (var pb in b)
            best = MathF.Min(best, PcbGeometry.DistanceToPolyline(pb, a));

        return best;
    }

    private static float CenterDistance(PcbObject a, PcbObject b) =>
        PcbGeometry.Distance(GetCenter(a), GetCenter(b));

    private static PointF GetCenter(PcbObject obj) => obj switch
    {
        PcbPad p => new PointF(p.X, p.Y),
        PcbVia v => new PointF(v.X, v.Y),
        PcbTrack t when t.Points.Count > 0 => t.Points[0],
        _ => new PointF(obj.Bounds.X + obj.Bounds.Width / 2f, obj.Bounds.Y + obj.Bounds.Height / 2f)
    };

    private static float GetCopperRadius(PcbObject obj) => obj switch
    {
        PcbPad p => MathF.Min(p.Width, p.Height) / 2f,
        PcbVia v => v.Diameter / 2f,
        _ => 0
    };

    private static string GetNet(PcbObject o) => o switch
    {
        PcbPad p => p.Net,
        PcbVia v => v.Net,
        PcbTrack t => t.Net,
        _ => ""
    };

    private static DrcIssue Issue(Severity severity, string code, string message, PcbObject obj) =>
        new()
        {
            Severity = severity,
            Code = code,
            Message = message,
            ObjectId = obj.Id
        };
}

public static class GerberExporter
{
    public static void ExportAll(PcbProject project, string folder)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "board_outline.gko"), ExportLayer(project, PcbLayer.BoardOutline, "GKO"));
        File.WriteAllText(Path.Combine(folder, "top_copper.gtl"), ExportLayer(project, PcbLayer.TopCopper, "GTL"));
        File.WriteAllText(Path.Combine(folder, "bottom_copper.gbl"), ExportLayer(project, PcbLayer.BottomCopper, "GBL"));
        File.WriteAllText(Path.Combine(folder, "silkscreen.gto"), ExportLayer(project, PcbLayer.Silkscreen, "GTO"));
        File.WriteAllText(Path.Combine(folder, "drill.drl"), ExcellonExporter.Export(project));
        File.WriteAllText(Path.Combine(folder, "netlist.txt"), NetlistExporter.Export(project));
    }

    public static string ExportLayer(PcbProject project, PcbLayer layer, string name)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("G04 Generated by Professional PCB Editor*");
        sb.AppendLine("%FSLAX46Y46*%");
        sb.AppendLine("%MOMM*%");

        var widths = project.Objects
            .OfType<PcbTrack>()
            .Where(t => t.Layer == layer && t.Points.Count > 1)
            .Select(t => Math.Max(t.Width, 0.01f))
            .Distinct()
            .OrderBy(w => w)
            .ToList();

        sb.AppendLine("%ADD10C,0.100000*%");
        for (var i = 0; i < widths.Count; i++)
            sb.AppendLine($"%ADD{11 + i}C,{widths[i]:0.######}*%");

        foreach (var obj in project.Objects.Where(o => o.Layer == layer))
        {
            switch (obj)
            {
                case PcbTrack t when t.Points.Count > 1:
                    var w = Math.Max(t.Width, 0.01f);
                    var idx = widths.IndexOf(w);
                    sb.AppendLine($"D{11 + idx}*");
                    var first = t.Points[0];
                    sb.AppendLine($"X{ToGerber(first.X)}Y{ToGerber(first.Y)}D02*");
                    foreach (var p in t.Points.Skip(1))
                        sb.AppendLine($"X{ToGerber(p.X)}Y{ToGerber(p.Y)}D01*");
                    break;
                case PcbPad p:
                    sb.AppendLine("D10*");
                    sb.AppendLine($"X{ToGerber(p.X)}Y{ToGerber(p.Y)}D03*");
                    break;
                case PcbVia v:
                    sb.AppendLine("D10*");
                    sb.AppendLine($"X{ToGerber(v.X)}Y{ToGerber(v.Y)}D03*");
                    break;
                case PcbBoard b:
                    sb.AppendLine("D10*");
                    sb.AppendLine($"X{ToGerber(0)}Y{ToGerber(0)}D02*");
                    sb.AppendLine($"X{ToGerber(b.Width)}Y{ToGerber(0)}D01*");
                    sb.AppendLine($"X{ToGerber(b.Width)}Y{ToGerber(b.Height)}D01*");
                    sb.AppendLine($"X{ToGerber(0)}Y{ToGerber(b.Height)}D01*");
                    sb.AppendLine($"X{ToGerber(0)}Y{ToGerber(0)}D01*");
                    break;
            }
        }
        sb.AppendLine("M02*");
        return sb.ToString();
    }

    private static string ToGerber(float mm) => Math.Round(mm * 1000000).ToString("0000000000");
}

public static class ExcellonExporter
{
    public static string Export(PcbProject project)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("M48");
        sb.AppendLine("; Generated by Professional PCB Editor");
        sb.AppendLine("METRIC");

        var drills = project.Objects.OfType<PcbPad>().Where(p => p.HoleDiameter > 0).Select(p => p.HoleDiameter)
            .Concat(project.Objects.OfType<PcbVia>().Select(v => v.HoleDiameter))
            .Distinct().OrderBy(x => x).ToList();

        for (var i = 0; i < drills.Count; i++)
            sb.AppendLine($"T{i + 1:00}C{drills[i]:0.###}");

        sb.AppendLine("%");
        sb.AppendLine();

        for (var i = 0; i < drills.Count; i++)
        {
            sb.AppendLine($"T{i + 1:00}");
            foreach (var p in project.Objects.OfType<PcbPad>().Where(p => Math.Abs(p.HoleDiameter - drills[i]) < 0.0001f))
                sb.AppendLine($"X{p.X:0.###}Y{p.Y:0.###}");
            foreach (var v in project.Objects.OfType<PcbVia>().Where(v => Math.Abs(v.HoleDiameter - drills[i]) < 0.0001f))
                sb.AppendLine($"X{v.X:0.###}Y{v.Y:0.###}");
        }

        sb.AppendLine("M30");
        return sb.ToString();
    }
}

public static class NetlistExporter
{
    public static string Export(PcbProject project)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# PCB netlist");
        foreach (var component in project.Objects.OfType<PcbComponent>())
        {
            sb.AppendLine($"COMPONENT {component.Reference} {component.Value}");
            foreach (var pad in component.Pads)
                sb.AppendLine($"  PAD {pad.Name} NET={pad.Net} X={pad.X:0.###} Y={pad.Y:0.###}");
        }
        foreach (var net in project.Objects.Select(GetNet).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().OrderBy(x => x))
        {
            sb.AppendLine($"NET {net}");
            foreach (var pad in project.Objects.OfType<PcbPad>().Where(p => p.Net == net))
                sb.AppendLine($"  PAD {pad.Name}");
            foreach (var via in project.Objects.OfType<PcbVia>().Where(v => v.Net == net))
                sb.AppendLine($"  VIA {via.Name}");
            foreach (var track in project.Objects.OfType<PcbTrack>().Where(t => t.Net == net))
                sb.AppendLine($"  TRACK {track.Name}");
        }
        return sb.ToString();
    }

    private static string GetNet(PcbObject o) => o switch { PcbPad p => p.Net, PcbVia v => v.Net, PcbTrack t => t.Net, _ => "" };
}

public static class ProjectTools
{
    public static PcbProject Clone(PcbProject source)
    {
        var opts = new JsonSerializerOptions { WriteIndented = false, IncludeFields = true, Converters = { new PcbObjectConverter() } };
        var json = JsonSerializer.Serialize(source, opts);
        return JsonSerializer.Deserialize<PcbProject>(json, opts) ?? new PcbProject();
    }

    public static void Normalize(PcbProject project)
    {
        project.Grid = Math.Clamp(project.Grid, 0.01f, 10f);
        project.Board.Width = Math.Max(1, project.Board.Width);
        project.Board.Height = Math.Max(1, project.Board.Height);
        foreach (var p in project.Objects.OfType<PcbTrack>())
        {
            p.Width = Math.Max(0.01f, p.Width);
            p.Points = p.Points.Distinct().ToList();
        }
        foreach (var p in project.Objects.OfType<PcbPad>())
        {
            p.Width = Math.Max(0.1f, p.Width);
            p.Height = Math.Max(0.1f, p.Height);
            p.HoleDiameter = Math.Max(0, p.HoleDiameter);
        }
    }

    public static string MakeReference(PcbProject project, string prefix)
    {
        var used = project.Objects.OfType<PcbComponent>().Select(c => c.Reference).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < 100000; i++) if (!used.Contains(prefix + i)) return prefix + i;
        return prefix + Guid.NewGuid().ToString("N")[..4];
    }
}

public sealed class UndoManager
{
    private const int MaxHistory = 50;
    private readonly LinkedList<PcbProject> undo = new();
    private readonly LinkedList<PcbProject> redo = new();

    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public void Capture(PcbProject project)
    {
        undo.AddLast(ProjectTools.Clone(project));
        redo.Clear();
        while (undo.Count > MaxHistory) undo.RemoveFirst();
    }

    public PcbProject? Undo(PcbProject current)
    {
        if (!CanUndo) return null;
        var last = undo.Last!.Value;
        undo.RemoveLast();
        redo.AddLast(ProjectTools.Clone(current));
        return last;
    }

    public PcbProject? Redo(PcbProject current)
    {
        if (!CanRedo) return null;
        var last = redo.Last!.Value;
        redo.RemoveLast();
        undo.AddLast(ProjectTools.Clone(current));
        return last;
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}

public static class ManufacturingReport
{
    public static string Build(PcbProject project, DesignRules? rules = null)
    {
        var issues = AdvancedDrc.Run(project, rules);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("PCB MANUFACTURING REPORT");
        sb.AppendLine($"Board: {project.Name}");
        sb.AppendLine($"Size: {project.Board.Width:0.###} x {project.Board.Height:0.###} mm");
        sb.AppendLine($"Objects: {project.Objects.Count}");
        sb.AppendLine($"Pads: {project.Objects.OfType<PcbPad>().Count()}");
        sb.AppendLine($"Vias: {project.Objects.OfType<PcbVia>().Count()}");
        sb.AppendLine($"Tracks: {project.Objects.OfType<PcbTrack>().Count()}");
        sb.AppendLine($"Components: {project.Objects.OfType<PcbComponent>().Count()}");
        sb.AppendLine($"DRC issues: {issues.Count}");
        foreach (var issue in issues) sb.AppendLine(issue.ToString());
        return sb.ToString();
    }
}