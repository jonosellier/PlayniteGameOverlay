using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PlayniteGameOverlay
{
    // Core Audio enumeration is public Windows API. Setting the default endpoint
    // uses the Windows PolicyConfig COM interface, which is not a documented SDK API.
    internal static class AudioOutputService
    {
        internal sealed class Output
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public bool IsDefault { get; set; }
        }

        internal static List<Output> GetOutputs()
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDeviceCollection collection = null;
            IMMDevice current = null;
            try
            {
                string defaultId = null;
                int result = enumerator.GetDefaultAudioEndpoint(0, 1, out current);
                if (result >= 0) Marshal.ThrowExceptionForHR(current.GetId(out defaultId));
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out collection));
                uint count; Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                var outputs = new List<Output>();
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null; IPropertyStore properties = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                        string id; Marshal.ThrowExceptionForHR(device.GetId(out id));
                        Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                        var key = new PropertyKey { FormatId = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                        PropVariant value;
                        Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out value));
                        string name;
                        try { name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : id; }
                        finally { PropVariantClear(ref value); }
                        outputs.Add(new Output { Id = id, Name = name ?? id, IsDefault = id == defaultId });
                    }
                    finally { Release(properties); Release(device); }
                }
                return outputs;
            }
            finally { Release(current); Release(collection); Release(enumerator); }
        }

        internal static void SetDefault(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Select an audio output.", nameof(id));
            if (!GetOutputs().Exists(d => d.Id == id)) throw new InvalidOperationException("This audio output is no longer available.");
            var policy = (IPolicyConfig)new PolicyConfig();
            try
            {
                for (int role = 0; role < 3; role++) Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
            }
            finally { Release(policy); }
            if (!GetOutputs().Exists(d => d.Id == id && d.IsDefault)) throw new InvalidOperationException("Windows did not confirm the audio output change.");
        }

        private static void Release(object instance) { if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance); }
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid FormatId; public uint Id; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
            [FieldOffset(8)] public IntPtr Pointer;
        }
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        }
        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr instance);
            [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        }
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] private class PolicyConfig { }
        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            // Unused slots preserve the native vtable order.
            void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
            void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
            void GetPropertyValue(); void SetPropertyValue();
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        }
    }
}
