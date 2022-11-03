using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace itch_butler_gui
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);


#if DEBUG
            Application.Run(new Form1());
#else
            try
            {
                Application.Run(new Form1());
            }
            catch (Exception e)
            {
                MessageBox.Show(string.Format("Fatal error. Exiting now.\n\n{0}", e.ToString()));
            }
#endif


        }
    }
}
