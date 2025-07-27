using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinFastGUI.Services
{
    public static class WimgApi
    {
        // Dosya Erişim Hakları
        public const uint WIM_GENERIC_READ = 0x80000000;
        public const uint WIM_GENERIC_WRITE = 0x40000000;

        // Dosya Oluşturma Davranışları
        public const uint WIM_CREATE_ALWAYS = 2; // Dosya varsa üzerine yaz, yoksa oluştur.

        // WIM Sıkıştırma Tipleri
        public enum WIM_COMPRESSION_TYPE
        {
            WIM_COMPRESS_NONE = 0,
            WIM_COMPRESS_XPRESS = 1,
            WIM_COMPRESS_LZX = 2, // Yüksek sıkıştırma
            WIM_COMPRESS_LZMS = 3
        }

        // WIMCreateFile ve WIMCaptureImage gibi fonksiyonlara geçirilen bayrakları tanımlar.
        public enum WIM_FLAG : uint
        {
            WIM_FLAG_NONE = 0x00000000,
            WIM_FLAG_VERIFY = 0x00000002, // Dosya bütünlüğünü doğrula
            WIM_FLAG_NO_FILEACL = 0x00000020 // Dosya ACL'lerini yakalama
        }

        // WIM Mesaj Tipleri (Callback tarafından alınan MessageId için)
        public enum WIM_MSG
        {
            WIM_MSG_PROGRESS = 2,
            WIM_MSG_ERROR = 4,
            WIM_MSG_WARNING = 11,
            WIM_MSG_SCANNING_FILES = 0,
            WIM_MSG_FILEINFO = 5,
            WIM_MSG_CAPTURING_FILES = 1,
            WIM_MSG_SUCCESS = 3,
        }

        // WIMSetReferenceFile için referans tipi
        public enum WIM_REFERENCE_TYPE : uint
        {
            WIM_REFERENCE_EXCLUSION_LIST = 0x00000001, // Dışlama listesi
            WIM_REFERENCE_COMPRESSION_EXCLUSION_LIST = 0x00000002 // Sıkıştırma dışlama listesi
        }

        // WIM API Geri Dönüş Değerleri
        public const uint WIM_S_CONTINUE = 0; // İşleme devam et
        public const uint WIM_ACTION_SKIP = 1; // Bir dosyayı/işlemi atla

        // WIM API Callback Tanımı
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate uint WIMMessageCallback(uint MessageId, IntPtr wParam, IntPtr lParam, IntPtr UserData);

        // --- WIM API DllImport Tanımları ---
        [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr WIMCreateFile(
            string WimPath,
            uint DesiredAccess,
            uint CreationDisposition,
            uint FlagsAndAttributes,
            uint CompressionType,
            out uint CreationResult);

        [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WIMSetTemporaryPath(IntPtr WimHandle, string TempPath);

        [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint WIMRegisterMessageCallback(IntPtr WimHandle, WIMMessageCallback Callback, IntPtr UserData);

        [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WIMCaptureImage(IntPtr WimHandle, string Path, uint CaptureFlags);

        [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WIMUnregisterMessageCallback(IntPtr WimHandle, WIMMessageCallback Callback);

        [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WIMCloseHandle(IntPtr WimHandle);

        [DllImport("wimgapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WIMSetReferenceFile(IntPtr WimHandle, string Path, uint ReferenceType);
    }
}
