using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Navisworks.Api.Plugins;

namespace SmartGroupClashes
{
    /// <summary>
    /// Обработчик команд ленты: открывает и скрывает панель <see cref="SmartGroupClashesPane"/>.
    /// </summary>
    [Plugin("SmartGroupClashes", "SmartGroupClashes", DisplayName = "Группировать\nпересечения")]
    [Strings("SmartGroupClashes.name")]
    [RibbonLayout("SmartGroupClashes.xaml")]
    [RibbonTab("ID_SmartGroupClashesTab",
        DisplayName = "Группировка пересечений")]
    [Command("ID_SmartGroupClashesButton",
             Icon = "SmartGroupClashesIcon_Small.ico", LargeIcon = "SmartGroupClashesIcon_Large.ico",
             DisplayName = "Группировать\nпересечения")]

    internal class RibbonHandler : CommandHandlerPlugin
    {
        /// <inheritdoc />
        public override int ExecuteCommand(string commandId, params string[] parameters)
        {
            if (Autodesk.Navisworks.Api.Application.IsAutomated)
            {
                throw new InvalidOperationException("Недопустимо при запуске через автоматизацию.");
            }

            // Найти запись плагина панели и переключить её видимость.
            PluginRecord pr = Autodesk.Navisworks.Api.Application.Plugins.FindPlugin("SmartGroupClashes.SmartGroupClashesPane.SmartGroupClashes");

            if (pr != null && pr is DockPanePluginRecord && pr.IsEnabled)
            {
                if (pr.LoadedPlugin == null)
                {
                    pr.LoadPlugin();
                }

                DockPanePlugin dpp = pr.LoadedPlugin as DockPanePlugin;
                if (dpp != null)
                {
                    dpp.Visible = !dpp.Visible;
                }
            }

            return 0;
        }

        /// <inheritdoc />
        public override CommandState CanExecuteCommand(String commandId)
        {
            CommandState state = new CommandState();
            state.IsVisible = true;
            state.IsEnabled = true;
            state.IsChecked = true;

            return state;
        }

        /// <inheritdoc />
        public override bool CanExecuteRibbonTab(string name)
        {
            return true;
        }


    }
}


