using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
#if VIRTUESKY_FIREBASE
using Firebase;
#endif
#if VIRTUESKY_FIREBASE_REMOTECONFIG
using Firebase.Extensions;
using Firebase.RemoteConfig;
#endif

using Newtonsoft.Json;
using RemoteConfigGenerator;
using VirtueSky.Pattern;
using Task = System.Threading.Tasks.Task;


namespace VirtueSky.RemoteConfigGenerated
{
    public class RemoteConfig : MonoBehaviour
    {
        public event Action OnRemoteConfigLoaded;
#if VIRTUESKY_FIREBASE_REMOTECONFIG
        private FirebaseRemoteConfig _fbRemoteConfigInstance;
#endif

        public bool enableRemoteSync = true;
        public static bool IsLoaded = false;
        public static bool IsFirebaseAppDependencyStatusAvailable { get; private set; } = false;

        public static event Action OnLoaded;

        protected void Awake()
        {
            PrepareLoad();
            IsLoaded = false;
#if VIRTUESKY_FIREBASE
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                if (task.Result == DependencyStatus.Available)
                {
                    try
                    {
                        LoadRemoteConfig();
                    }
                    catch (Exception ex)
                    {
                        Debug.Log(ex.ToString());
                    }

                    IsFirebaseAppDependencyStatusAvailable = true;
                }
                else
                {
                    Debug.LogError(String.Format("Could not resolve all Firebase dependencies: {0}", task.Result));
                }
            });
#endif

        }

        public void LoadRemoteConfig()
        {
#if VIRTUESKY_FIREBASE_REMOTECONFIG    
            _fbRemoteConfigInstance = FirebaseRemoteConfig.DefaultInstance;
            if (!enableRemoteSync)
            {
                EndLoad();
                return;
            }

            ActivateCachedValuesAndLoad();
#endif
        }

        #region Firebase

        private void ActivateCachedValuesAndLoad()
        {
#if VIRTUESKY_FIREBASE_REMOTECONFIG
            _fbRemoteConfigInstance.ActivateAsync().ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled)
                {
                    Debug.Log("Activate cached values canceled.");
                }
                else if (task.IsFaulted)
                {
                    Debug.Log("Activate cached values encountered an error.");
                }

                if (TryApplyActivatedValues())
                {
                    Debug.Log("Activated cached Remote Config values from a previous session. Waiting for remote fetch before EndLoad.");
                }
                else
                {
                    Debug.Log("No cached Remote Config values available. Using in-app defaults until remote fetch completes.");
                }

                FirebaseFetchDataAsync();
            });
#endif
        }

        private void FirebaseFetchDataAsync()
        {
#if VIRTUESKY_FIREBASE_REMOTECONFIG
            Debug.Log("Fetching data...");
            var setting = _fbRemoteConfigInstance.ConfigSettings;
            setting.MinimumFetchIntervalInMilliseconds = 0;
            _fbRemoteConfigInstance.SetConfigSettingsAsync(setting).ContinueWithOnMainThread(task =>
            {
                Task fetchTask = _fbRemoteConfigInstance.FetchAsync();
                fetchTask.ContinueWithOnMainThread(FirebaseFetchComplete);
            });
#endif
        }

        void FirebaseFetchComplete(Task fetchTask)
        {
            if (IsLoaded)
            {
                return;
            }

            if (fetchTask.IsCanceled)
            {
                Debug.Log("Fetch canceled.");
            }
            else if (fetchTask.IsFaulted)
            {
                Debug.Log("Fetch encountered an error.");
            }
            else if (fetchTask.IsCompleted)
            {
                Debug.Log("Fetch completed successfully!");
            }
#if VIRTUESKY_FIREBASE_REMOTECONFIG
            var info = _fbRemoteConfigInstance.Info;
            switch (info.LastFetchStatus)
            {
                case LastFetchStatus.Success:
                    _fbRemoteConfigInstance.ActivateAsync().ContinueWithOnMainThread(task =>
                    {
                        if (task.IsCanceled)
                        {
                            Debug.Log("Activate fetched values canceled.");
                        }
                        else if (task.IsFaulted)
                        {
                            Debug.Log("Activate fetched values encountered an error.");
                        }

                        if (TryApplyActivatedValues())
                        {
                            EndLoad();
                            Debug.Log(String.Format("Remote data loaded and ready with latest fetched values (last fetch time {0}).", info.FetchTime));
                        }
                        else
                        {
                            Debug.Log("Fetch succeeded but no activated Remote Config keys were available. Using current values.");
                            EndLoad();
                        }
                    });
                    break;
                case LastFetchStatus.Failure:
                    switch (info.LastFetchFailureReason)
                    {
                        case FetchFailureReason.Error:
                            Debug.Log("Fetch failed for unknown reason");
                            break;
                        case FetchFailureReason.Throttled:
                            Debug.Log("Fetch throttled until " + info.ThrottledEndTime);
                            break;
                    }

                    if (!IsLoaded)
                    {
                        Debug.Log("Continuing startup with the currently active Remote Config values or in-app defaults.");
                        EndLoad();
                    }

                    break;
                case LastFetchStatus.Pending:
                    Debug.Log("Latest Fetch call still pending.");

                    if (!IsLoaded)
                    {
                        EndLoad();
                    }

                    break;
            }
#endif
        }

        private bool TryApplyActivatedValues()
        {
#if VIRTUESKY_FIREBASE_REMOTECONFIG
            foreach (var _ in _fbRemoteConfigInstance.Keys)
            {
                // Remote Config đã được activate, các property sẽ tự động lấy giá trị mới
                Debug.Log("Remote Config values activated successfully");
                return true;
            }
#endif
            return false;
        }
        
        #endregion

        /// <summary>
        /// Reset loaded flag
        /// </summary>
        public void Reset()
        {
            IsLoaded = false;
        }
        
        public string ExportToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"InterTimeGap: {RemoteData.InterTimeGap}");
            sb.AppendLine($"StartLevelShowInter: {RemoteData.StartLevelShowInter}");
            return sb.ToString();
        }

        /// <summary>
        /// Prepare default values before loading
        /// </summary>
        private void PrepareLoad()
        {
        }

        /// <summary>
        /// Remote config load completed - Apply game-specific configurations
        /// </summary>
        public void EndLoad()
        {
            if (IsLoaded) return;
            IsLoaded = true;
            Debug.Log(ExportToString());
            OnLoaded?.Invoke();
        }
    }
}
