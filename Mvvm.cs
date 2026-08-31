using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using ZasDictWin.Services;

namespace ZasDictWin.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

/// <summary>画面内オーバーレイの基底。OS ダイアログは OBS のウィンドウキャプチャに映らないため使わない。</summary>
public abstract class OverlayViewModel : ViewModelBase
{
    private DockSide _side = DockSide.Right;
    private bool _isActive;

    public string Title { get; protected set; } = "";
    public Action? RequestClose { get; set; }
    public ICommand CloseCommand => new RelayCommand(() => RequestClose?.Invoke());

    /// <summary>タブとして並べるか。偽なら中央のモーダルとして 1 枚だけ出す。</summary>
    public virtual bool IsDockable => true;

    /// <summary>閉じることも他所へ運ぶこともできない据え置きのタブか（中央の単語詳細だけ）。</summary>
    public virtual bool IsPinned => false;

    /// <summary>同じ種類は 1 枚までしか開かない。その同一性の判定と、行き先の記憶のキーに使う。</summary>
    public string Kind => GetType().Name;

    /// <summary>今どの辺に付いているか。<see cref="DockGroups"/> だけが書き換える。</summary>
    public DockSide Side
    {
        get => _side;
        set => Set(ref _side, value);
    }

    /// <summary>その辺で今表に出ているタブか。<see cref="DockGroup.Selected"/> だけが書き換える。</summary>
    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }
}

/// <summary>
/// 文字サイズ倍率を DataContext と無関係に参照するための共有状態。
/// Style の Setter やネストした DataTemplate の中など、DataContext が MainViewModel を
/// 辿れない場所からも {x:Static} 経由で FontSize をスケールできるようにする。
/// MainViewModel.ApplySettings() が設定変更のたびに Scale を書き戻す。
/// </summary>
public sealed class FontScaleState : ViewModelBase
{
    public static FontScaleState Instance { get; } = new();

    private double _scale = 1.0;
    public double Scale { get => _scale; set => Set(ref _scale, value); }
}

/// <summary>
/// 見出し語フォント（Heksa）を DataContext と無関係に参照するための共有状態。
/// オーバーレイの DataContext は MainViewModel ではないため、そこからも
/// {x:Static} 経由で同じフォントを引けるようにしている。
/// MainViewModel.ApplySettings() が設定変更のたびに Family を書き戻す。
/// </summary>
public sealed class HeadwordFontState : ViewModelBase
{
    public static HeadwordFontState Instance { get; } = new();

    /// <summary>Heksa 無効時と読み込み失敗時のフォント。MainWindow の既定と揃えてある。</summary>
    public static FontFamily Fallback { get; } = new("Yu Gothic UI");

    private FontFamily _family = Fallback;
    public FontFamily Family { get => _family; set => Set(ref _family, value); }

    /// <summary>
    /// ttf / otf を FontFamily として読み込む。Fonts.GetFontFamilies はファイルパスではなく
    /// 「ディレクトリの URI ＋ ファイル名」を要求するため、パスを 2 つに割って渡す。
    /// </summary>
    public static FontFamily? Load(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return null;
            var baseUri = new Uri(dir.EndsWith(Path.DirectorySeparatorChar) ? dir : dir + Path.DirectorySeparatorChar);
            return Fonts.GetFontFamilies(baseUri, Path.GetFileName(path)).FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException or UriFormatException)
        {
            // 呼び出し側は「フォントが見つからない」としか出せないので、原因は記録に残す。
            ErrorLog.Write($"Heksa フォントの読み込み ({path})", ex);
            return null;
        }
    }
}
