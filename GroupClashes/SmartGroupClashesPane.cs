using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Navisworks.Api.Plugins;

namespace SmartGroupClashes
{
    /// <summary>
    /// Панель Navisworks с интерфейсом группировки (WinForms + WPF внутри <see cref="SmartGroupClashesHostingControl"/>).
    /// </summary>
    [Plugin("SmartGroupClashes.SmartGroupClashesPane", "SmartGroupClashes", DisplayName = "Группировать пересечения", ToolTip = "Группировать пересечения")]
    [DockPanePlugin(300, 380)]
    internal class SmartGroupClashesPane : DockPanePlugin
    {
        /// <inheritdoc />
        public override Control CreateControlPane()
        {
            SmartGroupClashesHostingControl control = new SmartGroupClashesHostingControl();

            control.Dock = DockStyle.Fill;

            // Инициализировать дочерние окна WinForms до показа панели.
            control.CreateControl();

            return control;
        }

        /// <inheritdoc />
        public override void DestroyControlPane(Control pane)
        {
            pane.Dispose();
        }
    }
}
