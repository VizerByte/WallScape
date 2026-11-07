using System;
using System.Threading;
using System.Windows.Forms;

namespace WallScape
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "WallScape.SingleInstance", out created))
            {
                if (!created)
                {
                    // Another instance is running: ask it to open the gallery.
                    uint message = Native.RegisterWindowMessage("WallScape.ShowGallery");
                    Native.PostMessage(new IntPtr(Native.HWND_BROADCAST), message, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                try
                {
                    Application.Run(new AppContext());
                }
                catch (Exception error)
                {
                    MessageBox.Show("WallScape stopped unexpectedly.\r\n\r\n" + error,
                        "WallScape", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
