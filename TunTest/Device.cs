using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace Tun2Any
{
    class Device
    {
        private const string UsermodeDeviceSpace = "\\\\.\\Global\\";
        private object thisLock = new object();

        string deviceName;
        IntPtr device;
        FileStream stream;
        //
        // Returns the tap device guid list
        //
		private static List<string> GetGuidList()
		{
			const string AdapterKey = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4D36E972-E325-11CE-BFC1-08002BE10318}";
			RegistryKey regAdapters = Registry.LocalMachine.OpenSubKey ( AdapterKey, true);
			string[] keyNames = regAdapters.GetSubKeyNames();
			List<string> devGuid = new List<string>();
			foreach(string x in keyNames)
			{
                if (x == "Properties") continue;
				RegistryKey regAdapter = regAdapters.OpenSubKey(x);
				object id = regAdapter.GetValue("ComponentId");
				if (id != null && id.ToString() == "tap0901") devGuid.Add(regAdapter.GetValue("NetCfgInstanceId").ToString());
			}
			return devGuid;
		}
        //
        // Returns the device guid by name
        //
        private static string GetGuidByName(string name)
        {
            List<string> deviceGuidList = Device.GetGuidList();
            foreach(string guid in deviceGuidList)
            {
                if (Device.HumanName(guid) == name)
                {
                    return guid;
                }
            }

            throw new ArgumentNullException();
        }
		//
		// Returns the device name from the Control panel based on GUID
		//
		private static string HumanName(string guid)
		{
			const string ConnectionKey = "SYSTEM\\CurrentControlSet\\Control\\Network\\{4D36E972-E325-11CE-BFC1-08002BE10318}";
			if (guid != "")
			{
				RegistryKey regConnection = Registry.LocalMachine.OpenSubKey ( ConnectionKey + "\\" + guid + "\\Connection", true);
				object id = regConnection.GetValue("Name");
				if (id != null) return id.ToString();
			}
			return "";
		}

        private static IntPtr openDeviceByGuid(string guid)
        {
			return CreateFile(UsermodeDeviceSpace+guid+".tap",FileAccess.ReadWrite,
				FileShare.ReadWrite,0,FileMode.Open,FILE_ATTRIBUTE_SYSTEM | FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        }

        public Device(string name)
        {
            string deviceGuid = GetGuidByName(name);
            device = openDeviceByGuid(deviceGuid);
            deviceName = name;
        }

        public int getMTU()
        {
            var adapter = NetworkInterface.GetAllNetworkInterfaces().Where(i => i.Name == this.deviceName).First();
            IPInterfaceProperties adapterProperties = adapter.GetIPProperties();
            IPv4InterfaceProperties p = adapterProperties.GetIPv4Properties();
            return p.Mtu;
        }

        public FileStream getStream()
        {
            lock (thisLock)
            {
                if (stream != null)
                {
                    return stream;
                }

                SafeFileHandle handleValue = null;
                handleValue = new SafeFileHandle(device, true);

                stream = new FileStream(handleValue, FileAccess.ReadWrite,  10000, true);

                return stream;
            }
        }

        public void setMediaStatusAsConnected()
        {
			int len;
			IntPtr pstatus = Marshal.AllocHGlobal(4);
			Marshal.WriteInt32(pstatus, 1);
			DeviceIoControl(device, TAP_CONTROL_CODE (6, METHOD_BUFFERED) /* TAP_IOCTL_SET_MEDIA_STATUS */, pstatus, 4,
					pstatus, 4, out len, IntPtr.Zero);
        }

        public void setTunMode(string ip, string netmask)
        {
            int len;
			IntPtr ptun = Marshal.AllocHGlobal(12);
            int ipInt = BitConverter.ToInt32(IPAddress.Parse(ip).GetAddressBytes(), 0);
            int netmaskInt = BitConverter.ToInt32(IPAddress.Parse(netmask).GetAddressBytes(), 0);
			Marshal.WriteInt32(ptun, 0, ipInt);
			Marshal.WriteInt32(ptun, 4, ipInt & netmaskInt);
			Marshal.WriteInt32(ptun, 8, netmaskInt);
			DeviceIoControl (device, TAP_CONTROL_CODE (10, METHOD_BUFFERED) /* TAP_IOCTL_CONFIG_TUN */, ptun, 12,
				ptun, 12, out len, IntPtr.Zero);
        }

		private static uint CTL_CODE(uint DeviceType, uint Function, uint Method, uint Access)
		{
			return ((DeviceType << 16) | (Access << 14) | (Function << 2) | Method);
		}

		private static uint TAP_CONTROL_CODE(uint request, uint method)
		{
			return CTL_CODE (FILE_DEVICE_UNKNOWN, request, method, FILE_ANY_ACCESS);
		}
		private const uint METHOD_BUFFERED = 0;
		private const uint FILE_ANY_ACCESS = 0;
		private const uint FILE_DEVICE_UNKNOWN = 0x00000022;
		
		[DllImport("Kernel32.dll", /* ExactSpelling = true, */ SetLastError = true, CharSet = CharSet.Auto)]
		static extern IntPtr CreateFile(
			string filename,
			[MarshalAs(UnmanagedType.U4)]FileAccess fileaccess,
			[MarshalAs(UnmanagedType.U4)]FileShare fileshare,
			int securityattributes,
			[MarshalAs(UnmanagedType.U4)]FileMode creationdisposition,
			int flags,
			IntPtr template);
		const int FILE_ATTRIBUTE_SYSTEM = 0x4;
		const int FILE_FLAG_OVERLAPPED = 0x40000000;

		[DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Auto)]
		static extern bool DeviceIoControl(IntPtr hDevice, uint dwIoControlCode,
			IntPtr lpInBuffer, uint nInBufferSize,
			IntPtr lpOutBuffer, uint nOutBufferSize,
			out int lpBytesReturned, IntPtr lpOverlapped);
    }
}
