using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace SplunkActivityMonitor
{
    public partial class Form1 : Form
    {
        // Default with values from local test instance
        public Form1()
        {
            InitializeComponent();
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
