using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api.Clash;
using Autodesk.Navisworks.Api;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SmartGroupClashes
{
    /// <summary>
    /// Группировка и разгруппировка результатов теста коллизий в открытом документе Navisworks.
    /// </summary>
    internal static class GroupingFunctions
    {
        /// <summary>
        /// Сравнение экземпляров по ссылке (для словарей «копия пересечения → исходный объект в документе»).
        /// </summary>
        private sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
            where T : class
        {
            /// <inheritdoc />
            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            /// <inheritdoc />
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        /// <summary>Подсказки для списка имён свойств (отображаемое имя в Navisworks).</summary>
        public static readonly string[] DefaultCustomPropertyDisplayNameSuggestions =
        {
            "Семейство",
            "Тип",
            "Имя",
            "Марка",
            "Комментарии",
            "Уровень",
            "Площадь",
            "Длина",
            "Объём",
            "Материал",
            "Ключевые слова",
        };

        /// <summary>
        /// Выполняет группировку пересечений в указанном тесте коллизий.
        /// </summary>
        /// <param name="selectedClashTest">Тест коллизий для изменения.</param>
        /// <param name="groupingMode">Первый уровень группировки («Группировать по»).</param>
        /// <param name="subgroupingMode">Второй уровень («Затем по»); <see cref="GroupingMode.None"/>, если не задан.</param>
        /// <param name="keepExistingGroups">Сохранять ли уже существующие группы в тесте.</param>
        /// <param name="skipAllFixedGroups">Не создавать группы, где все пересечения уже в статусе «исправлено»/«утверждено».</param>
        /// <param name="customPropertyStage1SelectionA">Отображаемое имя свойства для стороны A на первом уровне (режим «Своё свойство»).</param>
        /// <param name="customPropertyStage1SelectionB">Отображаемое имя свойства для стороны B на первом уровне.</param>
        /// <param name="customPropertyStage2SelectionA">Отображаемое имя свойства для стороны A на втором уровне.</param>
        /// <param name="customPropertyStage2SelectionB">Отображаемое имя свойства для стороны B на втором уровне.</param>
        /// <returns><c>true</c>, если в тест были записаны новые группы или пересечения; <c>false</c>, если нечего обрабатывать.</returns>
        public static bool GroupClashes(
            ClashTest selectedClashTest,
            GroupingMode groupingMode,
            GroupingMode subgroupingMode,
            bool keepExistingGroups,
            bool skipAllFixedGroups,
            string customPropertyStage1SelectionA = null,
            string customPropertyStage1SelectionB = null,
            string customPropertyStage2SelectionA = null,
            string customPropertyStage2SelectionB = null)
        {
            // Собрать отдельные пересечения из дерева теста (с учётом существующих групп).
            List<ClashResult> clashResults = GetIndividualClashResults(selectedClashTest,keepExistingGroups).ToList();
            if (clashResults.Count == 0)
            {
                return false;
            }

            Dictionary<string, ClashResult> sourceByDisplayName = clashResults
                .Where(r => r != null && !string.IsNullOrWhiteSpace(r.DisplayName))
                .GroupBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.CurrentCultureIgnoreCase);

            List<ClashResultGroup> clashResultGroups = new List<ClashResultGroup>();
            bool needSourceMapForSubgroup = subgroupingMode == GroupingMode.ElementName
                || subgroupingMode == GroupingMode.FamilyName
                || subgroupingMode == GroupingMode.TypeName
                || subgroupingMode == GroupingMode.CustomProperty;
            Dictionary<ClashResult, ClashResult> sourceByCopy = needSourceMapForSubgroup
                ? new Dictionary<ClashResult, ClashResult>(new ReferenceEqualityComparer<ClashResult>())
                : null;

            string stage1A = groupingMode == GroupingMode.CustomProperty ? customPropertyStage1SelectionA : null;
            string stage1B = groupingMode == GroupingMode.CustomProperty ? customPropertyStage1SelectionB : null;
            Dictionary<ClashResult, string> cachedSubgroupNameBySource = BuildSubgroupNameCache(
                clashResults,
                subgroupingMode,
                customPropertyStage2SelectionA,
                customPropertyStage2SelectionB);

            // Первый уровень: сформировать группы по выбранному режиму.
            CreateGroup(
                ref clashResultGroups,
                groupingMode,
                clashResults,
                "",
                stage1A,
                stage1B,
                sourceByCopy);

            // Второй уровень: при необходимости разбить группы на подгруппы.
            if (subgroupingMode != GroupingMode.None)
            {
                string stage2A = subgroupingMode == GroupingMode.CustomProperty ? customPropertyStage2SelectionA : null;
                string stage2B = subgroupingMode == GroupingMode.CustomProperty ? customPropertyStage2SelectionB : null;
                CreateSubGroups(
                    ref clashResultGroups,
                    subgroupingMode,
                    stage2A,
                    stage2B,
                    sourceByCopy,
                    sourceByDisplayName,
                    cachedSubgroupNameBySource);
            }

            // Исключить группы, где все пересечения уже «исправлены»/«утверждены» (если опция включена).
            List<ClashResult> fixedUngroupedResults = new List<ClashResult>();
            if (skipAllFixedGroups)
            {
                fixedUngroupedResults = RemoveFullyFixedGroups(ref clashResultGroups);
            }

            // Группы из одного пересечения не имеют смысла — вернуть такие пересечения в общий список.
            List<ClashResult> ungroupedClashResults = RemoveOneClashGroup(ref clashResultGroups);
            ungroupedClashResults.AddRange(fixedUngroupedResults);

            // При необходимости добавить к результату копии уже существующих групп теста.
            if (keepExistingGroups) clashResultGroups.AddRange(BackupExistingClashGroups(selectedClashTest));

            if (clashResultGroups.Count == 0 && ungroupedClashResults.Count == 0)
            {
                return false;
            }

            // Записать сформированные группы и «висячие» пересечения в документ (транзакция Navisworks).
            ProcessClashGroup(clashResultGroups, ungroupedClashResults, selectedClashTest);
            return true;
        }

        private static void CreateGroup(
            ref List<ClashResultGroup> clashResultGroups,
            GroupingMode groupingMode,
            List<ClashResult> clashResults,
            string initialName,
            string customPropertyDisplayNameForSelectionA,
            string customPropertyDisplayNameForSelectionB,
            Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            // Распределить все пересечения по веткам в зависимости от режима.
            switch (groupingMode)
            {
                case GroupingMode.None:
                    return;
                case GroupingMode.Level:
                    clashResultGroups = GroupByLevel(clashResults, initialName, sourceByCopy);
                    break;
                case GroupingMode.GridIntersection:
                    clashResultGroups = GroupByGridIntersection(clashResults, initialName, sourceByCopy);
                    break;
                case GroupingMode.SelectionA:
                case GroupingMode.SelectionB:
                    clashResultGroups = GroupByElementOfAGivenSelection(clashResults, groupingMode, initialName, sourceByCopy);
                    break;
                case GroupingMode.ModelA:
                case GroupingMode.ModelB:
                    clashResultGroups = GroupByElementOfAGivenModel(clashResults, groupingMode, initialName, sourceByCopy);
                    break;
                case GroupingMode.ApprovedBy:
                case GroupingMode.AssignedTo:
                case GroupingMode.Status:
                    clashResultGroups = GroupByProperties(clashResults, groupingMode, initialName, sourceByCopy);
                    break;
                case GroupingMode.ElementName:
                    clashResultGroups = GroupByElementName(clashResults, initialName, sourceByCopy);
                    break;
                case GroupingMode.FamilyName:
                    clashResultGroups = GroupByFamilyOrTypeName(clashResults, initialName, useFamily: true, sourceByCopy: sourceByCopy);
                    break;
                case GroupingMode.TypeName:
                    clashResultGroups = GroupByFamilyOrTypeName(clashResults, initialName, useFamily: false, sourceByCopy: sourceByCopy);
                    break;
                case GroupingMode.CustomProperty:
                    clashResultGroups = GroupByCustomProperty(
                        clashResults,
                        initialName,
                        customPropertyDisplayNameForSelectionA,
                        customPropertyDisplayNameForSelectionB,
                        sourceByCopy);
                    break;
            }
        }

        private static void CreateSubGroups(
            ref List<ClashResultGroup> clashResultGroups,
            GroupingMode mode,
            string customPropertyDisplayNameForSelectionA,
            string customPropertyDisplayNameForSelectionB,
            Dictionary<ClashResult, ClashResult> sourceByCopy,
            Dictionary<string, ClashResult> sourceByDisplayName,
            Dictionary<ClashResult, string> cachedSubgroupNameBySource)
        {
            if (cachedSubgroupNameBySource != null)
            {
                clashResultGroups = CreateSubGroupsFromCache(
                    clashResultGroups,
                    mode,
                    sourceByCopy,
                    sourceByDisplayName,
                    cachedSubgroupNameBySource);
                return;
            }

            List<ClashResultGroup> clashResultSubGroups = new List<ClashResultGroup>();

            foreach (ClashResultGroup group in clashResultGroups)
            {

                List<ClashResult> clashResults = new List<ClashResult>();

                foreach (SavedItem item in group.Children)
                {
                    ClashResult clashResult = item as ClashResult;
                    if (clashResult != null)
                    {
                        ClashResult source = clashResult;
                        if (sourceByCopy != null)
                        {
                            ClashResult mappedSource;
                            if (sourceByCopy.TryGetValue(clashResult, out mappedSource))
                            {
                                source = mappedSource;
                            }
                        }

                        if (ReferenceEquals(source, clashResult) && sourceByDisplayName != null)
                        {
                            ClashResult mappedByName;
                            string key = clashResult.DisplayName;
                            if (!string.IsNullOrWhiteSpace(key)
                                && sourceByDisplayName.TryGetValue(key, out mappedByName))
                            {
                                source = mappedByName;
                            }
                        }

                        clashResults.Add(source);
                    }
                }

                List<ClashResultGroup> clashResultTempSubGroups = new List<ClashResultGroup>();
                CreateGroup(
                    ref clashResultTempSubGroups,
                    mode,
                    clashResults,
                    group.DisplayName + "_",
                    customPropertyDisplayNameForSelectionA,
                    customPropertyDisplayNameForSelectionB,
                    sourceByCopy);
                clashResultSubGroups.AddRange(clashResultTempSubGroups);
            }

            clashResultGroups = clashResultSubGroups;
        }

        private static List<ClashResultGroup> CreateSubGroupsFromCache(
            List<ClashResultGroup> clashResultGroups,
            GroupingMode mode,
            Dictionary<ClashResult, ClashResult> sourceByCopy,
            Dictionary<string, ClashResult> sourceByDisplayName,
            Dictionary<ClashResult, string> cachedSubgroupNameBySource)
        {
            List<ClashResultGroup> clashResultSubGroups = new List<ClashResultGroup>();

            foreach (ClashResultGroup group in clashResultGroups)
            {
                Dictionary<string, ClashResultGroup> byName = new Dictionary<string, ClashResultGroup>(StringComparer.CurrentCultureIgnoreCase);
                foreach (ClashResult clashResult in group.Children.OfType<ClashResult>())
                {
                    ClashResult source = clashResult;
                    if (sourceByCopy != null)
                    {
                        ClashResult mappedSource;
                        if (sourceByCopy.TryGetValue(clashResult, out mappedSource))
                        {
                            source = mappedSource;
                        }
                    }

                    if (ReferenceEquals(source, clashResult) && sourceByDisplayName != null)
                    {
                        ClashResult mappedByName;
                        string key = clashResult.DisplayName;
                        if (!string.IsNullOrWhiteSpace(key)
                            && sourceByDisplayName.TryGetValue(key, out mappedByName))
                        {
                            source = mappedByName;
                        }
                    }

                    string subgroupName;
                    if (!cachedSubgroupNameBySource.TryGetValue(source, out subgroupName)
                        || string.IsNullOrWhiteSpace(subgroupName))
                    {
                        subgroupName = GetEmptyLabelForMode(mode);
                    }

                    ClashResultGroup subgroup;
                    if (!byName.TryGetValue(subgroupName, out subgroup))
                    {
                        subgroup = new ClashResultGroup();
                        subgroup.DisplayName = group.DisplayName + "_" + subgroupName;
                        byName.Add(subgroupName, subgroup);
                    }

                    // Элемент дерева нельзя одновременно прикрепить к двум родителям — добавляем копию в подгруппу.
                    subgroup.Children.Add((ClashResult)clashResult.CreateCopy());
                }

                clashResultSubGroups.AddRange(byName.Values);
            }

            return clashResultSubGroups;
        }

        /// <summary>
        /// Удаляет все группы в тесте коллизий, оставляя плоский список пересечений.
        /// </summary>
        /// <param name="selectedClashTest">Тест коллизий для изменения.</param>
        public static void UnGroupClashes(ClashTest selectedClashTest)
        {
            List<ClashResultGroup> groups = new List<ClashResultGroup>();
            List<ClashResult> results = GetIndividualClashResults(selectedClashTest,false).ToList();
            List<ClashResult> copiedResult = new List<ClashResult>();

            foreach (ClashResult result in results)
            {
                copiedResult.Add((ClashResult)result.CreateCopy());
            }

            // Записать пустой набор групп и копии пересечений в тест (фактически — снять группировку).
            ProcessClashGroup(groups, copiedResult, selectedClashTest);

        }

        #region Методы группировки по режимам

        /// <summary>
        /// Создаёт копию пересечения и при необходимости запоминает соответствие «копия → исходник».
        /// </summary>
        private static ClashResult CreateTrackedCopy(ClashResult source, Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            ClashResult copy = (ClashResult)source.CreateCopy();
            if (sourceByCopy != null)
            {
                sourceByCopy[copy] = source;
            }
            return copy;
        }

        private static List<ClashResultGroup> GroupByLevel(List<ClashResult> results, string initialName, Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            // Активная система сетки/уровней уже проверена вызывающим кодом (интерфейс скрывает режим при отсутствии).
            GridSystem gridSystem = Application.MainDocument.Grids.ActiveSystem;
            Dictionary<GridLevel, ClashResultGroup> groups = new Dictionary<GridLevel, ClashResultGroup>();
            ClashResultGroup currentGroup;

            // Пересечения без привязки к уровню сетки попадают в отдельную группу.
            ClashResultGroup nullGridGroup = new ClashResultGroup();
            nullGridGroup.DisplayName = initialName + "Без уровня";

            foreach (ClashResult result in results)
            {
                ClashResult copiedResult = CreateTrackedCopy(result, sourceByCopy);
                GridIntersection closestIntersection = null;
                try
                {
                    closestIntersection = gridSystem.ClosestIntersection(copiedResult.Center);
                }
                catch
                {
                    // Ошибка геометрии/API — пропускаем пересечение в группу «Без уровня», остальные обрабатываем дальше.
                }
                if (closestIntersection != null)
                {
                    GridLevel closestLevel = closestIntersection.Level;

                    if (!groups.TryGetValue(closestLevel, out currentGroup))
                    {
                        currentGroup = new ClashResultGroup();
                        string displayName = closestLevel.DisplayName;
                        if (string.IsNullOrEmpty(displayName)) { displayName = "Уровень без имени"; }
                        currentGroup.DisplayName = initialName + displayName;
                        groups.Add(closestLevel, currentGroup);
                    }
                    currentGroup.Children.Add(copiedResult);
                }
                else
                {
                    nullGridGroup.Children.Add(copiedResult);
                }
            }

            IOrderedEnumerable<KeyValuePair<GridLevel, ClashResultGroup>> list = groups.OrderBy(key => key.Key.Elevation);
            groups = list.ToDictionary((keyItem) => keyItem.Key, (valueItem) => valueItem.Value);

            List<ClashResultGroup> groupsByLevel = groups.Values.ToList();
            if (nullGridGroup.Children.Count != 0) groupsByLevel.Add(nullGridGroup);

            return groupsByLevel;
        }

        private static List<ClashResultGroup> GroupByGridIntersection(List<ClashResult> results, string initialName, Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            // См. <see cref="GroupByLevel"/>: активная система сетки проверена до вызова.
            GridSystem gridSystem = Application.MainDocument.Grids.ActiveSystem;
            Dictionary<string, ClashResultGroup> groups = new Dictionary<string, ClashResultGroup>();
            ClashResultGroup currentGroup;

            // Пересечения без найденного узла сетки — в общую «запасную» группу.
            ClashResultGroup nullGridGroup = new ClashResultGroup();
            nullGridGroup.DisplayName = initialName + "Нет пересечения осей";

            foreach (ClashResult result in results)
            {
                ClashResult copiedResult = CreateTrackedCopy(result, sourceByCopy);
                GridIntersection closestGridIntersection = null;
                try
                {
                    closestGridIntersection = gridSystem.ClosestIntersection(copiedResult.Center);
                }
                catch
                {
                    // Ошибка при поиске ближайшего пересечения осей — пересечение уходит в группу «Нет пересечения осей».
                }
                if (closestGridIntersection != null)
                {
                    string groupKey = GetGridIntersectionKeyWithoutLevel(closestGridIntersection);

                    if (!groups.TryGetValue(groupKey, out currentGroup))
                    {
                        currentGroup = new ClashResultGroup();
                        string displayName = groupKey;
                        if (string.IsNullOrEmpty(displayName)) { displayName = "Пересечение без имени"; }
                        currentGroup.DisplayName = initialName + displayName;
                        groups.Add(groupKey, currentGroup);
                    }
                    currentGroup.Children.Add(copiedResult);
                }
                else
                {
                    nullGridGroup.Children.Add(copiedResult);
                }
            }
           
            IOrderedEnumerable<KeyValuePair<string, ClashResultGroup>> list =
                groups.OrderBy(key => key.Key, StringComparer.CurrentCultureIgnoreCase);
            groups = list.ToDictionary((keyItem) => keyItem.Key, (valueItem) => valueItem.Value);

            List<ClashResultGroup> groupsByGridIntersection = groups.Values.ToList();
            if (nullGridGroup.Children.Count != 0) groupsByGridIntersection.Add(nullGridGroup);

            return groupsByGridIntersection;
        }

        private static string GetGridIntersectionKeyWithoutLevel(GridIntersection intersection)
        {
            // В строке отображения Navisworks после «:» часто указан этаж (например «A/1: Level 2»).
            // В режиме только «Пересечение осей» группируем по осям, игнорируя суффикс уровня.
            string displayName = intersection?.DisplayName ?? string.Empty;
            int separatorIndex = displayName.IndexOf(':');
            if (separatorIndex >= 0)
            {
                return displayName.Substring(0, separatorIndex).Trim();
            }

            return displayName.Trim();
        }

        private static List<ClashResultGroup> GroupByElementOfAGivenSelection(List<ClashResult> results, GroupingMode mode, string initialName, Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            Dictionary<ModelItem, ClashResultGroup> groups = new Dictionary<ModelItem, ClashResultGroup>();
            ClashResultGroup currentGroup;
            List<ClashResultGroup> emptyClashResultGroups = new List<ClashResultGroup>();

            foreach (ClashResult result in results)
            {

                ModelItem modelItem = null;

                if (mode == GroupingMode.SelectionA)
                {
                    if (result.CompositeItem1 != null)
                    {
                        modelItem = GetSignificantAncestorOrSelf(result.CompositeItem1);
                    }
                    else if (result.CompositeItem2 != null)
                    {
                        modelItem = GetSignificantAncestorOrSelf(result.CompositeItem2);
                    }
                }
                else if (mode == GroupingMode.SelectionB)
                {
                    if (result.CompositeItem2 != null)
                    {
                        modelItem = GetSignificantAncestorOrSelf(result.CompositeItem2);
                    }
                    else if (result.CompositeItem1 != null)
                    {
                        modelItem = GetSignificantAncestorOrSelf(result.CompositeItem1);
                    }
                }

                string displayName = "Пустое пересечение";
                if (modelItem != null)
                {
                    displayName = modelItem.DisplayName;
                    // Создать новую группу по ключу элемента модели, если ещё не существует.
                    if (!groups.TryGetValue(modelItem, out currentGroup))
                    {
                        currentGroup = new ClashResultGroup();
                        if (string.IsNullOrEmpty(displayName)){ displayName = modelItem.Parent.DisplayName; }
                        if (string.IsNullOrEmpty(displayName)) { displayName = "Родитель без имени"; }
                        currentGroup.DisplayName = initialName + displayName;
                        groups.Add(modelItem, currentGroup);
                    }

                    // Добавить пересечение (копию) в найденную или созданную группу.
                    currentGroup.Children.Add(CreateTrackedCopy(result, sourceByCopy));
                }
                else
                {
                    // Нет элемента выбора — отдельная группа «Пустое пересечение».
                    System.Diagnostics.Debug.WriteLine("test");
                    ClashResultGroup oneClashResultGroup = new ClashResultGroup();
                    oneClashResultGroup.DisplayName = "Пустое пересечение";
                    oneClashResultGroup.Children.Add(CreateTrackedCopy(result, sourceByCopy));
                    emptyClashResultGroups.Add(oneClashResultGroup);
                }



            }

            List<ClashResultGroup> allGroups = groups.Values.ToList();
            allGroups.AddRange(emptyClashResultGroups);
            return allGroups;
        }

        private static List<ClashResultGroup> GroupByElementOfAGivenModel(List<ClashResult> results, GroupingMode mode, string initialName, Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            Dictionary<ModelItem, ClashResultGroup> groups = new Dictionary<ModelItem, ClashResultGroup>();
            ClashResultGroup currentGroup;
            List<ClashResultGroup> emptyClashResultGroups = new List<ClashResultGroup>();

            foreach (ClashResult result in results)
            {
                ModelItem rootModel = null;

                if (mode == GroupingMode.ModelA)
                {
                    if (result.CompositeItem1 != null)
                    {
                        rootModel = GetFileAncestor(result.CompositeItem1);
                    }
                    else if (result.CompositeItem2 != null)
                    {
                        rootModel = GetFileAncestor(result.CompositeItem2);
                    }
                }
                else if (mode == GroupingMode.ModelB)
                {
                    if (result.CompositeItem2 != null)
                    {
                        rootModel = GetFileAncestor(result.CompositeItem2);
                    }
                    else if (result.CompositeItem1 != null)
                    {
                        rootModel = GetFileAncestor(result.CompositeItem1);
                    }
                }

                string displayName = "Пустое пересечение";
                if (rootModel != null)
                {
                    displayName = rootModel.DisplayName;
                    // Создать группу по корневой модели (файлу), если ещё не создана.
                    if (!groups.TryGetValue(rootModel, out currentGroup))
                    {
                        currentGroup = new ClashResultGroup();
                        if (string.IsNullOrEmpty(displayName)) { displayName = "Модель без имени"; }
                        currentGroup.DisplayName = initialName + displayName;
                        groups.Add(rootModel, currentGroup);
                    }

                    // Добавить пересечение (копию) в найденную или созданную группу.
                    currentGroup.Children.Add(CreateTrackedCopy(result, sourceByCopy));
                }
                else
                {
                    // Не удалось определить файл модели — отдельная группа «Пустое пересечение».
                    System.Diagnostics.Debug.WriteLine("test");
                    ClashResultGroup oneClashResultGroup = new ClashResultGroup();
                    oneClashResultGroup.DisplayName = "Пустое пересечение";
                    oneClashResultGroup.Children.Add(CreateTrackedCopy(result, sourceByCopy));
                    emptyClashResultGroups.Add(oneClashResultGroup);
                }
            }

            List<ClashResultGroup> allGroups = groups.Values.ToList();
            allGroups.AddRange(emptyClashResultGroups);
            return allGroups;
        }

        private static List<ClashResultGroup> GroupByProperties(List<ClashResult> results, GroupingMode mode, string initialName, Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            Dictionary<string, ClashResultGroup> groups = new Dictionary<string, ClashResultGroup>();
            ClashResultGroup currentGroup;

            foreach (ClashResult result in results)
            {
                string clashProperty = null;

                if (mode == GroupingMode.ApprovedBy)
                {
                    clashProperty = result.ApprovedBy.ToString();
                }
                else if (mode == GroupingMode.AssignedTo)
                {
                    clashProperty = result.AssignedTo.ToString();
                }
                else if (mode == GroupingMode.Status)
                {
                    clashProperty = result.Status.ToString();
                }

                if (string.IsNullOrEmpty(clashProperty)) { clashProperty = "Unspecified"; }

                string displayLabel = clashProperty;
                if (string.Equals(clashProperty, "Unspecified", StringComparison.Ordinal))
                {
                    displayLabel = "Не указано";
                }
                else if (mode == GroupingMode.Status)
                {
                    displayLabel = LocalizeClashStatus(clashProperty);
                }

                if (!groups.TryGetValue(clashProperty, out currentGroup))
                {
                    currentGroup = new ClashResultGroup();
                    currentGroup.DisplayName = initialName + displayLabel;
                    groups.Add(clashProperty, currentGroup);
                }
                currentGroup.Children.Add(CreateTrackedCopy(result, sourceByCopy));
            }

            return groups.Values.ToList();
        }

        private static List<ClashResultGroup> GroupByElementName(List<ClashResult> results, string initialName, Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            Dictionary<string, ClashResultGroup> groups = new Dictionary<string, ClashResultGroup>(StringComparer.CurrentCultureIgnoreCase);
            ClashResultGroup currentGroup;

            foreach (ClashResult result in results)
            {
                // Читать имена с исходных элементов в документе (не с отсоединённых копий после CreateCopy).
                string nameA = GetModelItemDisplayName(result.CompositeItem1);
                string nameB = GetModelItemDisplayName(result.CompositeItem2);

                string groupName;
                if (!string.IsNullOrWhiteSpace(nameA) && !string.IsNullOrWhiteSpace(nameB))
                {
                    // Упорядочить пару имён, чтобы группа не зависела от порядка сторон A/B.
                    groupName = string.Compare(nameA, nameB, StringComparison.CurrentCultureIgnoreCase) <= 0
                        ? nameA + " — " + nameB
                        : nameB + " — " + nameA;
                }
                else if (!string.IsNullOrWhiteSpace(nameA))
                {
                    groupName = nameA;
                }
                else if (!string.IsNullOrWhiteSpace(nameB))
                {
                    groupName = nameB;
                }
                else
                {
                    groupName = "Пустое пересечение";
                }

                if (!groups.TryGetValue(groupName, out currentGroup))
                {
                    currentGroup = new ClashResultGroup();
                    currentGroup.DisplayName = initialName + groupName;
                    groups.Add(groupName, currentGroup);
                }

                currentGroup.Children.Add(CreateTrackedCopy(result, sourceByCopy));
            }

            return groups.Values.ToList();
        }

        private static List<ClashResultGroup> GroupByCustomProperty(
            List<ClashResult> results,
            string initialName,
            string propertyDisplayNameA,
            string propertyDisplayNameB,
            Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            string trimmedA = (propertyDisplayNameA ?? string.Empty).Trim();
            string trimmedB = (propertyDisplayNameB ?? string.Empty).Trim();
            string[] candidatesA = string.IsNullOrEmpty(trimmedA) ? Array.Empty<string>() : new[] { trimmedA };
            string[] candidatesB = string.IsNullOrEmpty(trimmedB) ? Array.Empty<string>() : new[] { trimmedB };
            const string emptyLabel = "Значения параметров не найдены";

            Dictionary<string, ClashResultGroup> groups = new Dictionary<string, ClashResultGroup>(StringComparer.CurrentCultureIgnoreCase);
            ClashResultGroup currentGroup;

            foreach (ClashResult result in results)
            {
                // Значения свойств — с элементов в документе (см. комментарий в GroupByElementName).
                string valueA = TryGetPropertyByDisplayNames(result.CompositeItem1, candidatesA);
                string valueB = TryGetPropertyByDisplayNames(result.CompositeItem2, candidatesB);

                string groupName;
                if (!string.IsNullOrWhiteSpace(valueA) && !string.IsNullOrWhiteSpace(valueB))
                {
                    groupName = string.Compare(valueA, valueB, StringComparison.CurrentCultureIgnoreCase) <= 0
                        ? valueA + " — " + valueB
                        : valueB + " — " + valueA;
                }
                else if (!string.IsNullOrWhiteSpace(valueA))
                {
                    groupName = valueA;
                }
                else if (!string.IsNullOrWhiteSpace(valueB))
                {
                    groupName = valueB;
                }
                else
                {
                    groupName = emptyLabel;
                }

                if (!groups.TryGetValue(groupName, out currentGroup))
                {
                    currentGroup = new ClashResultGroup();
                    currentGroup.DisplayName = initialName + groupName;
                    groups.Add(groupName, currentGroup);
                }

                currentGroup.Children.Add(CreateTrackedCopy(result, sourceByCopy));
            }

            return groups.Values.ToList();
        }

        /// <summary>
        /// Имена категории свойств «Объект» / Item / Element в Navisworks (там обычно «Семейство» и «Тип»).
        /// </summary>
        private static readonly string[] ObjectTabCategoryDisplayNames =
        {
            "Объект",
            "Item",
            "Element",
        };

        private static readonly string[] FamilyPropertyDisplayNames =
        {
            "Семейство",
            "Family",
            "Family Name",
        };

        private static readonly string[] TypePropertyDisplayNames =
        {
            "Тип",
            "Type",
            "Type Name",
        };

        private static List<ClashResultGroup> GroupByFamilyOrTypeName(List<ClashResult> results, string initialName, bool useFamily, Dictionary<ClashResult, ClashResult> sourceByCopy)
        {
            Dictionary<string, ClashResultGroup> groups = new Dictionary<string, ClashResultGroup>(StringComparer.CurrentCultureIgnoreCase);
            ClashResultGroup currentGroup;
            string[] candidates = useFamily ? FamilyPropertyDisplayNames : TypePropertyDisplayNames;
            string emptyLabel = useFamily ? "Семейство не указано" : "Тип не указан";

            foreach (ClashResult result in results)
            {
                // Семейство/тип читаются с исходных ModelItem в документе.
                string valueA = TryGetPropertyByDisplayNames(result.CompositeItem1, candidates);
                string valueB = TryGetPropertyByDisplayNames(result.CompositeItem2, candidates);

                string groupName;
                if (!string.IsNullOrWhiteSpace(valueA) && !string.IsNullOrWhiteSpace(valueB))
                {
                    // Стабильное имя группы при перестановке сторон A и B.
                    groupName = string.Compare(valueA, valueB, StringComparison.CurrentCultureIgnoreCase) <= 0
                        ? valueA + " — " + valueB
                        : valueB + " — " + valueA;
                }
                else if (!string.IsNullOrWhiteSpace(valueA))
                {
                    groupName = valueA;
                }
                else if (!string.IsNullOrWhiteSpace(valueB))
                {
                    groupName = valueB;
                }
                else
                {
                    groupName = emptyLabel;
                }

                if (!groups.TryGetValue(groupName, out currentGroup))
                {
                    currentGroup = new ClashResultGroup();
                    currentGroup.DisplayName = initialName + groupName;
                    groups.Add(groupName, currentGroup);
                }

                currentGroup.Children.Add(CreateTrackedCopy(result, sourceByCopy));
            }

            return groups.Values.ToList();
        }

        /// <summary>
        /// Строит кэш подписей подгрупп по исходным пересечениям до первого прохода группировки
        /// (чтобы второй уровень не читал свойства с «оторванных» копий после <see cref="CreateTrackedCopy"/>).
        /// </summary>
        /// <returns>Словарь «исходное пересечение → имя подгруппы» или <c>null</c>, если кэш не применим.</returns>
        private static Dictionary<ClashResult, string> BuildSubgroupNameCache(
            IEnumerable<ClashResult> results,
            GroupingMode subgroupingMode,
            string customPropertyDisplayNameForSelectionA,
            string customPropertyDisplayNameForSelectionB)
        {
            if (results == null)
            {
                return null;
            }

            bool supported = subgroupingMode == GroupingMode.ElementName
                || subgroupingMode == GroupingMode.FamilyName
                || subgroupingMode == GroupingMode.TypeName
                || subgroupingMode == GroupingMode.CustomProperty;
            if (!supported)
            {
                return null;
            }

            Dictionary<ClashResult, string> cache = new Dictionary<ClashResult, string>(new ReferenceEqualityComparer<ClashResult>());
            string[] customA = string.IsNullOrWhiteSpace(customPropertyDisplayNameForSelectionA)
                ? Array.Empty<string>()
                : new[] { customPropertyDisplayNameForSelectionA.Trim() };
            string[] customB = string.IsNullOrWhiteSpace(customPropertyDisplayNameForSelectionB)
                ? Array.Empty<string>()
                : new[] { customPropertyDisplayNameForSelectionB.Trim() };

            foreach (ClashResult result in results)
            {
                if (result == null)
                {
                    continue;
                }

                string key;
                switch (subgroupingMode)
                {
                    case GroupingMode.ElementName:
                        key = ComposePairGroupName(
                            GetModelItemDisplayName(result.CompositeItem1),
                            GetModelItemDisplayName(result.CompositeItem2),
                            "Пустое пересечение");
                        break;
                    case GroupingMode.FamilyName:
                        key = ComposePairGroupName(
                            TryGetPropertyByDisplayNames(result.CompositeItem1, FamilyPropertyDisplayNames),
                            TryGetPropertyByDisplayNames(result.CompositeItem2, FamilyPropertyDisplayNames),
                            "Семейство не указано");
                        break;
                    case GroupingMode.TypeName:
                        key = ComposePairGroupName(
                            TryGetPropertyByDisplayNames(result.CompositeItem1, TypePropertyDisplayNames),
                            TryGetPropertyByDisplayNames(result.CompositeItem2, TypePropertyDisplayNames),
                            "Тип не указан");
                        break;
                    case GroupingMode.CustomProperty:
                        key = ComposePairGroupName(
                            TryGetPropertyByDisplayNames(result.CompositeItem1, customA),
                            TryGetPropertyByDisplayNames(result.CompositeItem2, customB),
                            "Значения параметров не найдены");
                        break;
                    default:
                        key = string.Empty;
                        break;
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    key = GetEmptyLabelForMode(subgroupingMode);
                }

                cache[result] = key;
            }

            return cache;
        }

        private static string ComposePairGroupName(string valueA, string valueB, string emptyLabel)
        {
            if (!string.IsNullOrWhiteSpace(valueA) && !string.IsNullOrWhiteSpace(valueB))
            {
                return string.Compare(valueA, valueB, StringComparison.CurrentCultureIgnoreCase) <= 0
                    ? valueA + " — " + valueB
                    : valueB + " — " + valueA;
            }

            if (!string.IsNullOrWhiteSpace(valueA))
            {
                return valueA;
            }

            if (!string.IsNullOrWhiteSpace(valueB))
            {
                return valueB;
            }

            return emptyLabel;
        }

        private static string GetEmptyLabelForMode(GroupingMode mode)
        {
            switch (mode)
            {
                case GroupingMode.FamilyName:
                    return "Семейство не указано";
                case GroupingMode.TypeName:
                    return "Тип не указан";
                case GroupingMode.CustomProperty:
                    return "Значения параметров не найдены";
                case GroupingMode.ElementName:
                    return "Пустое пересечение";
                default:
                    return "Не указано";
            }
        }

        private static bool IsObjectPropertyCategory(PropertyCategory category)
        {
            string dn = (category.DisplayName ?? string.Empty).Trim();
            foreach (string tab in ObjectTabCategoryDisplayNames)
            {
                if (dn.Equals(tab, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string TryReadPropertyDisplayNameFromCategory(PropertyCategory category, string[] displayNameCandidates)
        {
            DataPropertyCollection properties;
            try
            {
                properties = category.Properties;
            }
            catch
            {
                return string.Empty;
            }
            foreach (string candidate in displayNameCandidates)
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                DataProperty property = null;
                try
                {
                    property = properties.FindPropertyByDisplayName(candidate);
                }
                catch
                {
                    continue;
                }
                if (property != null)
                {
                    string text = string.Empty;
                    try
                    {
                        text = ConvertVariantToTrimmedString(property.Value);
                    }
                    catch
                    {
                    }
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }

            foreach (DataProperty property in properties)
            {
                string displayName;
                try
                {
                    displayName = (property.DisplayName ?? string.Empty).Trim();
                }
                catch
                {
                    continue;
                }
                foreach (string candidate in displayNameCandidates)
                {
                    if (displayName.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        string text = string.Empty;
                        try
                        {
                            text = ConvertVariantToTrimmedString(property.Value);
                        }
                        catch
                        {
                        }
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
            }

            return string.Empty;
        }

        private static string TryGetPropertyByDisplayNames(ModelItem item, string[] displayNameCandidates)
        {
            if (item == null || displayNameCandidates == null || displayNameCandidates.Length == 0)
            {
                return string.Empty;
            }

            ModelItem current = GetSignificantAncestorOrSelf(item);
            while (current != null)
            {
                PropertyCategoryCollection categories;
                try
                {
                    categories = current.PropertyCategories;
                }
                catch
                {
                    break;
                }

                foreach (PropertyCategory category in categories)
                {
                    if (!IsObjectPropertyCategory(category))
                    {
                        continue;
                    }

                    string fromObject = TryReadPropertyDisplayNameFromCategory(category, displayNameCandidates);
                    if (!string.IsNullOrWhiteSpace(fromObject))
                    {
                        return fromObject;
                    }
                }

                foreach (PropertyCategory category in categories)
                {
                    if (IsObjectPropertyCategory(category))
                    {
                        continue;
                    }

                    string fromOther = TryReadPropertyDisplayNameFromCategory(category, displayNameCandidates);
                    if (!string.IsNullOrWhiteSpace(fromOther))
                    {
                        return fromOther;
                    }
                }

                try
                {
                    current = current.Parent;
                }
                catch
                {
                    break;
                }
            }

            return string.Empty;
        }

        private static string ConvertVariantToTrimmedString(VariantData value)
        {
            if (value == null)
            {
                return string.Empty;
            }

            try
            {
                if (value.IsDisplayString)
                {
                    return (value.ToDisplayString() ?? string.Empty).Trim();
                }

                if (value.IsIdentifierString)
                {
                    return (value.ToIdentifierString() ?? string.Empty).Trim();
                }

                if (value.IsNamedConstant)
                {
                    NamedConstant nc = value.ToNamedConstant();
                    if (nc != null)
                    {
                        string dn = nc.DisplayName;
                        if (!string.IsNullOrWhiteSpace(dn))
                        {
                            return dn.Trim();
                        }

                        string name = nc.Name;
                        if (!string.IsNullOrWhiteSpace(name))
                        {
                            return name.Trim();
                        }
                    }
                }

                return (value.ToString() ?? string.Empty).Trim();
            }
            catch
            {
                return string.Empty;
            }
        }

        #endregion


        #region Вспомогательные методы

        /// <summary>
        /// Заменяет дочерние элементы теста коллизий на сформированные группы и отдельные пересечения.
        /// </summary>
        private static void ProcessClashGroup(List<ClashResultGroup> clashGroups, List<ClashResult> ungroupedClashResults, ClashTest selectedClashTest)
        {
            using (Transaction tx = Application.MainDocument.BeginTransaction("SmartGroupClashes"))
            {
                ClashTest copiedClashTest = (ClashTest)selectedClashTest.CreateCopyWithoutChildren();
                // При замене теста в коллекции Navisworks может уничтожить старый экземпляр.
                // Если пользователь отменит операцию, подставляем полную резервную копию с детьми.
                ClashTest BackupTest = (ClashTest)selectedClashTest.CreateCopy();
                DocumentClash documentClash = Application.MainDocument.GetClash();
                int indexOfClashTest = documentClash.TestsData.Tests.IndexOf(selectedClashTest);
                documentClash.TestsData.TestsReplaceWithCopy(indexOfClashTest, copiedClashTest);

                int CurrentProgress = 0;
                int TotalProgress = ungroupedClashResults.Count + clashGroups.Count;
                Progress ProgressBar = Application.BeginProgress(
                    "Копирование результатов",
                    "Копирование результатов теста «" + selectedClashTest.DisplayName + "» в панель группировки…");
                try
                {
                    foreach (ClashResultGroup clashResultGroup in clashGroups)
                    {
                        if (ProgressBar.IsCanceled) break;
                        documentClash.TestsData.TestsAddCopy((GroupItem)documentClash.TestsData.Tests[indexOfClashTest], clashResultGroup);
                        CurrentProgress++;
                        ProgressBar.Update((double)CurrentProgress / TotalProgress);
                    }
                    foreach (ClashResult clashResult in ungroupedClashResults)
                    {
                        if (ProgressBar.IsCanceled) break;
                        documentClash.TestsData.TestsAddCopy((GroupItem)documentClash.TestsData.Tests[indexOfClashTest], clashResult);
                        CurrentProgress++;
                        ProgressBar.Update((double)CurrentProgress / TotalProgress);
                    }
                    if (ProgressBar.IsCanceled) documentClash.TestsData.TestsReplaceWithCopy(indexOfClashTest, BackupTest);
                    tx.Commit();
                }
                finally
                {
                    Application.EndProgress();
                }
            }
        }

        private static List<ClashResult> RemoveOneClashGroup(ref List<ClashResultGroup> clashResultGroups)
        {
            List<ClashResult> ungroupedClashResults = new List<ClashResult>();
            for (int i = clashResultGroups.Count - 1; i >= 0; i--)
            {
                ClashResultGroup group = clashResultGroups[i];
                if (group.Children.Count == 1)
                {
                    ClashResult result = (ClashResult)group.Children.FirstOrDefault();
                    ungroupedClashResults.Add(result);
                    clashResultGroups.RemoveAt(i);
                }
            }

            return ungroupedClashResults;
        }

        private static List<ClashResult> RemoveFullyFixedGroups(ref List<ClashResultGroup> clashResultGroups)
        {
            List<ClashResult> ungroupedClashResults = new List<ClashResult>();
            for (int i = clashResultGroups.Count - 1; i >= 0; i--)
            {
                ClashResultGroup group = clashResultGroups[i];
                var clashResults = group.Children.OfType<ClashResult>().ToList();
                if (clashResults.Count == 0)
                {
                    continue;
                }

                bool allFixed = clashResults.All(result => IsFixedStatus(result));
                if (allFixed)
                {
                    ungroupedClashResults.AddRange(clashResults);
                    clashResultGroups.RemoveAt(i);
                }
            }

            return ungroupedClashResults;
        }

        private static bool IsFixedStatus(ClashResult result)
        {
            // Сравнение по строковому представлению enum: в разных версиях Navisworks подписи в UI могут отличаться.
            string status = result.Status.ToString();
            return string.Equals(status, "Resolved", StringComparison.OrdinalIgnoreCase)
                || string.Equals(status, "Approved", StringComparison.OrdinalIgnoreCase);
        }

        private static string LocalizeClashStatus(string apiStatus)
        {
            if (string.IsNullOrWhiteSpace(apiStatus))
            {
                return "Не указано";
            }

            switch (apiStatus.Trim())
            {
                case "Active":
                    return "Активные";
                case "Approved":
                    return "Утверждённые";
                case "Assigned":
                    return "Назначенные";
                case "Resolved":
                    return "Исправленные";
                case "New":
                    return "Новые";
                case "Reviewed":
                    return "Проверенные";
                case "Tested":
                    return "Протестированные";
                default:
                    return apiStatus;
            }
        }

        private static IEnumerable<ClashResult> GetIndividualClashResults(ClashTest clashTest, bool keepExistingGroup)
        {
            for (var i = 0; i < clashTest.Children.Count; i++)
            {
                if (clashTest.Children[i].IsGroup)
                {
                    if (!keepExistingGroup)
                    {
                        IEnumerable<ClashResult> GroupResults = GetGroupResults((ClashResultGroup)clashTest.Children[i]);
                        foreach (ClashResult clashResult in GroupResults)
                        {
                            yield return clashResult;
                        }
                    }
                }
                else yield return (ClashResult)clashTest.Children[i];
            }
        }

        private static IEnumerable<ClashResultGroup> BackupExistingClashGroups(ClashTest clashTest)
        {
            for (var i = 0; i < clashTest.Children.Count; i++)
            {
                if (clashTest.Children[i].IsGroup)
                {
                    yield return (ClashResultGroup)clashTest.Children[i].CreateCopy();
                }
            }
        }

        private static IEnumerable<ClashResult> GetGroupResults(ClashResultGroup clashResultGroup)
        {
            for (var i = 0; i < clashResultGroup.Children.Count; i++)
            {
                yield return (ClashResult)clashResultGroup.Children[i];
            }
        }

        private static ModelItem GetSignificantAncestorOrSelf(ModelItem item)
        {
            ModelItem originalItem = item;
            ModelItem currentComposite = null;

            // Подняться по родителям к последнему составному (composite) предку — значимый для выбора объект.
            while (item != null)
            {
                ModelItem parent;
                try
                {
                    parent = item.Parent;
                }
                catch
                {
                    break;
                }

                if (parent == null)
                {
                    break;
                }

                item = parent;
                if (item.IsComposite) currentComposite = item;
            }

            return currentComposite ?? originalItem;
        }

        private static ModelItem GetFileAncestor(ModelItem item)
        {
            ModelItem originalItem = item;

            ModelItem currentComposite = null;

            // Найти ближайший предок, у которого есть модель (корень файла NWC и т.п.).
            while (item != null)
            {
                ModelItem parent;
                try
                {
                    parent = item.Parent;
                }
                catch
                {
                    break;
                }

                if (parent == null)
                {
                    break;
                }

                item = parent;
                if (item.HasModel)
                {
                    currentComposite = item;
                    break;
                }
            }

            return currentComposite ?? originalItem;
        }

        private static string GetModelItemDisplayName(ModelItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            ModelItem significantItem = GetSignificantAncestorOrSelf(item);
            string displayName = significantItem?.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = item.DisplayName;
            }

            return string.IsNullOrWhiteSpace(displayName) ? string.Empty : displayName.Trim();
        }

        #endregion

    }

    /// <summary>
    /// Режим правила группировки (соответствует пунктам комбобоксов в панели плагина).
    /// </summary>
    public enum GroupingMode
    {
        [Description("<Нет>")]
        None,
        [Description("Уровень")]
        Level,
        [Description("Пересечение осей")]
        GridIntersection,
        [Description("Выбор A")]
        SelectionA,
        [Description("Выбор B")]
        SelectionB,
        [Description("Модель A")]
        ModelA,
        [Description("Модель B")]
        ModelB,
        [Description("Назначено")]
        AssignedTo,
        [Description("Утверждено")]
        ApprovedBy,
        [Description("Статус")]
        Status,
        [Description("Имя элемента")]
        ElementName,
        [Description("Имя семейства")]
        FamilyName,
        [Description("Имя типа")]
        TypeName,
        [Description("Своё свойство")]
        CustomProperty
    }

}
