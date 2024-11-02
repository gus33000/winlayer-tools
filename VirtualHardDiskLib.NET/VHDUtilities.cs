﻿using DiscUtils;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.Vhd;
using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace VirtualHardDiskLib
{
    public class VHDUtilities
    {
        /// <summary>
        /// Creates a temporary vhd of the given size in GB.
        /// The created VHD is dynamically allocated and is of type VHD (legacy)
        /// </summary>
        /// <param name="sizeInGB">The size of the VHD in GB</param>
        /// <returns>The path to the created vhd</returns>
        internal static string CreateVirtualDisk(long sizeInGB = 10)
        {
            long diskSize = sizeInGB * 1024 * 1024 * 1024;
            string tempVhd = Path.GetTempFileName();

            using Stream vhdStream = File.Create(tempVhd);
            using Disk disk = Disk.InitializeDynamic(vhdStream, DiscUtils.Streams.Ownership.Dispose, diskSize);

            GuidPartitionTable table = GuidPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsNtfs);
            //BiosPartitionTable table = BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsNtfs);
            //PartitionInfo ntfsPartition = table.Partitions[0];
            PartitionInfo ntfsPartition = table.Partitions[1];
            NtfsFileSystem.Format(ntfsPartition.Open(), "Windows UUP Medium", Geometry.FromCapacity(diskSize), ntfsPartition.FirstSector, ntfsPartition.SectorCount);

            return tempVhd;
        }

        public static string CreateDiffDisk(string OriginalVirtualDisk)
        {
            string tempVhd = Path.GetTempFileName();
            Disk.InitializeDifferencing(tempVhd, OriginalVirtualDisk).Dispose();
            return tempVhd;
        }

        internal static int MountVirtualDisk(string vhdfile)
        {
            var handle = IntPtr.Zero;

            // open disk handle
            var openParameters = new NativeMethods.OPEN_VIRTUAL_DISK_PARAMETERS();
            openParameters.Version = NativeMethods.OPEN_VIRTUAL_DISK_VERSION.OPEN_VIRTUAL_DISK_VERSION_1;
            openParameters.Version1.RWDepth = NativeMethods.OPEN_VIRTUAL_DISK_RW_DEPTH_DEFAULT;

            var openStorageType = new NativeMethods.VIRTUAL_STORAGE_TYPE();
            openStorageType.DeviceId = NativeMethods.VIRTUAL_STORAGE_TYPE_DEVICE_VHD;
            openStorageType.VendorId = NativeMethods.VIRTUAL_STORAGE_TYPE_VENDOR_MICROSOFT;

            var openResult = NativeMethods.OpenVirtualDisk(ref openStorageType, vhdfile,
                NativeMethods.VIRTUAL_DISK_ACCESS_MASK.VIRTUAL_DISK_ACCESS_ALL,
                NativeMethods.OPEN_VIRTUAL_DISK_FLAG.OPEN_VIRTUAL_DISK_FLAG_NONE, ref openParameters, ref handle);
            if (openResult != NativeMethods.ERROR_SUCCESS)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Native error {0}.",
                    openResult));

            // attach disk - permanently
            var attachParameters = new NativeMethods.ATTACH_VIRTUAL_DISK_PARAMETERS();
            attachParameters.Version = NativeMethods.ATTACH_VIRTUAL_DISK_VERSION.ATTACH_VIRTUAL_DISK_VERSION_1;
            var attachResult = NativeMethods.AttachVirtualDisk(handle, IntPtr.Zero,
                NativeMethods.ATTACH_VIRTUAL_DISK_FLAG.ATTACH_VIRTUAL_DISK_FLAG_PERMANENT_LIFETIME | NativeMethods.ATTACH_VIRTUAL_DISK_FLAG.ATTACH_VIRTUAL_DISK_FLAG_NO_DRIVE_LETTER, 0,
                ref attachParameters, IntPtr.Zero);
            if (attachResult != NativeMethods.ERROR_SUCCESS)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Native error {0}.",
                    attachResult));

            var num = _findVhdPhysicalDriveNumber(handle);

            // close handle to disk
            NativeMethods.CloseHandle(handle);

            return num;
        }

        internal static void DismountVirtualDisk(string vhdfile)
        {
            var handle = IntPtr.Zero;

            // open disk handle
            var openParameters = new NativeMethods.OPEN_VIRTUAL_DISK_PARAMETERS();
            openParameters.Version = NativeMethods.OPEN_VIRTUAL_DISK_VERSION.OPEN_VIRTUAL_DISK_VERSION_1;
            openParameters.Version1.RWDepth = NativeMethods.OPEN_VIRTUAL_DISK_RW_DEPTH_DEFAULT;

            var openStorageType = new NativeMethods.VIRTUAL_STORAGE_TYPE();
            openStorageType.DeviceId = NativeMethods.VIRTUAL_STORAGE_TYPE_DEVICE_VHD;
            openStorageType.VendorId = NativeMethods.VIRTUAL_STORAGE_TYPE_VENDOR_MICROSOFT;

            var openResult = NativeMethods.OpenVirtualDisk(ref openStorageType, vhdfile,
                NativeMethods.VIRTUAL_DISK_ACCESS_MASK.VIRTUAL_DISK_ACCESS_ALL,
                NativeMethods.OPEN_VIRTUAL_DISK_FLAG.OPEN_VIRTUAL_DISK_FLAG_NONE, ref openParameters, ref handle);
            if (openResult != NativeMethods.ERROR_SUCCESS)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Native error {0}.",
                    openResult));

            // detach disk
            var detachResult = NativeMethods.DetachVirtualDisk(handle,
                NativeMethods.DETACH_VIRTUAL_DISK_FLAG.DETACH_VIRTUAL_DISK_FLAG_NONE, 0);
            if (detachResult != NativeMethods.ERROR_SUCCESS)
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, "Native error {0}.",
                    detachResult));

            // close handle to disk
            NativeMethods.CloseHandle(handle);
        }

        private static int _findVhdPhysicalDriveNumber(IntPtr vhdHandle)
        {
            int driveNumber;
            int bufferSize = 260;
            StringBuilder vhdPhysicalPath = new StringBuilder(bufferSize);

            NativeMethods.GetVirtualDiskPhysicalPath(vhdHandle, ref bufferSize, vhdPhysicalPath);
            Int32.TryParse(Regex.Match(vhdPhysicalPath.ToString(), @"\d+").Value, out driveNumber);
            return driveNumber;
        }

        private static string _findVhdVolumePath(int vhdPhysicalDrive)
        {
            StringBuilder volumeName = new StringBuilder(260);
            IntPtr findVolumeHandle;
            IntPtr volumeHandle;
            NativeMethods.STORAGE_DEVICE_NUMBER deviceNumber = new NativeMethods.STORAGE_DEVICE_NUMBER();
            uint bytesReturned = 0;
            bool found = false;

            findVolumeHandle = NativeMethods.FindFirstVolume(volumeName, volumeName.Capacity);
            do
            {
                int backslashPos = volumeName.Length - 1;
                if (volumeName[backslashPos] == '\\')
                {
                    volumeName.Length--;
                }
                volumeHandle = NativeMethods.CreateFile(volumeName.ToString(), 0, NativeMethods.FILE_SHARE_MODE_FLAGS.FILE_SHARE_READ | NativeMethods.FILE_SHARE_MODE_FLAGS.FILE_SHARE_WRITE,
                    IntPtr.Zero, NativeMethods.CREATION_DISPOSITION_FLAGS.OPEN_EXISTING, 0, IntPtr.Zero);
                if (volumeHandle == NativeMethods.INVALID_HANDLE_VALUE)
                {
                    continue;
                }

                NativeMethods.DeviceIoControl(volumeHandle, NativeMethods.IO_CONTROL_CODE.STORAGE_DEVICE_NUMBER, IntPtr.Zero, 0,
                    ref deviceNumber, (uint)Marshal.SizeOf(deviceNumber), ref bytesReturned, IntPtr.Zero);

                if (deviceNumber.deviceNumber == vhdPhysicalDrive)
                {
                    found = true;
                    break;
                }
            } while (NativeMethods.FindNextVolume(findVolumeHandle, volumeName, volumeName.Capacity));
            NativeMethods.FindVolumeClose(findVolumeHandle);
            return found ? volumeName.ToString() : ""; //when It returns "" then the error occurs
        }

        private static void _mountVhdToDriveLetter(string vhdVolumePath, string mountPoint)
        {
            if (vhdVolumePath[vhdVolumePath.Length - 1] != '\\')
            {
                vhdVolumePath += '\\';
            }

            if (!NativeMethods.SetVolumeMountPoint(mountPoint, vhdVolumePath))
            {
                throw new Exception("The VHD cannot be accessed [SetVolumeMountPoint failed]");
            }
        }

        internal static void AttachDriveLetterToDiskAndPartitionId(int diskid, int partid, char driveletter)
        {
            RemoveFileExplorerAutoRun(driveletter);
            var volpath = _findVhdVolumePath(diskid);
            _mountVhdToDriveLetter(volpath, driveletter + ":\\");
        }

        /// <summary>
        /// Removing file explorer auto run for the given DriveLetter so that when a vhd is mounted file explorer doesn't open
        /// </summary>
        /// <param name="DriveLetter"></param>
        private static void RemoveFileExplorerAutoRun(char DriveLetter)
        {
            var KeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer";
            RegistryKey AutoRunKey = Registry.CurrentUser.OpenSubKey(KeyPath, true);
            var DriveLetterValue = DriveLetter - 'A';

            if (AutoRunKey != null)
            {
                RemoveFileExplorerAutoRun(AutoRunKey, DriveLetterValue);
            }
            else // create key as it does not exist
            {
                AutoRunKey = Registry.CurrentUser.CreateSubKey(KeyPath);
                RemoveFileExplorerAutoRun(AutoRunKey, DriveLetterValue);
            }
        }

        private static void RemoveFileExplorerAutoRun(RegistryKey AutoRunKey, int DriveLetterValue)
        {
            if (AutoRunKey != null)
            {
                AutoRunKey.SetValue("NoDriveTypeAutoRun", DriveLetterValue);
                AutoRunKey.Close();
            }
        }
    }
}