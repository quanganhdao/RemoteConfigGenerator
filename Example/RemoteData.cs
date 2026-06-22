namespace VirtueSky.RemoteConfigGenerated
{
    using RemoteConfigGenerator;

    [RemoteConfigData(PrefsPrefix = "rc_")]
    public static partial class RemoteData
    {
        [RemoteConfigField(Key = "inter_time_gap")]
        public static int InterTimeGap = 30;
        
        [RemoteConfigField(Key = "start_level_show_inter")]
        public static int StartLevelShowInter = 15;
    }
}