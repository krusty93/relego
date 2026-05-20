using System.Globalization;
using Relego.Cli.Infrastructure;
using Relego.Core.Contracts;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Relego.Cli.Tui;

public sealed class SettingsScreen : IScreen
{
    private const int LabelColumnWidth = 22;
    private const int HorizontalPadding = 2;
    private const int MinWeight = 1;
    private const int MaxWeight = 15;

    private static readonly string[] ScheduleOptions = ["daily", "weekly"];
    private static readonly string[] DayOfWeekOptions = ["monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday"];

    private static readonly (string Key, string Label)[] NormalKeyHints =
    [
        ("↑↓", "Navigate"),
        ("Enter", "Edit"),
        ("T", "Test email settings"),
        ("R", "Refresh"),
        ("Esc", "Go Back"),
        ("Q", "Quit")
    ];

    private readonly RelegoHttpClient _client;
    private readonly bool _isDevelopment;
    private readonly List<SettingsField> _fields = [];
    private SettingsResponse? _settings;
    private int _selectedField;
    private bool _isEditing;
    private bool _isSelectMode;
    private List<string> _selectOptions = [];
    private int _selectIndex;
    private string _editBuffer = string.Empty;
    private string? _statusMessage;
    private bool _statusIsError;

    // Terminal.Gui view references
    private Label? _statusLabel;
    private ListView? _fieldList;
    private System.Collections.ObjectModel.ObservableCollection<string>? _fieldRows;
    private TextField? _editField;
    private Label? _editPromptLabel;
    private View? _editOverlay;
    private Action<ScreenResult>? _navigate;
    private bool _viewCreated;

    private readonly Func<CancellationToken, Task>? _refreshChromeAsync;

    public SettingsScreen(RelegoHttpClient client, bool isDevelopment = false, Func<CancellationToken, Task>? refreshChromeAsync = null)
    {
        _client = client;
        _isDevelopment = isDevelopment;
        _refreshChromeAsync = refreshChromeAsync;
    }

    public string Title => "";

    public int SelectedField => _selectedField;

    public bool IsEditing => _isEditing;

    public string EditBuffer => _editBuffer;

    public string? StatusMessage => _statusMessage;

    public bool StatusIsError => _statusIsError;

    public SettingsResponse? Settings => _settings;

    public IReadOnlyList<SettingsField> Fields => _fields;

    public IReadOnlyList<(string Key, string Label)> KeyHints => NormalKeyHints;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _settings = await _client.GetSettingsAsync(cancellationToken).ConfigureAwait(false);

        var localTimezone = TimeZoneInfo.Local.Id;
        if (!string.Equals(_settings.Timezone, localTimezone, StringComparison.Ordinal))
        {
            var request = new UpdateSettingsRequest { Timezone = localTimezone };
            _settings = await _client.PutSettingsAsync(request, cancellationToken).ConfigureAwait(false);
        }

        RebuildFields();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Views are owned by the parent container hierarchy")]
    public View CreateView(Action<ScreenResult> navigate)
    {
        ArgumentNullException.ThrowIfNull(navigate);
        _navigate = navigate;

        var container = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true
        };

        var headerLabel = new Label
        {
            X = HorizontalPadding,
            Y = 0,
            Width = Dim.Fill(HorizontalPadding * 2),
            Height = 1,
            Text = "Settings",
            CanFocus = false
        };
        headerLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.AccentText, TuiTheme.Palette.Background)));

        _fieldRows = new System.Collections.ObjectModel.ObservableCollection<string>();
        _fieldList = new ShortcutListView
        {
            X = HorizontalPadding,
            Y = 2,
            Width = Dim.Fill(HorizontalPadding * 2),
            Height = Dim.Fill(3),
            CanFocus = true
        };
        _fieldList.SetSource(_fieldRows);
        _fieldList.ValueChanged += (_, _) =>
        {
            if (!_isEditing && _fieldList.SelectedItem is int sel)
                _selectedField = Math.Clamp(sel, 0, Math.Max(0, _fields.Count - 1));
        };
        _fieldList.Accepting += async (_, _) => await HandleFieldEnterAsync().ConfigureAwait(false);
        if (_fieldList is ShortcutListView shortcutList)
        {
            shortcutList.ShortcutKeyPressed += async (_, key) => await HandleShortcutKeyAsync(key).ConfigureAwait(false);
        }
        _fieldList.KeyDown += async (_, key) => await HandleListKeyDownAsync(key).ConfigureAwait(false);

        _editPromptLabel = new Label
        {
            X = HorizontalPadding,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(HorizontalPadding * 2),
            Height = 1,
            Visible = false,
            CanFocus = false
        };
        _editPromptLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(
            TuiTheme.Palette.TextMuted, TuiTheme.Palette.Background)));

        _editField = new TextField
        {
            X = HorizontalPadding,
            Y = Pos.AnchorEnd(2),
            Width = Dim.Fill(HorizontalPadding * 2),
            Height = 1,
            Visible = false,
            CanFocus = true
        };
        var editFieldAttribute = new Terminal.Gui.Drawing.Attribute(TuiTheme.Palette.Text, TuiTheme.Palette.Background);
        _editField.SetScheme(CreateEditFieldScheme(editFieldAttribute));
        _editField.Accepting += async (_, _) => await HandleEditSubmitAsync().ConfigureAwait(false);
        _editField.KeyDown += (_, key) =>
        {
            if (key.KeyCode == KeyCode.Esc)
            {
                CancelEdit();
                key.Handled = true;
                return;
            }

            if (_isSelectMode)
            {
                switch (key.KeyCode)
                {
                    case KeyCode.CursorLeft:
                    case KeyCode.CursorUp:
                        CycleSelect(-1);
                        key.Handled = true;
                        break;
                    case KeyCode.CursorRight:
                    case KeyCode.CursorDown:
                        CycleSelect(1);
                        key.Handled = true;
                        break;
                    default:
                        if (key.KeyCode != KeyCode.Enter)
                            key.Handled = true;
                        break;
                }
            }
        };

        _editOverlay = new View
        {
            X = 0,
            Y = Pos.AnchorEnd(3),
            Width = Dim.Fill(),
            Height = 3,
            Visible = false,
            CanFocus = false
        };

        _statusLabel = new Label
        {
            X = HorizontalPadding,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(HorizontalPadding * 2),
            Height = 1,
            Visible = false,
            CanFocus = false
        };

        container.KeyDown += async (_, key) => await HandleContainerKeyDownAsync(key).ConfigureAwait(false);

        container.Add(headerLabel, _fieldList, _editOverlay, _editPromptLabel, _editField, _statusLabel);

        _viewCreated = true;
        UpdateViewState();

        return container;
    }

    public async Task<ScreenResult> HandleKeyAsync(ConsoleKeyInfo key, CancellationToken cancellationToken)
    {
        if (_isEditing)
        {
            // Edit mode keys are handled by TextField events
            return ScreenResult.Stay();
        }

        ScreenResult result = key.Key switch
        {
            ConsoleKey.UpArrow => MoveSelection(-1),
            ConsoleKey.DownArrow => MoveSelection(1),
            ConsoleKey.Enter => await HandleEnterAsync(cancellationToken).ConfigureAwait(false),
            ConsoleKey.T => await HandleTestEmailAsync(cancellationToken).ConfigureAwait(false),
            ConsoleKey.R => await HandleRefreshAsync(cancellationToken).ConfigureAwait(false),
            ConsoleKey.Q => ScreenResult.ConfirmQuit(),
            ConsoleKey.Escape => ScreenResult.Pop(),
            ConsoleKey.C when key.Modifiers.HasFlag(ConsoleModifiers.Control) => ScreenResult.Quit(),
            _ => ScreenResult.Stay()
        };

        UpdateViewStateIfCreated();
        return result;
    }

    private ScreenResult MoveSelection(int delta)
    {
        if (_fields.Count == 0)
            return ScreenResult.Stay();
        _selectedField = Math.Clamp(_selectedField + delta, 0, _fields.Count - 1);
        ClearStatus();
        return ScreenResult.Stay();
    }

    private async Task<ScreenResult> HandleEnterAsync(CancellationToken cancellationToken)
    {
        if (_fields.Count == 0 || _selectedField < 0 || _selectedField >= _fields.Count)
            return ScreenResult.Stay();

        var field = _fields[_selectedField];

        if (field.Kind == FieldKind.Action)
        {
            return await ExecuteActionAsync(field, cancellationToken).ConfigureAwait(false);
        }

        StartEdit(field);
        return ScreenResult.Stay();
    }

    private void StartEdit(SettingsField field)
    {
        _isEditing = true;
        _editBuffer = field.Value;
        ClearStatus();

        var hasOptions = field.Options is { Count: > 0 };
        _isSelectMode = hasOptions;

        if (hasOptions)
        {
            _selectOptions = [.. field.Options!];
            _selectIndex = Math.Max(0, _selectOptions.IndexOf(field.Value));
        }

        if (_editPromptLabel is not null)
        {
            _editPromptLabel.Text = field.Hint ?? $"Edit {field.Label}:";
            _editPromptLabel.Visible = true;
        }

        if (_editField is not null)
        {
            _editField.Text = hasOptions ? _selectOptions[_selectIndex] : _editBuffer;
            _editField.Visible = true;
            _editField.SetFocus();
            if (!hasOptions)
                _editField.MoveEnd();
        }

        if (_editOverlay is not null)
            _editOverlay.Visible = true;

        UpdateViewStateIfCreated();
    }

    private void CancelEdit()
    {
        // Preserve the selected index: SetFocus() on the ListView may fire ValueChanged,
        // which would overwrite _selectedField with whatever SelectedItem the list reports
        // (potentially 0), so we save and restore the value around the SetFocus call.
        var savedSelectedField = _selectedField;

        _isEditing = false;
        _isSelectMode = false;
        _selectOptions = [];
        _selectIndex = 0;
        _editBuffer = string.Empty;

        if (_editPromptLabel is not null)
            _editPromptLabel.Visible = false;
        if (_editField is not null)
            _editField.Visible = false;
        if (_editOverlay is not null)
            _editOverlay.Visible = false;

        _fieldList?.SetFocus();
        _selectedField = savedSelectedField;
        UpdateViewStateIfCreated();
    }

    private async Task HandleEditSubmitAsync()
    {
        if (!_isEditing || _fields.Count == 0)
            return;

        ClearStatus();

        var field = _fields[_selectedField];
        var newValue = _editField?.Text?.Trim() ?? string.Empty;

        var validationError = ValidateField(field, newValue);
        if (validationError is not null)
        {
            SetStatus(validationError, isError: true);
            return;
        }

        var request = BuildUpdateRequest(field, newValue);

        try
        {
            _settings = await _client.PutSettingsAsync(request).ConfigureAwait(false);
            RebuildFields();
            CancelEdit();
            SetStatus($"{field.Label} updated.", isError: false);

            if (field.FieldId == "kindleEmail" && _refreshChromeAsync is not null)
                _ = Task.Run(() => _refreshChromeAsync(CancellationToken.None));
        }
        catch (HttpRequestException ex)
        {
            SetStatus($"Failed to save: {ex.Message}", isError: true);
        }
    }

    private async Task<ScreenResult> HandleTestEmailAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings?.KindleEmail))
        {
            SetStatus("Kindle email is not configured. Please set it first.", isError: true);
            return ScreenResult.Stay();
        }

        SetStatus("Sending test email...", isError: false);
        UpdateViewStateIfCreated();

        try
        {
            using var response = await _client.PostTestEmailAsync(cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                SetStatus("Test email sent successfully.", isError: false);
            }
            else
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                SetStatus($"Test email failed: {body}", isError: true);
            }
        }
        catch (HttpRequestException ex)
        {
            SetStatus($"Test email failed: {ex.Message}", isError: true);
        }

        return ScreenResult.Stay();
    }

    private async Task<ScreenResult> HandleRefreshAsync(CancellationToken cancellationToken)
    {
        try
        {
            _settings = await _client.GetSettingsAsync(cancellationToken).ConfigureAwait(false);
            RebuildFields();
            SetStatus("Settings refreshed.", isError: false);
        }
        catch (HttpRequestException ex)
        {
            SetStatus($"Refresh failed: {ex.Message}", isError: true);
        }

        return ScreenResult.Stay();
    }

    private async Task<ScreenResult> ExecuteActionAsync(SettingsField field, CancellationToken cancellationToken)
    {
        if (field.ActionId == "test-email")
        {
            return await HandleTestEmailAsync(cancellationToken).ConfigureAwait(false);
        }

        if (field.ActionId == "trigger-recap")
        {
            SetStatus("Triggering recap...", isError: false);
            UpdateViewStateIfCreated();

            try
            {
                using var response = await _client.PostTestRecapAsync(cancellationToken).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    SetStatus("Recap triggered.", isError: false);
                else
                    SetStatus("Recap trigger failed.", isError: true);
            }
            catch (HttpRequestException ex)
            {
                SetStatus($"Recap trigger failed: {ex.Message}", isError: true);
            }

            return ScreenResult.Stay();
        }

        return ScreenResult.Stay();
    }

    public static string? ValidateField(SettingsField field, string value)
    {
        return field.FieldId switch
        {
            "kindleEmail" => !value.Contains('@') ? "Email must contain '@'." : null,
            "schedule" => value is not "daily" and not "weekly" ? "Schedule must be 'daily' or 'weekly'." : null,
            "deliveryDay" => !DayOfWeekOptions.Contains(value, StringComparer.OrdinalIgnoreCase) ? "Invalid day of week." : null,
            "deliveryTime" => !TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
                ? "Time must be in HH:mm format." : null,
            "count" => !int.TryParse(value, out var c) || c < MinWeight || c > MaxWeight
                ? $"Count must be an integer between {MinWeight} and {MaxWeight}." : null,
            _ => null
        };
    }

    private static UpdateSettingsRequest BuildUpdateRequest(SettingsField field, string value)
    {
        return field.FieldId switch
        {
            "kindleEmail" => new UpdateSettingsRequest { KindleEmail = value },
            "schedule" => new UpdateSettingsRequest { Schedule = value },
            "deliveryDay" => new UpdateSettingsRequest { DeliveryDay = value },
            "deliveryTime" => new UpdateSettingsRequest { DeliveryTime = value },
            "count" => new UpdateSettingsRequest { Count = int.Parse(value, CultureInfo.InvariantCulture) },
            _ => new UpdateSettingsRequest()
        };
    }

    private void RebuildFields()
    {
        _fields.Clear();

        if (_settings is null)
            return;

        _fields.Add(new SettingsField("Kindle Email", _settings.KindleEmail, "kindleEmail", FieldKind.Editable,
            Hint: "Insert a valid email address"));
        _fields.Add(new SettingsField("Schedule", _settings.Schedule, "schedule", FieldKind.Editable,
            Hint: "◀ ▶ to change, Enter to confirm", Options: ScheduleOptions));

        if (string.Equals(_settings.Schedule, "weekly", StringComparison.OrdinalIgnoreCase))
        {
            var dayValue = string.IsNullOrWhiteSpace(_settings.DeliveryDay) ? "monday" : _settings.DeliveryDay;
            _fields.Add(new SettingsField("Delivery Day", dayValue, "deliveryDay", FieldKind.Editable,
                Hint: "◀ ▶ to change, Enter to confirm", Options: DayOfWeekOptions));
        }

        _fields.Add(new SettingsField("Delivery Time", _settings.DeliveryTime, "deliveryTime", FieldKind.Editable,
            Hint: "HH:mm format (e.g. 09:30)", DisplaySuffix: $"({_settings.Timezone})"));
        _fields.Add(new SettingsField("Highlights per Recap", _settings.Count.ToString(CultureInfo.InvariantCulture), "count", FieldKind.Editable,
            Hint: $"A number between {MinWeight} and {MaxWeight}"));

        if (_isDevelopment)
        {
            _fields.Add(new SettingsField("▶ Trigger recap (dev)", string.Empty, "trigger-recap", FieldKind.Action));
        }

        if (_selectedField >= _fields.Count)
            _selectedField = Math.Max(0, _fields.Count - 1);
    }

    private void SetStatus(string message, bool isError)
    {
        _statusMessage = message;
        _statusIsError = isError;
        UpdateViewStateIfCreated();
    }

    private void ClearStatus()
    {
        _statusMessage = null;
        _statusIsError = false;
        UpdateViewStateIfCreated();
    }

    private void CycleSelect(int delta)
    {
        if (_selectOptions.Count == 0)
            return;

        _selectIndex = (_selectIndex + delta + _selectOptions.Count) % _selectOptions.Count;
        if (_editField is not null)
            _editField.Text = _selectOptions[_selectIndex];
    }

    private void UpdateViewStateIfCreated()
    {
        if (_viewCreated)
            UpdateViewState();
    }

    private void UpdateViewState()
    {
        // Snapshot selection before modifying _fieldRows: collection-change notifications
        // (e.g. from Clear()) can fire ValueChanged on the ListView, which would
        // overwrite _selectedField with SelectedItem = 0 before we restore it.
        var selectedField = _selectedField;

        if (_fieldRows is not null)
        {
            _fieldRows.Clear();
            foreach (var field in _fields)
            {
                _fieldRows.Add(FormatField(field));
            }
        }

        if (_fieldList is not null && _fields.Count > 0)
        {
            _fieldList.SelectedItem = Math.Clamp(selectedField, 0, _fields.Count - 1);
        }

        if (_statusLabel is not null)
        {
            if (_statusMessage is not null)
            {
                _statusLabel.Text = _statusMessage;
                _statusLabel.Visible = true;
                var color = _statusIsError ? TuiTheme.Palette.Error : TuiTheme.Palette.Success;
                _statusLabel.SetScheme(new Scheme(new Terminal.Gui.Drawing.Attribute(color, TuiTheme.Palette.Background)));
            }
            else
            {
                _statusLabel.Visible = false;
            }
        }
    }

    private static string FormatField(SettingsField field)
    {
        if (field.Kind == FieldKind.Action)
            return $"  {field.Label}";

        var label = field.Label.PadRight(LabelColumnWidth);
        var displayValue = field.DisplaySuffix is not null ? $"{field.Value} {field.DisplaySuffix}" : field.Value;
        return $"  {label}{displayValue}";
    }

    private static Scheme CreateEditFieldScheme(Terminal.Gui.Drawing.Attribute attribute) => new(attribute)
    {
        Normal = attribute,
        Focus = attribute,
        Active = attribute,
        Code = attribute,
        Editable = attribute,
        Highlight = attribute,
        HotActive = attribute,
        HotFocus = attribute,
        HotNormal = attribute,
        ReadOnly = attribute,
        Disabled = attribute
    };

    private async Task HandleContainerKeyDownAsync(Key key)
    {
        if (_isEditing)
            return;

        if (!TryMapGlobalKey(key, out var mappedKey))
            return;

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleListKeyDownAsync(Key key)
    {
        if (_isEditing)
            return;

        if (!TryMapGlobalKey(key, out var mappedKey))
            return;

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleShortcutKeyAsync(Key key)
    {
        if (_isEditing)
            return;

        if (!TryMapGlobalKey(key, out var mappedKey))
            return;

        key.Handled = true;
        var result = await HandleKeyAsync(mappedKey, CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private async Task HandleFieldEnterAsync()
    {
        var result = await HandleKeyAsync(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false), CancellationToken.None).ConfigureAwait(false);
        ApplyNavigation(result);
    }

    private static bool TryMapGlobalKey(Key key, out ConsoleKeyInfo mapped)
    {
        mapped = default;
        var rune = key.AsRune.Value;

        if (key.KeyCode == KeyCode.Esc)
        {
            mapped = new ConsoleKeyInfo('\x1B', ConsoleKey.Escape, false, false, false);
            return true;
        }

        if (key.KeyCode == (KeyCode.C | KeyCode.CtrlMask))
        {
            mapped = new ConsoleKeyInfo('\x03', ConsoleKey.C, false, false, true);
            return true;
        }

        if (rune is 'q' or 'Q')
        {
            mapped = new ConsoleKeyInfo('q', ConsoleKey.Q, false, false, false);
            return true;
        }

        if (rune is 't' or 'T')
        {
            mapped = new ConsoleKeyInfo('t', ConsoleKey.T, false, false, false);
            return true;
        }

        if (rune is 'r' or 'R')
        {
            mapped = new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false);
            return true;
        }

        return false;
    }

    private void ApplyNavigation(ScreenResult result)
    {
        if (result.Action != ScreenAction.None)
            _navigate?.Invoke(result);
    }

    public sealed record SettingsField(
        string Label,
        string Value,
        string FieldId,
        FieldKind Kind,
        string? Hint = null,
        IReadOnlyList<string>? Options = null,
        string? DisplaySuffix = null)
    {
        public string? ActionId => Kind == FieldKind.Action ? FieldId : null;
    }

    public enum FieldKind
    {
        Editable,
        Action
    }
}
