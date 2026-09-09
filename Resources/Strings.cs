using System.Resources;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace ZasDictWin.Resources;

/// <summary>
/// UI 文字列。既定（Strings.resx）は日本語、Strings.&lt;コード&gt;.resx が各対応言語を持つ。
/// プロパティ名は resx のキー名とそのまま一致させること。
///
/// 人工言語を想定しているため、言語名の照会に
/// .NET 標準の CultureInfo / サテライトアセンブリ機構は使っていない。
///
/// XAML からは x:Static で参照する（言語切り替えはアプリの再起動で反映するため、
/// 動的な差し替えは持たない）。SetLanguage は App.xaml.cs が起動時に一度だけ呼ぶ。
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Neutral =
        new("ZasDictWin.Resources.Strings", typeof(Strings).Assembly);

    private static ResourceManager _active = Neutral;

    /// <summary>参照先を切り替える。Languages.All にある未知の呼び方をされても
    /// （resx が万一見当たらなくても）既定言語へ静かに落として起動を続けられるようにする。</summary>
    public static void SetLanguage(string code)
    {
        if (code == Languages.Default)
        {
            _active = Neutral;
            return;
        }

        try
        {
            var rm = new ResourceManager($"ZasDictWin.Resources.Strings.{code}", typeof(Strings).Assembly);
            rm.GetString("Common_Cancel"); // 実在確認を兼ねる。無ければここで MissingManifestResourceException
            _active = rm;
        }
        catch (MissingManifestResourceException)
        {
            _active = Neutral;
        }
        finally
        {
            // UIFont は静的初期化子で作れない（SetLanguage を呼ぶこと自体が型初期化の引き金になり、
            // _active を差し替える前に既定言語で設定される）。切り替えのたびに作り直す。
            _uiFont = null;
        }
    }

    // 未訳のキーは _active 側では null になるだけ（例外にはならない）なので、既定言語 → キー名の順に拾う。
    private static string Get([CallerMemberName] string name = "")
        => _active.GetString(name) ?? Neutral.GetString(name) ?? name;

    /// <summary>UI 全体（ウィンドウの既定 FontFamily）に使うフォント名。訳文ではなく表示設定なので、
    /// 各 Strings.*.resx で個別に決められる（既定は "Yu Gothic UI"）。
    /// 空欄の場合、デフォルト値を指定する。</summary>
    public static string Meta_UIFontFamily
    {
        get
        {
            var value = Get();
            return string.IsNullOrWhiteSpace(value) ? "Yu Gothic UI" : value;
        }
    }

    private static FontFamily? _uiFont;

    /// <summary>XAML の FontFamily へはこちら（FontFamily 型）を渡す。x:Static は型コンバーターを
    /// 通さないため、Meta_UIFontFamily（string）を直接 FontFamily プロパティに書くと、値が正しくても
    /// 「'Yu Gothic UI' は、プロパティ 'FontFamily' の有効な値ではありません」で必ず落ちる。</summary>
    public static FontFamily UIFont => _uiFont ??= new FontFamily(Meta_UIFontFamily);

    // 表示言語に関わらず常に Strings.en.resx を指す別系統のリソースマネージャー。SetLanguage で
    // 差し替わる _active とは独立に持つ（English 以外を表示中でも [en] タグ用に参照するため）。
    private static readonly ResourceManager EnManager =
        new("ZasDictWin.Resources.Strings.en", typeof(Strings).Assembly);

    private static FontFamily? _enFont;

    /// <summary>Views/LocalizedText.cs が <c>[en]…[/en]</c> で囲んだ部分に当てるフォント。
    /// 常に Strings.en.resx の Meta_UIFontFamily を参照する（表示中の言語には従わない）。</summary>
    public static FontFamily EnFont
    {
        get
        {
            if (_enFont is not null) return _enFont;
            var value = EnManager.GetString("Meta_UIFontFamily");
            _enFont = new FontFamily(string.IsNullOrWhiteSpace(value) ? "Yu Gothic UI" : value);
            return _enFont;
        }
    }

    public static string Common_Cancel => Get();
    public static string Common_Close => Get();
    public static string Common_Save => Get();
    public static string Common_Delete => Get();
    public static string Common_DeleteAction => Get();
    public static string Common_Edit => Get();
    public static string Common_Duplicate => Get();
    public static string Common_Minimize => Get();
    public static string Common_Maximize => Get();
    public static string Common_Restore => Get();
    public static string Common_Stop => Get();
    public static string Common_KeepEditing => Get();

    public static string Menu_File => Get();
    public static string Menu_Tools => Get();
    public static string Menu_Window => Get();
    public static string Menu_Open => Get();
    public static string Menu_NewDictionary => Get();
    public static string Menu_SaveAs => Get();

    public static string Search_Title => Get();
    public static string Search_Placeholder => Get();
    public static string Search_ModeForward => Get();
    public static string Search_ModePartial => Get();
    public static string Search_ModeBackward => Get();
    public static string Search_ModeExact => Get();
    public static string Search_ScopeForm => Get();
    public static string Label_Translation => Get();
    public static string Search_ScopeBoth => Get();
    public static string Search_ScopeFullText => Get();
    public static string Search_NoResults => Get();
    public static string Search_NoResultsHint => Get();

    public static string Detail_Title => Get();
    public static string Detail_EmptyTitle => Get();
    public static string Detail_EmptyHint => Get();
    public static string Label_Variations => Get();
    public static string Detail_RelatedExamples => Get();
    public static string Label_Headwords => Get();

    public static string Browser_Title => Get();
    public static string Browser_ButtonTooltip => Get();
    public static string Browser_Back => Get();
    public static string Browser_Forward => Get();
    public static string Browser_Reload => Get();
    public static string Browser_AddressPlaceholder => Get();
    public static string Browser_PageError => Get();
    public static string Browser_RuntimeMissing => Get();
    public static string Browser_Loading => Get();
    public static string Browser_LoadFailed => Get();
    public static string Browser_CannotOpen => Get();

    public static string Changelog_Title => Get();
    public static string Changelog_RelinkButton => Get();
    public static string Changelog_ExportButton => Get();
    public static string Changelog_NothingToExportStatus => Get();
    public static string Changelog_ExportDialogTitle => Get();
    public static string Changelog_ExportedStatus => Get();
    public static string Changelog_RelinkDialogTitle => Get();
    public static string Changelog_CsvFilter => Get();
    public static string Changelog_RelinkedStatus => Get();
    public static string Changelog_OldFormDetail => Get();

    public static string Stats_Title => Get();
    public static string Examples_Title => Get();
    public static string Stats_HomonymLabel => Get();
    public static string Stats_AverageLengthLabel => Get();
    public static string Stats_PosHeading => Get();
    public static string Label_Tags => Get();

    public static string Dialect_Title => Get();
    public static string Dialect_InputLabel => Get();
    public static string Dialect_SekoreLabel => Get();
    public static string Dialect_TitauiniLabel => Get();
    public static string Dialect_KaikoLabel => Get();
    public static string Dialect_ArzafireLabel => Get();
    public static string Dialect_FaithfulCheckbox => Get();
    public static string Dialect_Or => Get();

    public static string Ipa_Title => Get();
    public static string Ipa_SpellingLabel => Get();
    public static string Ipa_CandidateHint => Get();
    public static string Legend_Title => Get();

    public static string Examples_AddButton => Get();
    public static string Examples_FilterHint => Get();
    public static string Examples_SearchPlaceholder => Get();
    public static string Examples_Empty => Get();
    public static string Examples_CountLabel => Get();

    public static string ExampleEdit_AddTitle => Get();
    public static string ExampleEdit_EditTitle => Get();
    public static string ExampleEdit_IdPending => Get();
    public static string ExampleEdit_IdFormat => Get();
    public static string ExampleEdit_SentenceLabel => Get();
    public static string ExampleEdit_TranslationLabel => Get();
    public static string ExampleEdit_SupplementLabel => Get();
    public static string Tags_CommaHint => Get();
    public static string ExampleEdit_TagsPlaceholder => Get();
    public static string ExampleEdit_RelatedWordsHeading => Get();
    public static string ExampleEdit_WordSearchPlaceholder => Get();
    public static string ExampleEdit_SourceHeading => Get();
    public static string ExampleEdit_SourcePlaceholder => Get();
    public static string ExampleEdit_FetchButton => Get();
    public static string ExampleEdit_FetchHint => Get();
    public static string Label_ApiKeyPlaceholder => Get();
    public static string ExampleEdit_SaveKeyButton => Get();
    public static string ExampleEdit_ValidationSentence => Get();

    public static string Zpdic_KeyHintSaved => Get();
    public static string Zpdic_KeyHintMissing => Get();
    public static string Zpdic_EnterNumber => Get();
    public static string Zpdic_KeyMissing => Get();
    public static string Zpdic_Fetching => Get();
    public static string Zpdic_FetchFailed => Get();
    public static string Zpdic_KeySaved => Get();
    public static string Zpdic_KeySaveFailed => Get();
    public static string Zpdic_KeyInvalidChars => Get();
    public static string Zpdic_ResponseParseFailed => Get();
    public static string Zpdic_FetchOkWithAuthor => Get();
    public static string Zpdic_FetchOk => Get();
    public static string Zpdic_Timeout => Get();
    public static string Zpdic_Http400 => Get();
    public static string Zpdic_Http401 => Get();
    public static string Zpdic_Http404 => Get();
    public static string Zpdic_Http429 => Get();
    public static string Zpdic_HttpGeneric => Get();

    public static string WordEdit_AddTitle => Get();
    public static string WordEdit_EditTitle => Get();
    public static string WordEdit_FormLabel => Get();
    public static string WordEdit_AddTranslationButton => Get();
    public static string WordEdit_DragTooltip => Get();
    public static string Label_PartOfSpeech => Get();
    public static string WordEdit_PosPlaceholder => Get();
    public static string Label_Content => Get();
    public static string WordEdit_PronHint => Get();
    public static string WordEdit_RelationsHeading => Get();
    public static string WordEdit_AddRelationHeading => Get();
    public static string WordEdit_RelationTitlePlaceholder => Get();
    public static string WordEdit_AddVariationButton => Get();
    public static string WordEdit_VariationHint => Get();
    public static string WordEdit_ValidationPos => Get();
    public static string WordEdit_ReciprocalHint => Get();
    public static string WordEdit_NoReciprocalHint => Get();
    public static string WordEdit_UnsavedTitle => Get();
    public static string WordEdit_UnsavedMessage => Get();
    public static string Relation_DefaultTitle => Get();

    public static string Settings_Title => Get();
    public static string Settings_TabGeneral => Get();
    public static string Settings_TabDictionary => Get();
    public static string Settings_TabStreamWindow => Get();
    public static string Settings_TabBrowser => Get();
    public static string Settings_TabStorage => Get();
    public static string Settings_ApplyButton => Get();
    public static string Settings_FontScaleLabel => Get();
    public static string Settings_SortOrderLabel => Get();
    public static string Settings_AutoSaveCheckbox => Get();
    public static string Settings_WindowHeading => Get();
    public static string Settings_WidthLabel => Get();
    public static string Settings_HeightLabel => Get();
    public static string Settings_AspectLockCheckbox => Get();
    public static string Settings_HeksaHeading => Get();
    public static string Settings_HeksaAnnotation => Get();
    public static string Settings_HeksaEnableCheckbox => Get();
    public static string Settings_HeksaDescription => Get();
    public static string Settings_PickFileButton => Get();
    public static string Settings_HeksaMissingWarning => Get();
    public static string Settings_HeksaSampleLabel => Get();
    public static string Settings_ResetDefaultButton => Get();
    public static string Settings_ReciprocalHeading => Get();
    public static string Settings_ReciprocalNote => Get();
    public static string Settings_NoDictionaryWarning => Get();
    public static string Settings_PunctuationsNote => Get();
    public static string Settings_IgnoredPatternNote => Get();
    public static string Settings_DictDependentNote => Get();
    public static string Settings_BackgroundColorLabel => Get();
    public static string Settings_BackgroundColorNote => Get();
    public static string Settings_TopmostCheckbox => Get();
    public static string Settings_ShowTranslationsCheckbox => Get();
    public static string Settings_ShowContentsCheckbox => Get();
    public static string Settings_StreamWindowNote => Get();
    public static string Settings_BrowserOpenOnStartCheckbox => Get();
    public static string Settings_StartUrlLabel => Get();
    public static string Settings_StartUrlNote => Get();
    public static string Settings_EditModeHeading => Get();
    public static string Settings_ModeLocal => Get();
    public static string Settings_GitHubModeNote => Get();
    public static string Settings_RepoLabel => Get();
    public static string Settings_BranchLabel => Get();
    public static string Settings_JsonPathLabel => Get();
    public static string Settings_JsonPathNote => Get();
    public static string Settings_JsonPathPlaceholder => Get();
    public static string Settings_ChangelogPathLabel => Get();
    public static string Settings_ChangelogPathNote => Get();
    public static string Settings_ChangelogPathPlaceholder => Get();
    public static string Settings_TokenLabel => Get();
    public static string Settings_TokenPlaceholder => Get();
    public static string Settings_TokenStorageNote => Get();
    public static string Settings_PickFontDialogTitle => Get();
    public static string Settings_FontFilter => Get();
    public static string Settings_TokenSaved => Get();
    public static string Settings_TokenSaveFailed => Get();
    public static string Settings_TokenDeleted => Get();
    public static string GitHub_TokenHintSaved => Get();
    public static string GitHub_TokenHintMissing => Get();
    public static string Settings_AppliedStatus => Get();
    public static string Settings_Language => Get();
    public static string Settings_LanguageNote => Get();

    public static string GitHub_LoadButton => Get();
    public static string GitHub_LoadButtonTooltip => Get();
    public static string GitHub_CommitButton => Get();
    public static string GitHub_CommitButtonTooltip => Get();
    public static string GitHub_CommitDialogTitle => Get();
    public static string GitHub_CommitSummaryLabel => Get();
    public static string GitHub_CommitMessageLabel => Get();
    public static string GitHub_ConfigMissingRepo => Get();
    public static string GitHub_ConfigMissingToken => Get();
    public static string GitHub_Checking => Get();
    public static string GitHub_LoadFailedStatus => Get();
    public static string GitHub_NoDiff => Get();
    public static string GitHub_LoadConfirmTitle => Get();
    public static string GitHub_LoadConfirmQuestion => Get();
    public static string GitHub_LoadConfirmTarget => Get();
    public static string GitHub_LoadConfirmYes => Get();
    public static string GitHub_Loading => Get();
    public static string GitHub_ChangelogLoadFailedStatus => Get();
    public static string GitHub_LoadedStatus => Get();
    public static string GitHub_WriteFailedStatus => Get();
    public static string GitHub_NoPendingChanges => Get();
    public static string GitHub_DefaultCommitMessage => Get();
    public static string GitHub_CommitMessageFormat => Get();
    public static string GitHub_AndMoreSuffix => Get();
    public static string GitHub_Committing => Get();
    public static string GitHub_CommitFailedStatus => Get();
    public static string GitHub_Committed => Get();
    public static string GitHub_TokenInvalidChars => Get();
    public static string GitHub_FileNotFound => Get();
    public static string GitHub_FileTooLarge => Get();
    public static string GitHub_DownloadFailed => Get();
    public static string GitHub_FetchOk => Get();
    public static string Network_Timeout => Get();
    public static string GitHub_FetchFailed => Get();
    public static string GitHub_NoChangesToCommit => Get();
    public static string GitHub_BranchFetchFailed => Get();
    public static string GitHub_BranchParseFailed => Get();
    public static string GitHub_CommitInfoFetchFailed => Get();
    public static string GitHub_CommitInfoParseFailed => Get();
    public static string GitHub_TreeCreateFailed => Get();
    public static string GitHub_TreeParseFailed => Get();
    public static string GitHub_CommitCreateFailed => Get();
    public static string GitHub_CommitParseFailed => Get();
    public static string GitHub_RemoteUpdatedConflict => Get();
    public static string GitHub_BranchUpdateFailed => Get();
    public static string GitHub_CommitOk => Get();
    public static string GitHub_Http401 => Get();
    public static string GitHub_Http403 => Get();
    public static string GitHub_HttpGeneric => Get();

    public static string Main_OpenDictionaryPrompt => Get();
    public static string Main_NoDictionaryName => Get();
    public static string Main_CountLabel => Get();
    public static string Main_WordCountLabel => Get();
    public static string Main_FontScaleStatus => Get();
    public static string Main_NewDictionaryCreated => Get();
    public static string Main_OpenDialogTitle => Get();
    public static string Main_OtmFilter => Get();
    public static string Main_LoadFailedStatus => Get();
    public static string Main_LoadFailedTitle => Get();
    public static string Main_LoadedStatus => Get();
    public static string Main_SaveDialogTitle => Get();
    public static string Main_SaveFailedStatus => Get();
    public static string Main_SaveFailedTitle => Get();
    public static string Main_SavedStatus => Get();
    public static string Main_ChangelogAppendFailedStatus => Get();
    public static string Main_NoSavePathStatus => Get();
    public static string Main_NewWordButton => Get();
    public static string Main_ExamplesButtonTooltip => Get();
    public static string Main_UnsavedTooltip => Get();

    public static string Word_AddedStatus => Get();
    public static string Word_UpdatedStatus => Get();
    public static string Word_DuplicatedStatus => Get();
    public static string Word_DeleteConfirmTitle => Get();
    public static string Word_DeleteConfirmMessage => Get();
    public static string Word_DeletedStatus => Get();
    public static string Word_RelationNotFoundStatus => Get();

    public static string Example_AddedStatus => Get();
    public static string Example_UpdatedStatus => Get();
    public static string Example_DeleteConfirmTitle => Get();
    public static string Example_DeleteConfirmMessage => Get();
    public static string Example_DeletedStatus => Get();

    public static string App_UnsavedCloseTitle => Get();
    public static string App_UnsavedCloseMessage => Get();
    public static string App_SaveAndExit => Get();
    public static string App_ExitWithoutSaving => Get();

    public static string Otm_ParseEmpty => Get();
    public static string Otm_NotAnObject => Get();
    public static string Doc_Untitled => Get();

    public static string Window_StreamTitle => Get();
    public static string Window_CountTitle => Get();
    public static string Window_StreamPlaceholder => Get();
    public static string Window_StreamMenuOpen => Get();
    public static string Window_StreamMenuClose => Get();
    public static string Window_StreamMenuTooltip => Get();
    public static string Window_CountMenuOpen => Get();
    public static string Window_CountMenuClose => Get();
    public static string Window_CountMenuTooltip => Get();

    public static string Dock_EmptyHint => Get();
    public static string Dock_CloseAreaButton => Get();
    public static string Overlay_DropToFloatHint => Get();

    public static string Error_Title => Get();
    public static string Error_Message => Get();
}
