using System;
using System.IO;
using System.Security.AccessControl;
using ICSharpCode.SharpZipLib.GZip;
using ICSharpCode.SharpZipLib.Tar;
using System.Globalization;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace LayerConverter
{
    public static class LayerExpander
    {
        private static void ExpandArchive(string archivePath, string tempPath)
        {
            using GZipInputStream gzipInputStream = new(File.OpenRead(archivePath));
            using ExtTarInputStream extTarInputStream = new(gzipInputStream);

            ExtTarEntry extTarEntry;

            while ((extTarEntry = extTarInputStream.GetNextExtEntry()) != null)
            {
                if (extTarEntry.TarHeader.TypeFlag == TarHeader.LF_LINK)
                {
                    extTarEntry.TarHeader.Name = extTarEntry.TarHeader.Name.Replace("/", "\\");
                    extTarEntry.TarHeader.LinkName = extTarEntry.TarHeader.LinkName.Replace("/", "\\");

                    Console.WriteLine("Linking: " + extTarEntry.TarHeader.LinkName + " -> " + extTarEntry.TarHeader.Name);

                    if (extTarEntry.IsDirectory)
                    {
                        if (!Directory.Exists(Path.Combine(tempPath, extTarEntry.TarHeader.Name)))
                            JunctionPoint.Create(Path.Combine(tempPath, extTarEntry.TarHeader.Name), Path.Combine(tempPath, extTarEntry.TarHeader.LinkName), true);
                    }
                    else
                    {
                        if (!File.Exists(Path.Combine(tempPath, extTarEntry.TarHeader.Name)))
                        {
                            bool result = CreateHardLink(Path.Combine(tempPath, extTarEntry.TarHeader.Name), Path.Combine(tempPath, extTarEntry.TarHeader.LinkName), IntPtr.Zero);
                            if (!result)
                            {
                                throw new Win32Exception();
                            }
                        }
                    }
                }
                else
                {
                    extTarEntry.Name = extTarEntry.Name.Replace("/", "\\");

                    Console.WriteLine("Expanding: " + extTarEntry.Name);

                    if (extTarEntry.IsDirectory)
                    {
                        if (!Directory.Exists(Path.Combine(tempPath, extTarEntry.Name)))
                            Directory.CreateDirectory(Path.Combine(tempPath, extTarEntry.Name));
                    }
                    else
                    {
                        if (!File.Exists(Path.Combine(tempPath, extTarEntry.Name)))
                        {
                            using FileStream fs = File.Create(Path.Combine(tempPath, extTarEntry.Name));
                            extTarInputStream.CopyEntryContents(fs);
                        }
                    }
                }
            }
        }

        private static void ApplyArchiveMetadata(string archivePath, string tempPath)
        {
            Console.WriteLine("Applying metdata");

            using GZipInputStream gzipInputStream = new(File.OpenRead(archivePath));
            using ExtTarInputStream extTarInputStream = new(gzipInputStream);

            ExtTarEntry extTarEntry;

            while ((extTarEntry = extTarInputStream.GetNextExtEntry()) != null)
            {
                FileSystemInfo fileSystemInfo;

                if (extTarEntry.TarHeader.TypeFlag == TarHeader.LF_LINK)
                {
                    extTarEntry.TarHeader.Name = extTarEntry.TarHeader.Name.Replace("/", "\\");
                    extTarEntry.TarHeader.LinkName = extTarEntry.TarHeader.LinkName.Replace("/", "\\");

                    Console.WriteLine("Applying metadata to: " + extTarEntry.TarHeader.Name);

                    if (extTarEntry.IsDirectory)
                    {
                        fileSystemInfo = new DirectoryInfo(Path.Combine(tempPath, extTarEntry.TarHeader.Name));
                    }
                    else
                    {
                        fileSystemInfo = new FileInfo(Path.Combine(tempPath, extTarEntry.TarHeader.Name));
                    }
                }
                else
                {
                    extTarEntry.Name = extTarEntry.Name.Replace("/", "\\");

                    Console.WriteLine("Applying metadata to: " + extTarEntry.Name);

                    if (extTarEntry.IsDirectory)
                    {
                        fileSystemInfo = new DirectoryInfo(Path.Combine(tempPath, extTarEntry.Name));
                    }
                    else
                    {
                        fileSystemInfo = new FileInfo(Path.Combine(tempPath, extTarEntry.Name));
                    }
                }

                if (extTarEntry.Headers != null)
                {
                    if (extTarEntry.Headers.TryGetValue("LIBARCHIVE.creationtime", out var ctime))
                    {
                        try
                        {
                            fileSystemInfo.CreationTimeUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(double.Parse(ctime, CultureInfo.InvariantCulture));
                        }
                        catch { }
                    }

                    if (extTarEntry.Headers.TryGetValue("mtime", out var mtime))
                    {
                        try
                        {
                            fileSystemInfo.LastWriteTimeUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(double.Parse(mtime, CultureInfo.InvariantCulture));
                        }
                        catch { }
                    }

                    if (extTarEntry.Headers.TryGetValue("MSWINDOWS.fileattr", out var attr))
                    {
                        try
                        {
                            fileSystemInfo.Attributes = (FileAttributes)int.Parse(attr);
                        }
                        catch { }
                    }

                    if (extTarEntry.Headers.TryGetValue("atime", out var atime))
                    {
                        try
                        {
                            fileSystemInfo.LastAccessTimeUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(double.Parse(atime, CultureInfo.InvariantCulture));
                        }
                        catch { }
                    }
                }
            }
        }

        private static void ApplyArchiveSecurity(string archivePath, string tempPath)
        {
            Console.WriteLine("Applying security");

            new PrivilegeClass.Privilege(PrivilegeClass.Privilege.Restore).Enable();
            new PrivilegeClass.Privilege(PrivilegeClass.Privilege.TakeOwnership).Enable();

            using GZipInputStream gzipInputStream = new(File.OpenRead(archivePath));
            using ExtTarInputStream extTarInputStream = new(gzipInputStream);

            ExtTarEntry extTarEntry;

            while ((extTarEntry = extTarInputStream.GetNextExtEntry()) != null)
            {
                FileSystemInfo fileSystemInfo;

                if (extTarEntry.TarHeader.TypeFlag == TarHeader.LF_LINK)
                {
                    extTarEntry.TarHeader.Name = extTarEntry.TarHeader.Name.Replace("/", "\\");
                    extTarEntry.TarHeader.LinkName = extTarEntry.TarHeader.LinkName.Replace("/", "\\");

                    Console.WriteLine("Applying security to: " + extTarEntry.TarHeader.Name);

                    if (extTarEntry.IsDirectory)
                    {
                        fileSystemInfo = new DirectoryInfo(Path.Combine(tempPath, extTarEntry.TarHeader.Name));
                    }
                    else
                    {
                        fileSystemInfo = new FileInfo(Path.Combine(tempPath, extTarEntry.TarHeader.Name));
                    }
                }
                else
                {
                    extTarEntry.Name = extTarEntry.Name.Replace("/", "\\");

                    Console.WriteLine("Applying security to: " + extTarEntry.Name);

                    if (extTarEntry.IsDirectory)
                    {
                        fileSystemInfo = new DirectoryInfo(Path.Combine(tempPath, extTarEntry.Name));
                    }
                    else
                    {
                        fileSystemInfo = new FileInfo(Path.Combine(tempPath, extTarEntry.Name));
                    }
                }

                if (extTarEntry.Headers != null)
                {
                    if (extTarEntry.Headers.TryGetValue("MSWINDOWS.rawsd", out var rawSd))
                    {
                        try
                        {
                            if (fileSystemInfo is DirectoryInfo directoryInfo)
                            {
                                DirectorySecurity security = new(fileSystemInfo.FullName, AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
                                security.SetSecurityDescriptorBinaryForm(Convert.FromBase64String(rawSd));
                                directoryInfo.SetAccessControl(security);
                            }
                            else if (fileSystemInfo is FileInfo fileInfo)
                            {
                                FileSecurity security = new(fileSystemInfo.FullName, AccessControlSections.Owner | AccessControlSections.Group | AccessControlSections.Access);
                                security.SetSecurityDescriptorBinaryForm(Convert.FromBase64String(rawSd));
                                fileInfo.SetAccessControl(security);
                            }
                        } catch { }
                    }
                }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

        public static void ApplyLayer(string LayerPath, string ApplyDirectory)
        {
            if (!Directory.Exists(ApplyDirectory))
                Directory.CreateDirectory(ApplyDirectory);

            ExpandArchive(LayerPath, ApplyDirectory);
            ApplyArchiveMetadata(LayerPath, ApplyDirectory);
            ApplyArchiveSecurity(LayerPath, ApplyDirectory);
            Console.WriteLine("Done applying layer");

        }
    }
}
