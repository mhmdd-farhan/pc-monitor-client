namespace PCMonitorClient
{
    public static class SharedData
    {
        public static bool logoutFlag = false;
        public static int startFlag = 0;

        public static int assetID = 0;
        public static string createdBy = "";

        public static int duration = 0;

        public static string pcName = "";
        public static int siteID = 0;
        public static string siteName = "";

        public static string curCulture = "en";

        public static byte[] key = new byte[32]
        {
            76, 42, 63, 48, 105, 126, 147, 168,
            189, 210, 231, 252, 17, 38, 59, 80,
            101, 122, 143, 164, 185, 206, 227, 248,
            29, 50, 71, 92, 113, 134, 155, 176
        };
        public static byte[] iv = new byte[16]
        {
            210, 231, 252, 17, 38, 59, 80, 132,
            101, 122, 143, 164, 185, 206, 58, 29
        };
    }
}
