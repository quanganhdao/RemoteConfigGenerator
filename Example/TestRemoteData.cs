namespace VirtueSky.RemoteConfigGenerated
{
    using RemoteConfigGenerator;

    [RemoteConfigData(PrefsPrefix = "rc_")]
    public static partial class TestRemoteData
    {
        [RemoteConfigField(Key = "num_rewarded_life")]
        public static int NumRewardedLife = 3;

        [RemoteConfigField(Key = "user_name")]
        public static string UserName = "Guest";

        [RemoteConfigField(Key = "price_multiplier")]
        public static float PriceMultiplier = 1.5f;

        [RemoteConfigField(Key = "is_premium")]
        public static bool IsPremium = false;

        [RemoteConfigField(Key = "max_score")]
        public static long MaxScore = 999999L;
    }
}
