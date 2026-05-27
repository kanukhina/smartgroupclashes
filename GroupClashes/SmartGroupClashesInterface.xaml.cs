using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WIN = System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Markup;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using IOPath = System.IO.Path;

using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api;
using Forms = System.Windows.Forms;

namespace SmartGroupClashes
{
    /// <summary>
    /// WPF-панель настроек группировки: список тестов коллизий, режимы и вызов <see cref="GroupingFunctions"/>.
    /// </summary>
    public partial class SmartGroupClashesInterface : UserControl
    {
        /// <summary>Тесты коллизий текущего документа для списка выбора.</summary>
        public ObservableCollection<CustomClashTest> ClashTests { get; set; }

        /// <summary>Варианты режима для комбобокса «Группировать по».</summary>
        public ObservableCollection<GroupingModeOption> GroupByList { get; set; }

        /// <summary>Варианты режима для комбобокса «Затем по».</summary>
        public ObservableCollection<GroupingModeOption> GroupThenList { get; set; }

        /// <summary>Варианты режима для комбобокса «И затем по».</summary>
        public ObservableCollection<GroupingModeOption> GroupThirdList { get; set; }

        /// <summary>Подсказки для полей «Своё свойство» (отображаемые имена свойств).</summary>
        public ObservableCollection<string> CustomPropertySuggestions { get; } = new ObservableCollection<string>();

        private ICollectionView _clashTestsView;
        private int _lastSelectedIndex = -1;
        private bool _isRangeSelectionInProgress;
        private bool _isApplyingSettings;
        private string _loadedSettingsDocumentKey;

        /// <summary>Последний выбранный тест (для совместимости привязок, если используется).</summary>
        public ClashTest SelectedClashTest { get; set; }

        /// <summary>Создаёт панель, заполняет списки и подписывается на события документа.</summary>
        public SmartGroupClashesInterface()
        {
            InitializeComponent();

            ClashTests = new ObservableCollection<CustomClashTest>();
            GroupByList = new ObservableCollection<GroupingModeOption>();
            GroupThenList = new ObservableCollection<GroupingModeOption>();
            GroupThirdList = new ObservableCollection<GroupingModeOption>();

            foreach (string suggestion in GroupingFunctions.DefaultCustomPropertyDisplayNameSuggestions)
            {
                CustomPropertySuggestions.Add(suggestion);
            }

            RegisterChanges();

            this.DataContext = this;
            _clashTestsView = CollectionViewSource.GetDefaultView(ClashTests);
            _clashTestsView.Filter = FilterClashTests;

            AttachSettingsAutoSaveHandlers();
        }

        /// <summary>Группирует пересечения в выбранных тестах по заданным режимам.</summary>
        private void Group_Button_Click(object sender, WIN.RoutedEventArgs e)
        {
            if (ClashTestListBox.SelectedItems.Count == 0)
            {
                return;
            }

            bool groupingPerformed = false;

            // Временно отписаться от событий документа, чтобы не дергать UI во время тяжёлой операции.
            UnRegisterChanges();
            try
            {
                GroupingMode groupByMode = GetSelectedGroupingMode(comboBoxGroupBy);
                GroupingMode thenByModeSel = GetSelectedGroupingMode(comboBoxThenBy);
                GroupingMode thirdByModeSel = GetSelectedGroupingMode(comboBoxThirdBy);
                bool analyzeNewStatus = analyzeNewStatusCheckBox.IsChecked == true;
                bool analyzeActiveStatus = analyzeActiveStatusCheckBox.IsChecked == true;
                bool analyzeReviewedStatus = analyzeReviewedStatusCheckBox.IsChecked == true;
                bool analyzeApprovedStatus = analyzeApprovedStatusCheckBox.IsChecked == true;
                bool analyzeResolvedStatus = analyzeResolvedStatusCheckBox.IsChecked == true;

                string customStage1A = null;
                string customStage1B = null;
                string customStage2A = null;
                string customStage2B = null;
                string customStage3A = null;
                string customStage3B = null;

                if (groupByMode == GroupingMode.CustomProperty)
                {
                    customStage1A = CustomPropertyStage1ComboA.Text?.Trim();
                    customStage1B = CustomPropertyStage1ComboB.Text?.Trim();
                    if (string.IsNullOrEmpty(customStage1A) || string.IsNullOrEmpty(customStage1B))
                    {
                        Forms.MessageBox.Show(
                            "Для первого уровня («Группировать по» → «Своё свойство») укажите имя параметра для выбора A и для выбора B.",
                            "SmartGroupClashes",
                            Forms.MessageBoxButtons.OK,
                            Forms.MessageBoxIcon.Warning);
                        return;
                    }
                }

                if (thenByModeSel == GroupingMode.CustomProperty)
                {
                    customStage2A = CustomPropertyStage2ComboA.Text?.Trim();
                    customStage2B = CustomPropertyStage2ComboB.Text?.Trim();
                    if (string.IsNullOrEmpty(customStage2A) || string.IsNullOrEmpty(customStage2B))
                    {
                        Forms.MessageBox.Show(
                            "Для второго уровня («Затем по» → «Своё свойство») укажите имя параметра для выбора A и для выбора B.",
                            "SmartGroupClashes",
                            Forms.MessageBoxButtons.OK,
                            Forms.MessageBoxIcon.Warning);
                        return;
                    }
                }

                if (thirdByModeSel == GroupingMode.CustomProperty)
                {
                    customStage3A = CustomPropertyStage3ComboA.Text?.Trim();
                    customStage3B = CustomPropertyStage3ComboB.Text?.Trim();
                    if (string.IsNullOrEmpty(customStage3A) || string.IsNullOrEmpty(customStage3B))
                    {
                        Forms.MessageBox.Show(
                            "Для третьего уровня («И затем по» → «Своё свойство») укажите имя параметра для выбора A и для выбора B.",
                            "SmartGroupClashes",
                            Forms.MessageBoxButtons.OK,
                            Forms.MessageBoxIcon.Warning);
                        return;
                    }
                }

                foreach (object selectedItem in ClashTestListBox.SelectedItems)
                {
                    CustomClashTest selectedClashTest = (CustomClashTest)selectedItem;
                    ClashTest clashTest = selectedClashTest.ClashTest;

                    if (clashTest.Children.Count != 0)
                    {
                        if (groupByMode != GroupingMode.None
                            || thenByModeSel != GroupingMode.None
                            || thirdByModeSel != GroupingMode.None)
                        {
                            bool grouped = GroupingFunctions.GroupClashes(
                                clashTest,
                                groupByMode,
                                thenByModeSel,
                                thirdByModeSel,
                                (bool)keepExistingGroupsCheckBox.IsChecked,
                                (bool)skipFixedGroupsCheckBox.IsChecked,
                                analyzeNewStatus,
                                analyzeActiveStatus,
                                analyzeReviewedStatus,
                                analyzeApprovedStatus,
                                analyzeResolvedStatus,
                                customStage1A,
                                customStage1B,
                                customStage2A,
                                customStage2B,
                                customStage3A,
                                customStage3B);
                            groupingPerformed = groupingPerformed || grouped;
                        }
                    }
                }

                if (groupingPerformed)
                {
                    try
                    {
                        ShowGroupingCompletedMessage();
                    }
                    catch (Exception notifyEx)
                    {
                        Forms.MessageBox.Show(
                            "Группировка выполнена, но завершить интерфейс без ошибок не удалось: " + notifyEx.Message,
                            "SmartGroupClashes",
                            Forms.MessageBoxButtons.OK,
                            Forms.MessageBoxIcon.Warning);
                    }
                }
                else
                {
                    Forms.MessageBox.Show(
                        "Нет пересечений для группировки.",
                        "SmartGroupClashes",
                        Forms.MessageBoxButtons.OK,
                        Forms.MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Forms.MessageBox.Show(
                    "Во время группировки произошла ошибка: " + ex.Message,
                    "SmartGroupClashes",
                    Forms.MessageBoxButtons.OK,
                    Forms.MessageBoxIcon.Error);
            }
            finally
            {
                RegisterChanges();
            }
        }

        /// <summary>Реакция на смену режима в комбобоксе: обновляет видимость полей «Своё свойство».</summary>
        private void GroupingModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateCustomPropertyParamsVisibility();
            CheckPlugin();
            AutoSaveSettingsIfMissingForDocument();
        }

        /// <summary>Настраивает видимость панелей параметров пользовательских свойств (этапы 1–3).</summary>
        private void UpdateCustomPropertyParamsVisibility()
        {
            if (CustomPropertyParamsPanel == null)
            {
                return;
            }

            bool stage1 = GetSelectedGroupingMode(comboBoxGroupBy) == GroupingMode.CustomProperty;
            bool stage2 = GetSelectedGroupingMode(comboBoxThenBy) == GroupingMode.CustomProperty;
            bool stage3 = GetSelectedGroupingMode(comboBoxThirdBy) == GroupingMode.CustomProperty;
            bool need = stage1 || stage2 || stage3;
            CustomPropertyParamsPanel.Visibility = need ? WIN.Visibility.Visible : WIN.Visibility.Collapsed;
            bool enableCustom = need && comboBoxGroupBy.IsEnabled;

            if (CustomPropertyStage1Panel != null)
            {
                CustomPropertyStage1Panel.Visibility = stage1 ? WIN.Visibility.Visible : WIN.Visibility.Collapsed;
            }

            if (CustomPropertyStage2Panel != null)
            {
                CustomPropertyStage2Panel.Visibility = stage2 ? WIN.Visibility.Visible : WIN.Visibility.Collapsed;
            }

            if (CustomPropertyStage3Panel != null)
            {
                CustomPropertyStage3Panel.Visibility = stage3 ? WIN.Visibility.Visible : WIN.Visibility.Collapsed;
            }

            if (CustomPropertyStage1ComboA != null)
            {
                CustomPropertyStage1ComboA.IsEnabled = enableCustom && stage1;
            }

            if (CustomPropertyStage1ComboB != null)
            {
                CustomPropertyStage1ComboB.IsEnabled = enableCustom && stage1;
            }

            if (CustomPropertyStage2ComboA != null)
            {
                CustomPropertyStage2ComboA.IsEnabled = enableCustom && stage2;
            }

            if (CustomPropertyStage2ComboB != null)
            {
                CustomPropertyStage2ComboB.IsEnabled = enableCustom && stage2;
            }

            if (CustomPropertyStage3ComboA != null)
            {
                CustomPropertyStage3ComboA.IsEnabled = enableCustom && stage3;
            }

            if (CustomPropertyStage3ComboB != null)
            {
                CustomPropertyStage3ComboB.IsEnabled = enableCustom && stage3;
            }
        }

        /// <summary>Показывает стандартное сообщение об успешной группировке.</summary>
        private static void ShowGroupingCompletedMessage()
        {
            Forms.MessageBox.Show(
                "Группировка выполнена успешно.",
                "SmartGroupClashes",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Information);
        }

        /// <summary>Снимает группировку в выбранных тестах коллизий.</summary>
        private void Ungroup_Button_Click(object sender, WIN.RoutedEventArgs e)
        {
            if (ClashTestListBox.SelectedItems.Count != 0)
            {
                UnRegisterChanges();

                foreach (object selectedItem in ClashTestListBox.SelectedItems)
                {
                    CustomClashTest selectedClashTest = (CustomClashTest)selectedItem;
                    ClashTest clashTest = selectedClashTest.ClashTest;

                    if (clashTest.Children.Count != 0)
                    {
                        GroupingFunctions.UnGroupClashes(clashTest);
                    }
                }

                RegisterChanges();

            }
        }

        /// <summary>
        /// Подписывается на изменения базы документа и списка тестов коллизий, обновляет списки в UI.
        /// </summary>
        private void RegisterChanges()
        {
            Application.MainDocument.Database.Changed += DocumentClashTests_Changed;

            DocumentClashTests dct = Application.MainDocument.GetClash().TestsData;
            dct.Changed += DocumentClashTests_Changed;

            GetClashTests();
            CheckPlugin();
            LoadComboBox();
            TryLoadSettingsForCurrentDocument();
        }

        /// <summary>Отписывается от событий документа и тестов коллизий.</summary>
        private void UnRegisterChanges()
        {
            Application.MainDocument.Database.Changed -= DocumentClashTests_Changed;

            DocumentClashTests dct = Application.MainDocument.GetClash().TestsData;
            dct.Changed -= DocumentClashTests_Changed;
        }

        /// <summary>Обработчик внешних изменений: обновляет списки и фильтр.</summary>
        private void DocumentClashTests_Changed(object sender, EventArgs e)
        {
            GetClashTests();
            CheckPlugin();
            LoadComboBox();
            TryLoadSettingsForCurrentDocument();
            RefreshClashTestsFilter();
            UpdateSelectionSummary();

        }

        /// <summary>Загружает в коллекцию все тесты коллизий из текущего документа.</summary>
        private void GetClashTests()
        {
            DocumentClashTests dct = Application.MainDocument.GetClash().TestsData;
            ClashTests.Clear();

            foreach (SavedItem savedItem in dct.Tests)
            {
                if (savedItem.GetType() == typeof(ClashTest))
                {
                    ClashTests.Add(new CustomClashTest(savedItem as ClashTest));
                }
            }
            RefreshClashTestsFilter();
        }

        /// <summary>
        /// Включает или отключает элементы управления в зависимости от наличия документа и тестов коллизий.
        /// </summary>
        private void CheckPlugin()
        {
            bool hasGroupingCondition =
                GetSelectedGroupingMode(comboBoxGroupBy) != GroupingMode.None
                || GetSelectedGroupingMode(comboBoxThenBy) != GroupingMode.None
                || GetSelectedGroupingMode(comboBoxThirdBy) != GroupingMode.None;
            bool hasStatusSelection =
                analyzeNewStatusCheckBox.IsChecked == true
                || analyzeActiveStatusCheckBox.IsChecked == true
                || analyzeReviewedStatusCheckBox.IsChecked == true
                || analyzeApprovedStatusCheckBox.IsChecked == true
                || analyzeResolvedStatusCheckBox.IsChecked == true;

            if (Application.MainDocument == null
                || Application.MainDocument.IsClear
                || Application.MainDocument.GetClash() == null
                || Application.MainDocument.GetClash().TestsData.Tests.Count == 0)
            {
                Group_Button.IsEnabled = false;
                comboBoxGroupBy.IsEnabled = false;
                comboBoxThenBy.IsEnabled = false;
                comboBoxThirdBy.IsEnabled = false;
                Ungroup_Button.IsEnabled = false;
            }
            else
            {
                Group_Button.IsEnabled = ClashTestListBox.SelectedItems.Count > 0
                    && hasGroupingCondition
                    && hasStatusSelection;
                comboBoxGroupBy.IsEnabled = true;
                comboBoxThenBy.IsEnabled = true;
                comboBoxThirdBy.IsEnabled = true;
                Ungroup_Button.IsEnabled = ClashTestListBox.SelectedItems.Count > 0;
            }

            UpdateCustomPropertyParamsVisibility();
        }

        /// <summary>Заполняет списки режимов группировки и скрывает недоступные режимы (уровень/сетка без активной системы).</summary>
        private void LoadComboBox()
        {
            GroupByList.Clear();
            GroupThenList.Clear();
            GroupThirdList.Clear();

            foreach (GroupingMode mode in Enum.GetValues(typeof(GroupingMode)).Cast<GroupingMode>())
            {
                string label = GroupingModeOption.GetDisplayName(mode);
                GroupByList.Add(new GroupingModeOption { Mode = mode, DisplayName = label });
                GroupThenList.Add(new GroupingModeOption { Mode = mode, DisplayName = label });
                GroupThirdList.Add(new GroupingModeOption { Mode = mode, DisplayName = label });
            }

            if (Application.MainDocument.Grids.ActiveSystem == null)
            {
                RemoveModeOption(GroupByList, GroupingMode.GridIntersection);
                RemoveModeOption(GroupByList, GroupingMode.Level);
                RemoveModeOption(GroupThenList, GroupingMode.GridIntersection);
                RemoveModeOption(GroupThenList, GroupingMode.Level);
                RemoveModeOption(GroupThirdList, GroupingMode.GridIntersection);
                RemoveModeOption(GroupThirdList, GroupingMode.Level);
            }

            comboBoxGroupBy.SelectedIndex = 0;
            comboBoxThenBy.SelectedIndex = 0;
            comboBoxThirdBy.SelectedIndex = 0;
            UpdateCustomPropertyParamsVisibility();
        }

        /// <summary>Удаляет указанный режим из коллекции опций.</summary>
        private static void RemoveModeOption(ObservableCollection<GroupingModeOption> list, GroupingMode mode)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (list[i].Mode == mode)
                {
                    list.RemoveAt(i);
                }
            }
        }

        /// <summary>Возвращает режим, выбранный в комбобоксе, или <see cref="GroupingMode.None"/>.</summary>
        private static GroupingMode GetSelectedGroupingMode(ComboBox comboBox)
        {
            if (comboBox?.SelectedItem is GroupingModeOption opt)
            {
                return opt.Mode;
            }

            return GroupingMode.None;
        }

        /// <summary>Обновляет фильтр списка тестов при вводе в поле поиска.</summary>
        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshClashTestsFilter();
        }

        /// <summary>Фильтр представления: совпадение имени теста с текстом поиска без учёта регистра.</summary>
        private bool FilterClashTests(object item)
        {
            if (string.IsNullOrWhiteSpace(SearchTextBox?.Text))
            {
                return true;
            }

            var clashTest = item as CustomClashTest;
            if (clashTest == null)
            {
                return false;
            }

            return clashTest.DisplayName.IndexOf(SearchTextBox.Text.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Применяет текущий фильтр к представлению списка тестов.</summary>
        private void RefreshClashTestsFilter()
        {
            _clashTestsView?.Refresh();
        }

        /// <summary>Обновляет счётчик выбранных тестов и доступность кнопок.</summary>
        private void ClashTestListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isRangeSelectionInProgress
                && ClashTestListBox.SelectedIndex >= 0
                && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                _lastSelectedIndex = ClashTestListBox.SelectedIndex;
            }

            UpdateSelectionSummary();
            CheckPlugin();
        }

        /// <summary>Выделяет все тесты, попавшие в текущий список (с учётом фильтра).</summary>
        private void SelectAllTestsButton_Click(object sender, WIN.RoutedEventArgs e)
        {
            ClashTestListBox.SelectAll();
            if (ClashTestListBox.SelectedIndex >= 0)
            {
                _lastSelectedIndex = ClashTestListBox.SelectedIndex;
            }

            UpdateSelectionSummary();
            CheckPlugin();
        }

        /// <summary>Снимает выделение со всех тестов.</summary>
        private void ClearSelectionButton_Click(object sender, WIN.RoutedEventArgs e)
        {
            ClashTestListBox.UnselectAll();
            _lastSelectedIndex = -1;
            UpdateSelectionSummary();
            CheckPlugin();
        }

        /// <summary>Поддерживает выделение диапазона тестов при клике с зажатым Shift.</summary>
        private void ClashTestListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
            {
                return;
            }

            ListBoxItem clickedItem = FindAncestor<ListBoxItem>(e.OriginalSource as WIN.DependencyObject);
            if (clickedItem == null)
            {
                return;
            }

            int clickedIndex = ClashTestListBox.ItemContainerGenerator.IndexFromContainer(clickedItem);
            if (clickedIndex < 0)
            {
                return;
            }

            if (_lastSelectedIndex < 0)
            {
                _lastSelectedIndex = clickedIndex;
                return;
            }

            SelectRange(_lastSelectedIndex, clickedIndex);
            e.Handled = true;
        }

        /// <summary>Выделяет диапазон элементов от начального до конечного индекса включительно.</summary>
        private void SelectRange(int fromIndex, int toIndex)
        {
            int start = Math.Min(fromIndex, toIndex);
            int end = Math.Max(fromIndex, toIndex);

            _isRangeSelectionInProgress = true;
            try
            {
                ClashTestListBox.SelectedItems.Clear();
                for (int i = start; i <= end; i++)
                {
                    ClashTestListBox.SelectedItems.Add(ClashTestListBox.Items[i]);
                }
            }
            finally
            {
                _isRangeSelectionInProgress = false;
            }
        }

        /// <summary>Ищет предка указанного типа в визуальном дереве.</summary>
        private static T FindAncestor<T>(WIN.DependencyObject current) where T : WIN.DependencyObject
        {
            while (current != null)
            {
                if (current is T typed)
                {
                    return typed;
                }

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        /// <summary>Выводит в метку число выбранных тестов коллизий.</summary>
        private void UpdateSelectionSummary()
        {
            if (SelectedTestsCountLabel != null)
            {
                SelectedTestsCountLabel.Content = $"Выбрано тестов: {ClashTestListBox.SelectedItems.Count}";
            }
        }

        /// <summary>
        /// Подписывает элементы формы на изменение настроек, чтобы сохранить их по правилам автосохранения.
        /// </summary>
        private void AttachSettingsAutoSaveHandlers()
        {
            keepExistingGroupsCheckBox.Checked += SettingsControl_Changed;
            keepExistingGroupsCheckBox.Unchecked += SettingsControl_Changed;

            skipFixedGroupsCheckBox.Checked += SettingsControl_Changed;
            skipFixedGroupsCheckBox.Unchecked += SettingsControl_Changed;

            analyzeNewStatusCheckBox.Checked += SettingsControl_Changed;
            analyzeNewStatusCheckBox.Unchecked += SettingsControl_Changed;
            analyzeActiveStatusCheckBox.Checked += SettingsControl_Changed;
            analyzeActiveStatusCheckBox.Unchecked += SettingsControl_Changed;
            analyzeReviewedStatusCheckBox.Checked += SettingsControl_Changed;
            analyzeReviewedStatusCheckBox.Unchecked += SettingsControl_Changed;
            analyzeApprovedStatusCheckBox.Checked += SettingsControl_Changed;
            analyzeApprovedStatusCheckBox.Unchecked += SettingsControl_Changed;
            analyzeResolvedStatusCheckBox.Checked += SettingsControl_Changed;
            analyzeResolvedStatusCheckBox.Unchecked += SettingsControl_Changed;

            if (CustomPropertyStage1ComboA != null)
            {
                CustomPropertyStage1ComboA.LostFocus += SettingsControl_Changed;
            }

            if (CustomPropertyStage1ComboB != null)
            {
                CustomPropertyStage1ComboB.LostFocus += SettingsControl_Changed;
            }

            if (CustomPropertyStage2ComboA != null)
            {
                CustomPropertyStage2ComboA.LostFocus += SettingsControl_Changed;
            }

            if (CustomPropertyStage2ComboB != null)
            {
                CustomPropertyStage2ComboB.LostFocus += SettingsControl_Changed;
            }
        }

        /// <summary>
        /// Единый обработчик изменений параметров группировки в интерфейсе.
        /// </summary>
        private void SettingsControl_Changed(object sender, EventArgs e)
        {
            CheckPlugin();
            AutoSaveSettingsIfMissingForDocument();
        }

        /// <summary>
        /// Явно сохраняет настройки текущей модели в cfg-файл.
        /// </summary>
        private void SaveSettingsButton_Click(object sender, WIN.RoutedEventArgs e)
        {
            SaveCurrentSettingsForDocument(force: true);
        }

        /// <summary>
        /// Загружает настройки из cfg-файла для текущего документа Navisworks (по имени файла).
        /// </summary>
        private void TryLoadSettingsForCurrentDocument()
        {
            string documentKey = GetCurrentDocumentSettingsKey();
            if (string.IsNullOrWhiteSpace(documentKey))
            {
                return;
            }

            if (string.Equals(_loadedSettingsDocumentKey, documentKey, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string cfgPath = GetSettingsFilePath(documentKey);
            if (!File.Exists(cfgPath))
            {
                _loadedSettingsDocumentKey = documentKey;
                return;
            }

            Dictionary<string, string> map = ReadSettingsMap(cfgPath);
            _isApplyingSettings = true;
            try
            {
                keepExistingGroupsCheckBox.IsChecked = ParseBool(map, "KeepExistingGroups", false);
                skipFixedGroupsCheckBox.IsChecked = ParseBool(map, "SkipFixedGroups", true);
                analyzeNewStatusCheckBox.IsChecked = ParseBool(map, "AnalyzeStatusNew", true);
                analyzeActiveStatusCheckBox.IsChecked = ParseBool(map, "AnalyzeStatusActive", true);
                analyzeReviewedStatusCheckBox.IsChecked = ParseBool(map, "AnalyzeStatusReviewed", true);
                analyzeApprovedStatusCheckBox.IsChecked = ParseBool(map, "AnalyzeStatusApproved", true);
                analyzeResolvedStatusCheckBox.IsChecked = ParseBool(map, "AnalyzeStatusResolved", false);

                SetModeSelection(comboBoxGroupBy, ParseText(map, "GroupByMode"));
                SetModeSelection(comboBoxThenBy, ParseText(map, "ThenByMode"));
                SetModeSelection(comboBoxThirdBy, ParseText(map, "ThirdByMode"));
                ApplySelectedTests(ParseText(map, "SelectedTests"));

                CustomPropertyStage1ComboA.Text = ParseText(map, "CustomPropertyStage1A");
                CustomPropertyStage1ComboB.Text = ParseText(map, "CustomPropertyStage1B");
                CustomPropertyStage2ComboA.Text = ParseText(map, "CustomPropertyStage2A");
                CustomPropertyStage2ComboB.Text = ParseText(map, "CustomPropertyStage2B");
                CustomPropertyStage3ComboA.Text = ParseText(map, "CustomPropertyStage3A");
                CustomPropertyStage3ComboB.Text = ParseText(map, "CustomPropertyStage3B");
            }
            finally
            {
                _isApplyingSettings = false;
            }

            UpdateCustomPropertyParamsVisibility();
            CheckPlugin();
            _loadedSettingsDocumentKey = documentKey;
        }

        /// <summary>
        /// Выполняет автосохранение только если для текущей модели ещё нет файла настроек.
        /// </summary>
        private void AutoSaveSettingsIfMissingForDocument()
        {
            SaveCurrentSettingsForDocument(force: false);
        }

        /// <summary>
        /// Сохраняет текущие настройки в cfg-файл модели.
        /// </summary>
        /// <param name="force">
        /// <c>true</c> — сохранить всегда; <c>false</c> — сохранить только при отсутствии файла.
        /// </param>
        private void SaveCurrentSettingsForDocument(bool force)
        {
            if (_isApplyingSettings)
            {
                return;
            }

            string documentKey = GetCurrentDocumentSettingsKey();
            if (string.IsNullOrWhiteSpace(documentKey))
            {
                return;
            }

            string cfgPath = GetSettingsFilePath(documentKey);
            if (!force && File.Exists(cfgPath))
            {
                return;
            }

            string cfgDir = IOPath.GetDirectoryName(cfgPath);
            if (!string.IsNullOrWhiteSpace(cfgDir))
            {
                Directory.CreateDirectory(cfgDir);
            }

            List<string> lines = new List<string>
            {
                "KeepExistingGroups=" + (keepExistingGroupsCheckBox.IsChecked == true ? "true" : "false"),
                "SkipFixedGroups=" + (skipFixedGroupsCheckBox.IsChecked == true ? "true" : "false"),
                "AnalyzeStatusNew=" + (analyzeNewStatusCheckBox.IsChecked == true ? "true" : "false"),
                "AnalyzeStatusActive=" + (analyzeActiveStatusCheckBox.IsChecked == true ? "true" : "false"),
                "AnalyzeStatusReviewed=" + (analyzeReviewedStatusCheckBox.IsChecked == true ? "true" : "false"),
                "AnalyzeStatusApproved=" + (analyzeApprovedStatusCheckBox.IsChecked == true ? "true" : "false"),
                "AnalyzeStatusResolved=" + (analyzeResolvedStatusCheckBox.IsChecked == true ? "true" : "false"),
                "GroupByMode=" + GetSelectedGroupingMode(comboBoxGroupBy),
                "ThenByMode=" + GetSelectedGroupingMode(comboBoxThenBy),
                "ThirdByMode=" + GetSelectedGroupingMode(comboBoxThirdBy),
                "SelectedTests=" + EscapeCfgValue(GetSelectedTestsValue()),
                "CustomPropertyStage1A=" + EscapeCfgValue(CustomPropertyStage1ComboA.Text),
                "CustomPropertyStage1B=" + EscapeCfgValue(CustomPropertyStage1ComboB.Text),
                "CustomPropertyStage2A=" + EscapeCfgValue(CustomPropertyStage2ComboA.Text),
                "CustomPropertyStage2B=" + EscapeCfgValue(CustomPropertyStage2ComboB.Text),
                "CustomPropertyStage3A=" + EscapeCfgValue(CustomPropertyStage3ComboA.Text),
                "CustomPropertyStage3B=" + EscapeCfgValue(CustomPropertyStage3ComboB.Text),
            };

            File.WriteAllLines(cfgPath, lines, Encoding.UTF8);
            _loadedSettingsDocumentKey = documentKey;
        }

        /// <summary>
        /// Экранирует спецсимволы перед записью значения в строку cfg.
        /// </summary>
        private static string EscapeCfgValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\r", string.Empty)
                .Replace("\n", "\\n");
        }

        /// <summary>
        /// Восстанавливает экранированные спецсимволы после чтения строки cfg.
        /// </summary>
        private static string UnescapeCfgValue(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\n", "\n")
                .Replace("\\\\", "\\");
        }

        /// <summary>
        /// Читает cfg-файл в словарь ключ-значение.
        /// </summary>
        private static Dictionary<string, string> ReadSettingsMap(string cfgPath)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(cfgPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separatorIndex).Trim();
                string value = line.Substring(separatorIndex + 1);
                map[key] = UnescapeCfgValue(value);
            }

            return map;
        }

        /// <summary>
        /// Возвращает строковое значение настройки или пустую строку, если ключ отсутствует.
        /// </summary>
        private static string ParseText(Dictionary<string, string> map, string key)
        {
            string value;
            if (map.TryGetValue(key, out value))
            {
                return value ?? string.Empty;
            }

            return string.Empty;
        }

        /// <summary>
        /// Возвращает булево значение настройки или значение по умолчанию при ошибке разбора.
        /// </summary>
        private static bool ParseBool(Dictionary<string, string> map, string key, bool defaultValue)
        {
            string value;
            if (!map.TryGetValue(key, out value))
            {
                return defaultValue;
            }

            bool parsed;
            if (bool.TryParse(value, out parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        /// <summary>
        /// Выбирает режим группировки в комбобоксе по имени значения enum.
        /// </summary>
        private static void SetModeSelection(ComboBox comboBox, string modeName)
        {
            if (comboBox == null || string.IsNullOrWhiteSpace(modeName))
            {
                return;
            }

            foreach (object item in comboBox.Items)
            {
                GroupingModeOption option = item as GroupingModeOption;
                if (option != null && string.Equals(option.Mode.ToString(), modeName, StringComparison.OrdinalIgnoreCase))
                {
                    comboBox.SelectedItem = option;
                    return;
                }
            }
        }

        /// <summary>
        /// Возвращает каталог хранения пользовательских настроек SmartGroupClashes.
        /// </summary>
        private static string GetSettingsRootDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return IOPath.Combine(appData, "SmartGroupClashes", "settings");
        }

        /// <summary>
        /// Формирует полный путь к cfg-файлу настроек для указанного ключа документа.
        /// </summary>
        private static string GetSettingsFilePath(string documentKey)
        {
            string safeDocumentKey = SanitizeFileName(documentKey);
            return IOPath.Combine(GetSettingsRootDirectory(), safeDocumentKey + ".cfg");
        }

        /// <summary>
        /// Приводит произвольную строку к безопасному имени файла.
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unnamed";
            }

            char[] invalidChars = IOPath.GetInvalidFileNameChars();
            StringBuilder builder = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                bool isInvalid = invalidChars.Contains(c);
                builder.Append(isInvalid ? '_' : c);
            }

            string cleaned = builder.ToString().Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "unnamed" : cleaned;
        }

        /// <summary>
        /// Возвращает ключ настроек текущего документа: имя файла без расширения.
        /// </summary>
        private static string GetCurrentDocumentSettingsKey()
        {
            if (Application.MainDocument == null || Application.MainDocument.IsClear)
            {
                return null;
            }

            string fullPath = string.Empty;
            try
            {
                fullPath = Application.MainDocument.FileName;
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(fullPath))
            {
                return null;
            }

            return IOPath.GetFileNameWithoutExtension(fullPath);
        }

        /// <summary>
        /// Возвращает выбранные тесты коллизий как строку с разделителем ';'.
        /// </summary>
        private string GetSelectedTestsValue()
        {
            List<string> selectedNames = new List<string>();
            foreach (object selected in ClashTestListBox.SelectedItems)
            {
                CustomClashTest test = selected as CustomClashTest;
                string name = test?.DisplayName?.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    selectedNames.Add(name.Replace(";", "\\;"));
                }
            }

            return string.Join(";", selectedNames);
        }

        /// <summary>
        /// Применяет выбор тестов коллизий по сохранённому списку имён.
        /// </summary>
        private void ApplySelectedTests(string serializedSelectedTests)
        {
            if (ClashTestListBox == null)
            {
                return;
            }

            ClashTestListBox.SelectedItems.Clear();
            if (string.IsNullOrWhiteSpace(serializedSelectedTests))
            {
                return;
            }

            HashSet<string> savedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string token in SplitEscapedBySemicolon(serializedSelectedTests))
            {
                string cleaned = token?.Trim();
                if (!string.IsNullOrWhiteSpace(cleaned))
                {
                    savedNames.Add(cleaned);
                }
            }

            if (savedNames.Count == 0)
            {
                return;
            }

            foreach (object item in ClashTestListBox.Items)
            {
                CustomClashTest test = item as CustomClashTest;
                string name = test?.DisplayName?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && savedNames.Contains(name))
                {
                    ClashTestListBox.SelectedItems.Add(item);
                }
            }
        }

        /// <summary>
        /// Делит строку по ';' с поддержкой экранирования '\;'.
        /// </summary>
        private static List<string> SplitEscapedBySemicolon(string value)
        {
            List<string> parts = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return parts;
            }

            StringBuilder current = new StringBuilder();
            bool escape = false;
            foreach (char c in value)
            {
                if (escape)
                {
                    current.Append(c);
                    escape = false;
                    continue;
                }

                if (c == '\\')
                {
                    escape = true;
                    continue;
                }

                if (c == ';')
                {
                    parts.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                current.Append(c);
            }

            if (escape)
            {
                current.Append('\\');
            }

            parts.Add(current.ToString());
            return parts;
        }
    }

    /// <summary>
    /// Обёртка над <see cref="ClashTest"/> для привязки в списке (имя, выборы A/B).
    /// </summary>
    public class CustomClashTest
    {
        private readonly ClashTest _clashTest;

        /// <summary>Создаёт обёртку для указанного теста коллизий.</summary>
        /// <param name="test">Тест из документа Navisworks.</param>
        public CustomClashTest(ClashTest test)
        {
            _clashTest = test;
        }

        /// <summary>Отображаемое имя теста.</summary>
        public string DisplayName { get { return _clashTest.DisplayName; } }

        /// <summary>Исходный тест коллизий.</summary>
        public ClashTest ClashTest { get { return _clashTest; } }

        /// <summary>Краткое описание набора выбора A для подписи в списке.</summary>
        public string SelectionAName
        {
            get { return GetSelectedItem(_clashTest.SelectionA); }
        }

        /// <summary>Краткое описание набора выбора B для подписи в списке.</summary>
        public string SelectionBName
        {
            get { return GetSelectedItem(_clashTest.SelectionB); }
        }

        /// <summary>Формирует строку для UI по источникам выбора Navisworks.</summary>
        /// <returns>Текст для отображения в шаблоне элемента списка.</returns>
        private string GetSelectedItem(ClashSelection selection)
        {
            string result = "";
            if (selection.Selection.HasSelectionSources)
            {
                result = selection.Selection.SelectionSources.FirstOrDefault().ToString();
                if (result.Contains("lcop_selection_set_tree\\"))
                {
                    result = result.Replace("lcop_selection_set_tree\\", "");
                }

                if (selection.Selection.SelectionSources.Count > 1)
                {
                    result = result + " (и другие наборы выбора)";
                }

            }
            else if (selection.Selection.GetSelectedItems().Count == 0)
            {
                result = "Элементы не выбраны.";
            }
            else if (selection.Selection.GetSelectedItems().Count == 1)
            {
                result = selection.Selection.GetSelectedItems().FirstOrDefault().DisplayName;
            }
            else
            {
                result = selection.Selection.GetSelectedItems().FirstOrDefault().DisplayName;
                foreach (ModelItem item in selection.Selection.GetSelectedItems().Skip(1))
                {
                    result = result + "; " + item.DisplayName;
                }
            }

            return result;
        }

    }
}
