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

        /// <summary>Подсказки для полей «Своё свойство» (отображаемые имена свойств).</summary>
        public ObservableCollection<string> CustomPropertySuggestions { get; } = new ObservableCollection<string>();

        private ICollectionView _clashTestsView;
        private int _lastSelectedIndex = -1;
        private bool _isRangeSelectionInProgress;

        /// <summary>Последний выбранный тест (для совместимости привязок, если используется).</summary>
        public ClashTest SelectedClashTest { get; set; }

        /// <summary>Создаёт панель, заполняет списки и подписывается на события документа.</summary>
        public SmartGroupClashesInterface()
        {
            InitializeComponent();

            ClashTests = new ObservableCollection<CustomClashTest>();
            GroupByList = new ObservableCollection<GroupingModeOption>();
            GroupThenList = new ObservableCollection<GroupingModeOption>();

            foreach (string suggestion in GroupingFunctions.DefaultCustomPropertyDisplayNameSuggestions)
            {
                CustomPropertySuggestions.Add(suggestion);
            }

            RegisterChanges();

            this.DataContext = this;
            _clashTestsView = CollectionViewSource.GetDefaultView(ClashTests);
            _clashTestsView.Filter = FilterClashTests;
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

                string customStage1A = null;
                string customStage1B = null;
                string customStage2A = null;
                string customStage2B = null;

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

                foreach (object selectedItem in ClashTestListBox.SelectedItems)
                {
                    CustomClashTest selectedClashTest = (CustomClashTest)selectedItem;
                    ClashTest clashTest = selectedClashTest.ClashTest;

                    if (clashTest.Children.Count != 0)
                    {
                        if (thenByModeSel != GroupingMode.None
                            || groupByMode != GroupingMode.None)
                        {

                            if (thenByModeSel == GroupingMode.None
                                && groupByMode != GroupingMode.None)
                            {
                                GroupingMode mode = groupByMode;
                                bool grouped = GroupingFunctions.GroupClashes(
                                    clashTest,
                                    mode,
                                    GroupingMode.None,
                                    (bool)keepExistingGroupsCheckBox.IsChecked,
                                    (bool)skipFixedGroupsCheckBox.IsChecked,
                                    customStage1A,
                                    customStage1B,
                                    customStage2A,
                                    customStage2B);
                                groupingPerformed = groupingPerformed || grouped;
                            }
                            else if (groupByMode == GroupingMode.None
                                && thenByModeSel != GroupingMode.None)
                            {
                                GroupingMode mode = thenByModeSel;
                                bool grouped = GroupingFunctions.GroupClashes(
                                    clashTest,
                                    mode,
                                    GroupingMode.None,
                                    (bool)keepExistingGroupsCheckBox.IsChecked,
                                    (bool)skipFixedGroupsCheckBox.IsChecked,
                                    customStage1A,
                                    customStage1B,
                                    customStage2A,
                                    customStage2B);
                                groupingPerformed = groupingPerformed || grouped;
                            }
                            else
                            {
                                GroupingMode byMode = groupByMode;
                                GroupingMode thenByMode = thenByModeSel;
                                bool grouped = GroupingFunctions.GroupClashes(
                                    clashTest,
                                    byMode,
                                    thenByMode,
                                    (bool)keepExistingGroupsCheckBox.IsChecked,
                                    (bool)skipFixedGroupsCheckBox.IsChecked,
                                    customStage1A,
                                    customStage1B,
                                    customStage2A,
                                    customStage2B);
                                groupingPerformed = groupingPerformed || grouped;
                            }
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
        }

        /// <summary>Настраивает видимость панелей параметров пользовательских свойств (этапы 1 и 2).</summary>
        private void UpdateCustomPropertyParamsVisibility()
        {
            if (CustomPropertyParamsPanel == null)
            {
                return;
            }

            bool stage1 = GetSelectedGroupingMode(comboBoxGroupBy) == GroupingMode.CustomProperty;
            bool stage2 = GetSelectedGroupingMode(comboBoxThenBy) == GroupingMode.CustomProperty;
            bool need = stage1 || stage2;
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
            if (Application.MainDocument == null
                || Application.MainDocument.IsClear
                || Application.MainDocument.GetClash() == null
                || Application.MainDocument.GetClash().TestsData.Tests.Count == 0)
            {
                Group_Button.IsEnabled = false;
                comboBoxGroupBy.IsEnabled = false;
                comboBoxThenBy.IsEnabled = false;
                Ungroup_Button.IsEnabled = false;
            }
            else
            {
                Group_Button.IsEnabled = ClashTestListBox.SelectedItems.Count > 0;
                comboBoxGroupBy.IsEnabled = true;
                comboBoxThenBy.IsEnabled = true;
                Ungroup_Button.IsEnabled = ClashTestListBox.SelectedItems.Count > 0;
            }

            UpdateCustomPropertyParamsVisibility();
        }

        /// <summary>Заполняет списки режимов группировки и скрывает недоступные режимы (уровень/сетка без активной системы).</summary>
        private void LoadComboBox()
        {
            GroupByList.Clear();
            GroupThenList.Clear();

            foreach (GroupingMode mode in Enum.GetValues(typeof(GroupingMode)).Cast<GroupingMode>())
            {
                string label = GroupingModeOption.GetDisplayName(mode);
                GroupByList.Add(new GroupingModeOption { Mode = mode, DisplayName = label });
                GroupThenList.Add(new GroupingModeOption { Mode = mode, DisplayName = label });
            }

            if (Application.MainDocument.Grids.ActiveSystem == null)
            {
                RemoveModeOption(GroupByList, GroupingMode.GridIntersection);
                RemoveModeOption(GroupByList, GroupingMode.Level);
                RemoveModeOption(GroupThenList, GroupingMode.GridIntersection);
                RemoveModeOption(GroupThenList, GroupingMode.Level);
            }

            comboBoxGroupBy.SelectedIndex = 0;
            comboBoxThenBy.SelectedIndex = 0;
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
