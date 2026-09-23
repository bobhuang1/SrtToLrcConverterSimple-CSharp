using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Input;
using Microsoft.Win32;
using SrtToLrcConverter;

namespace SrtToLrcConverterSimple.Ui;

/// <summary>A named encoding choice for the UI dropdown. Null = auto-detect.</summary>
public sealed class EncodingOption
{
    public string Label { get; }
    public Encoding? Encoding { get; }

    public EncodingOption(string label, Encoding? encoding)
    {
        Label = label;
        Encoding = encoding;
    }
}

/// <summary>One converted file shown in the results list.</summary>
public sealed record ConvertRow(string InputFile, string OutputFile, string Status, string Detail, string? PreviewText);

/// <summary>Minimal ICommand implementation.</summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => _execute(parameter);
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SrtToLrcConverter.SrtToLrcConverter _converter = new();

    private bool _isFolderMode = true;
    private string _inputPath = string.Empty;
    private EncodingOption? _selectedEncoding;
    private bool _overrideOutputDir;
    private string _outputDirectory = string.Empty;
    private string _suffixListText = string.Empty;
    private string _statusText = "Ready.";
    private bool _isBusy;
    private ConvertRow? _selectedResult;
    private string _previewText = string.Empty;

    private readonly ObservableCollection<EncodingOption> _encodings = new();
    private readonly ObservableCollection<ConvertRow> _results = new();

    public MainViewModel()
    {
        _encodings.Add(new EncodingOption("Auto-detect (recommended)", null));
        _encodings.Add(new EncodingOption("UTF-8", Encoding.UTF8));
        _encodings.Add(new EncodingOption("UTF-8 with BOM", new UTF8Encoding(true)));
        _encodings.Add(new EncodingOption("UTF-16 LE", Encoding.Unicode));
        _encodings.Add(new EncodingOption("UTF-16 BE", Encoding.BigEndianUnicode));
        _encodings.Add(new EncodingOption("UTF-32", Encoding.UTF32));
        _encodings.Add(new EncodingOption("System default (ANSI)", Encoding.Default));
        _selectedEncoding = _encodings[0];

        var defaults = new LrcConversionOptions().FilenameSuffixesToStrip;
        _suffixListText = string.Join(Environment.NewLine, defaults);

        ConvertCommand = new RelayCommand(async _ => await ConvertAsync(), _ => !IsBusy);
        BrowseInputCommand = new RelayCommand(_ => BrowseInput());
        BrowseOutputCommand = new RelayCommand(_ => ChooseOutputDirectory());
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<EncodingOption> Encodings => _encodings;
    public ObservableCollection<ConvertRow> Results => _results;

    public ICommand ConvertCommand { get; }
    public ICommand BrowseInputCommand { get; }
    public ICommand BrowseOutputCommand { get; }

    public bool IsFolderMode
    {
        get => _isFolderMode;
        set
        {
            if (_isFolderMode != value)
            {
                _isFolderMode = value;
                OnChanged();
                OnChanged(nameof(IsFileMode));
            }
        }
    }

    public bool IsFileMode
    {
        get => !_isFolderMode;
        set => IsFolderMode = !value;
    }

    public string InputPath
    {
        get => _inputPath;
        set { _inputPath = value ?? string.Empty; OnChanged(); }
    }

    public EncodingOption? SelectedEncoding
    {
        get => _selectedEncoding;
        set { _selectedEncoding = value; OnChanged(); }
    }

    public bool OverrideOutputDir
    {
        get => _overrideOutputDir;
        set { _overrideOutputDir = value; OnChanged(); }
    }

    public string OutputDirectory
    {
        get => _outputDirectory;
        set { _outputDirectory = value ?? string.Empty; OnChanged(); }
    }

    public string SuffixListText
    {
        get => _suffixListText;
        set { _suffixListText = value ?? string.Empty; OnChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value ?? string.Empty; OnChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnChanged();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public ConvertRow? SelectedResult
    {
        get => _selectedResult;
        set
        {
            _selectedResult = value;
            OnChanged();
            UpdatePreview();
        }
    }

    public string PreviewText
    {
        get => _previewText;
        private set { _previewText = value ?? string.Empty; OnChanged(); }
    }

    private void UpdatePreview()
    {
        if (SelectedResult is null)
        {
            PreviewText = string.Empty;
            return;
        }

        var sb = new StringBuilder();
        if (SelectedResult.PreviewText is not null)
        {
            sb.Append(SelectedResult.PreviewText);
        }
        else if (!string.IsNullOrEmpty(SelectedResult.Detail))
        {
            sb.Append(SelectedResult.Detail);
        }

        if (SelectedResult.PreviewText is not null && !string.IsNullOrEmpty(SelectedResult.Detail))
        {
            sb.Append(Environment.NewLine).Append(SelectedResult.Detail);
        }

        PreviewText = sb.ToString();
    }

    private void BrowseInput()
    {
        if (IsFolderMode)
        {
            var dlg = new OpenFolderDialog { Title = "Select a folder containing subtitle files" };
            if (dlg.ShowDialog() == true)
            {
                InputPath = dlg.FolderName;
            }
        }
        else
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select a subtitle file",
                Filter = "Subtitle files (*.srt;*.vtt;*.ass;*.ssa;*.smi;*.sub;*.mpl2;*.pjs;*.ttml;*.dfxp)|*.srt;*.vtt;*.ass;*.ssa;*.smi;*.sub;*.mpl2;*.pjs;*.ttml;*.dfxp|All files (*.*)|*.*",
            };
            if (dlg.ShowDialog() == true)
            {
                InputPath = dlg.FileName;
            }
        }
    }

    private void ChooseOutputDirectory()
    {
        var dlg = new OpenFolderDialog { Title = "Select output folder for the .lrc files" };
        if (dlg.ShowDialog() == true)
        {
            OutputDirectory = dlg.FolderName;
        }
    }

    private List<string> ResolveInputs(LrcConversionOptions options)
    {
        if (IsFolderMode)
        {
            if (!Directory.Exists(InputPath))
            {
                StatusText = $"Input folder does not exist: \"{InputPath}\"";
                return [];
            }

            return options.InputExtensions
                .SelectMany(ext => Directory.EnumerateFiles(InputPath, "*" + ext,
                    new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true }))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!File.Exists(InputPath))
        {
            StatusText = $"Input file does not exist: \"{InputPath}\"";
            return [];
        }

        return [InputPath];
    }

    private async Task ConvertAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var options = new LrcConversionOptions
        {
            Encoding = SelectedEncoding?.Encoding,
            OutputDirectory = OverrideOutputDir ? OutputDirectory : null,
            FilenameSuffixesToStrip = SuffixListText
                .Split('\n')
                .Select(l => l.Trim().TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToArray(),
        };

        var inputs = ResolveInputs(options);
        if (inputs.Count == 0)
        {
            return;
        }

        Results.Clear();
        PreviewText = string.Empty;
        IsBusy = true;
        StatusText = "Converting…";

        var rows = await Task.Run(() =>
        {
            var list = new List<ConvertRow>();
            foreach (var file in inputs)
            {
                var result = _converter.ConvertFile(file, options);

                var detail = result.Lrc is null
                    ? (result.Error ?? string.Empty)
                    : string.Join(Environment.NewLine,
                        result.Lrc.Warnings.Select(w => $"{w.Sequence}: {w.Message}"));

                if (result.Converted)
                {
                    list.Add(new ConvertRow(file, result.OutputPath, "OK", detail, result.Lrc!.Text));
                }
                else if (result.Error is not null)
                {
                    list.Add(new ConvertRow(file, result.OutputPath, "Failed", result.Error, null));
                }
                else
                {
                    list.Add(new ConvertRow(file, result.OutputPath, "Skipped", detail, null));
                }
            }

            return list;
        });

        foreach (var row in rows)
        {
            Results.Add(row);
        }

        var ok = rows.Count(r => r.Status == "OK");
        var failed = rows.Count(r => r.Status == "Failed");
        var skipped = rows.Count(r => r.Status == "Skipped");
        StatusText =
            $"Converted {ok} of {rows.Count} file(s)." +
            (failed > 0 ? $" {failed} failed." : string.Empty) +
            (skipped > 0 ? $" {skipped} skipped (no subtitles)." : string.Empty);

        IsBusy = false;
    }

    private void OnChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}