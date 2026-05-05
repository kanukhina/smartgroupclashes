namespace SmartGroupClashes
{
    /// <summary>
    /// Режим группировки с русской подписью для списков в интерфейсе.
    /// </summary>
    public sealed class GroupingModeOption
    {
        /// <summary>Значение перечисления <see cref="GroupingMode"/>.</summary>
        public GroupingMode Mode { get; set; }

        /// <summary>Текст, показываемый пользователю в комбобоксе.</summary>
        public string DisplayName { get; set; }

        /// <summary>Возвращает локализованную подпись для режима (для списков UI).</summary>
        /// <param name="mode">Режим группировки.</param>
        /// <returns>Строка для отображения; для неизвестного значения — результат <see cref="object.ToString"/>.</returns>
        public static string GetDisplayName(GroupingMode mode)
        {
            switch (mode)
            {
                case GroupingMode.None:
                    return "<Нет>";
                case GroupingMode.Level:
                    return "Уровень";
                case GroupingMode.GridIntersection:
                    return "Пересечение осей";
                case GroupingMode.SelectionA:
                    return "Выбор A";
                case GroupingMode.SelectionB:
                    return "Выбор B";
                case GroupingMode.ModelA:
                    return "Модель A";
                case GroupingMode.ModelB:
                    return "Модель B";
                case GroupingMode.AssignedTo:
                    return "Назначено";
                case GroupingMode.ApprovedBy:
                    return "Утверждено";
                case GroupingMode.Status:
                    return "Статус";
                case GroupingMode.ElementName:
                    return "Имя элемента";
                case GroupingMode.FamilyName:
                    return "Имя семейства";
                case GroupingMode.TypeName:
                    return "Имя типа";
                case GroupingMode.CustomProperty:
                    return "Своё свойство";
                default:
                    return mode.ToString();
            }
        }
    }
}
