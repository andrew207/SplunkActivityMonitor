using System;
using System.Windows.Forms;

namespace SplunkActivityMonitor
{
    public partial class Form1 : Form
    {
        // Default with values from local test instance
        public Form1(bool EnableForegroundWindowMonitoring, bool EnableUSBMonitoring)
        {
            InitializeComponent(EnableForegroundWindowMonitoring, EnableUSBMonitoring);
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
        ~Form1()
        {
            Dispose(false);
        }
    }
}
