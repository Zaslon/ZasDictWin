using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
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
    private bool _isActive;

    public string Title { get; protected set; } = "";
    public Action? RequestClose { get; set; }
    public ICommand CloseCommand => new RelayCommand(() => RequestClose?.Invoke());

    /// <summary>タブとして並べるか。偽なら中央のモーダルとして 1 枚だけ出す。</summary>
    public virtual bool IsDockable => true;

    /// <summary>
    /// 行き先を覚えていないとき、本体のタブ束ではなく独立ウィンドウとして開くか。
    /// 常設の枠を割きたくないツール類だけが真にする。どちらで開いても、
    /// あとからタブを掴んでウィンドウの内と外を行き来させられる。
    /// </summary>
    public virtual bool PrefersFloating => false;

    /// <summary>独立ウィンドウとして出すときの既定の大きさ。</summary>
    public virtual Size FloatSize => new(620, 520);

    /// <summary>
    /// 閉じられない据え置きのタブか（検索と単語詳細）。運ぶことはできるので、
    /// ✕ を出さないことだけがここの意味。
    /// </summary>
    public virtual bool IsPinned => false;

    /// <summary>同じ種類は 1 枚までしか開かない。その同一性の判定と、行き先の記憶のキーに使う。</summary>
    public string Kind => GetType().Name;

    /// <summary>その枠で今表に出ているタブか。<see cref="DockLeaf.Selected"/> だけが書き換える。</summary>
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

    public FontFamily Family
    {
        get => _family;
        set
        {
            if (!Set(ref _family, value)) return;
            _fieldFamily = WithRoomForFallback(value);
            Raise(nameof(FieldFamily));
        }
    }

    private FontFamily _fieldFamily = Fallback;

    /// <summary>
    /// 入力欄（TextBox）に当てる見出し語フォント。表示だけの器（TextBlock）には
    /// <see cref="Family"/> をそのまま使う（行が広がって一覧の表示件数が減るため）。
    /// </summary>
    public FontFamily FieldFamily => _fieldFamily;

    /// <summary>
    /// 行の高さを日本語のフォールバック対象（<see cref="Fallback"/>）分に底上げしたフォントを返す。
    /// WPF の行の箱は主フォントの Baseline / LineSpacing だけで決まり、字が無くて別のフォントへ
    /// 落ちた分（Heksa に無い日本語など）が背高でも広がらない。直接LineHeightは変更できないため、
    /// 元のフォントだけを指す合成フォント（.CompositeFont と同じ仕組み）を組んで数値をそちらに持たせる。
    /// 値は em に対する比なので、文字サイズ倍率を変えても同じ割合で広がる。
    /// </summary>
    private static FontFamily WithRoomForFallback(FontFamily family)
    {
        if (family.Baseline >= Fallback.Baseline && family.LineSpacing >= Fallback.LineSpacing) return family;

        try
        {
            var roomy = new FontFamily
            {
                Baseline = Math.Max(family.Baseline, Fallback.Baseline),
                LineSpacing = Math.Max(family.LineSpacing, Fallback.LineSpacing)
            };
            // 行き先は絶対の綴りで渡す。合成フォントは基準 URI を持てないため、Source の
            // "./〜.ttf#〜" という相対の綴りのままだと元のフォントに届かず、黙って既定のフォントで描かれる。
            var target = family.BaseUri is null ? family.Source : $"{family.BaseUri}{family.Source.TrimStart('.', '/')}";
            roomy.FamilyMaps.Add(new FontFamilyMap { Unicode = "0000-10FFFF", Target = target, Scale = 1.0 });
            return roomy;
        }
        catch (ArgumentException ex)
        {
            // 組めなくても入力はできる（上端が切れるだけ）ので、元のフォントで続ける。
            ErrorLog.Write($"入力欄用フォントの合成 ({family.Source})", ex);
            return family;
        }
    }

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
