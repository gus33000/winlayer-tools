using System;
using System.Diagnostics;
using System.IO;
using Imaging;
using VirtualHardDiskLib;

namespace LayerConverter
{
    class Program
    {
        private static readonly WIMGImaging wim = new();

        static void Main(string[] args)
        {
            if (args.Length != 2)
            //if (args.Length != 3)
            {
                Console.WriteLine("Usage: tool.exe <path to downloaded file from winlayers> <path to output wim>"); //<short temp path>");
                return;
            }

            string archivePath = args[0];
            string outPath = args[1];
            //string tempPath = args[2];

            using (VirtualDiskSession session = new())
            {
            try
            {
                string tempPath = Path.Combine(session.GetMountedPath(), "C");
                LayerExpander.ApplyLayer(archivePath, tempPath);

                int prevperc = -1;
                string prevop = "";

                void callback(string SubOperation, int ProgressInPercentage, bool IsIndeterminate)
                {
                    if (prevperc == ProgressInPercentage && SubOperation == prevop)
                        return;

                    prevop = SubOperation;
                    prevperc = ProgressInPercentage;

                    string progress = IsIndeterminate ? "" : $" [Progress: {ProgressInPercentage}%]";
                    Console.WriteLine($"{progress} {SubOperation}");
                }

                wim.CaptureImage(outPath, "ContainerOS", "ContainerOS", "ContainerOS", Path.Combine(tempPath, "Files"), progressCallback: callback);
                wim.CaptureImage(outPath, "UtilityVM", "UtilityVM", "UtilityVM", Path.Combine(tempPath, "UtilityVM", "Files"), progressCallback: callback);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            /*TakeOwnDirectory(tempPath);
            TakeOwnDirectory(tempPath);
            try
            {
                Directory.Delete(tempPath, true);
            }
            catch { }*/
            }
        }

        static void TakeOwnDirectory(string path)
        {
            Process proc = new();
            proc.StartInfo = new ProcessStartInfo("cmd.exe", "/c takeown /f \"" + path + "\" && icacls \"" + path + "\" /grant *S-1-3-4:F /t /c /l");
            proc.StartInfo.UseShellExecute = false;
            proc.Start();
            proc.WaitForExit();
        }
    }
}
