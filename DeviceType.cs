using System;
using System.Runtime.InteropServices;

namespace PowerChangerGUI
{
    // この PC がノートパソコン（またはタブレット）かどうかを調べる
    internal static class DeviceType
    {
        public static bool IsLaptop()
        {
            // 判定1: Windows が持っている「PC の役割」情報（メーカーがファームウェアに書いている値）
            try
            {
                int role = PowerDeterminePlatformRoleEx(POWER_PLATFORM_ROLE_V2);
                if (role == PlatformRoleMobile || role == PlatformRoleSlate)
                    return true;
            }
            catch (Exception)
            {
                // 古い Windows などで関数が無い場合は、判定2 だけで決める
            }

            // 判定2: バッテリーが付いているか
            // BatteryFlag: 128 = バッテリーなし, 255 = 不明
            if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status) &&
                status.BatteryFlag != 128 && status.BatteryFlag != 255)
                return true;

            return false;
        }

        // ───────── Windows API の呼び出し定義 ─────────

        private const uint POWER_PLATFORM_ROLE_V2 = 2;
        private const int PlatformRoleMobile = 2;  // ノートパソコン
        private const int PlatformRoleSlate = 8;   // タブレット・2in1

        [DllImport("powrprof.dll")]
        private static extern int PowerDeterminePlatformRoleEx(uint version);

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    }
}
