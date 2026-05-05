using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Interop;

namespace SmartGroupClashes
{
    /// <summary>
    /// Контейнер WinForms: встраивает WPF-интерфейс <see cref="SmartGroupClashesInterface"/> через <see cref="ElementHost"/>.
    /// </summary>
    public partial class SmartGroupClashesHostingControl : UserControl
    {
        private ElementHost _ctrlHost;
        private SmartGroupClashesInterface _wpfAddressCtrl;
        private Panel _hostPanel;

        /// <summary>Создаёт контейнер и подключает обработчик загрузки.</summary>
        public SmartGroupClashesHostingControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// По событию Load создаёт <see cref="ElementHost"/> и размещает в нём WPF-панель плагина.
        /// </summary>
        private void Form1_Load(object sender, EventArgs e)
        {
            _ctrlHost = new ElementHost();
            this.Controls.Add(_ctrlHost);
            _ctrlHost.Dock = DockStyle.Fill;

            _wpfAddressCtrl = new SmartGroupClashesInterface();
            _wpfAddressCtrl.InitializeComponent();
            _ctrlHost.Child = _wpfAddressCtrl;
            _ctrlHost.AutoSize = false;
        }

        /// <inheritdoc />
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);

            // Родитель панели Navisworks — обычно WinForms Panel; подписываемся на изменение размера.
            _hostPanel = (Panel)Parent;
            _hostPanel.SizeChanged += (hostPanel_SizeChanged);
            ResizeControl();
        }

        /// <summary>Обновляет размеры при изменении области панели Navisworks.</summary>
        private void hostPanel_SizeChanged(object sender, EventArgs e)
        {
            ResizeControl();
        }

        /// <summary>Подгоняет размер контейнера под доступную область родительской панели.</summary>
        public void ResizeControl()
        {
            Width = _hostPanel.Width;
            Height = _hostPanel.Height;
        }

    }
}
